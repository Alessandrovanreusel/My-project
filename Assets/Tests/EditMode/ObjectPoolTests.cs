using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using CameraGame.Core;

namespace CameraGame.Tests
{
    /// <summary>
    /// Regression pins for <see cref="ObjectPool{T}"/>.
    ///
    /// Worth pinning because CLAUDE.md records where this project's bugs actually live: "Bugs in this
    /// project have hidden almost exclusively in reuse and in the second cycle onward, never in the first."
    /// A pool IS the reuse mechanism, so its second cycle is the exact place that sentence points at.
    ///
    /// ⚠️ EDIT-MODE LIMITS, STATED RATHER THAN HIDDEN. <c>ObjectPool.Clear()</c> calls
    /// <c>Object.Destroy</c>, which is not legal from edit mode (it needs <c>DestroyImmediate</c> there),
    /// so Clear() with live instances is NOT covered here and must not be "fixed" by weakening the test.
    /// It belongs in a PlayMode test. Everything below avoids that path deliberately.
    /// </summary>
    public class ObjectPoolTests
    {
        private readonly List<GameObject> _made = new List<GameObject>();

        /// <summary>A stand-in prefab. A plain GameObject's Transform satisfies <c>where T : Component</c>
        /// and needs no asset on disk.</summary>
        private Transform NewPrefab(string name = "PoolPrefab")
        {
            var go = new GameObject(name);
            _made.Add(go);
            return go.transform;
        }

        private void Track(Component c)
        {
            if (c != null) _made.Add(c.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _made)
                if (go != null) Object.DestroyImmediate(go);
            _made.Clear();
        }

        // Contract: "A null prefab means Create() would throw on every Get(). Fail soft: log once and leave
        // the pool inert (Get() returns null, Return()/Clear() no-op)."
        [Test]
        public void NullPrefab_LogsOnceAndLeavesThePoolInert()
        {
            LogAssert.Expect(LogType.Error, new Regex(@"\[Pool\].*null prefab"));

            var pool = new ObjectPool<Transform>(null);

            Assert.IsNull(pool.Get(), "an inert pool must hand back null rather than throwing");
            Assert.DoesNotThrow(() => pool.Return(null));
            Assert.DoesNotThrow(() => pool.Clear());
        }

        // Contract: "A negative prewarm is almost certainly a config slip; clamp instead of looping
        // negatively." The pool must simply be usable afterwards.
        [TestCase(-1)]
        [TestCase(-1000)]
        [TestCase(0)]
        public void NegativePrewarm_IsClampedAndThePoolStillWorks(int prewarm)
        {
            var pool = new ObjectPool<Transform>(NewPrefab(), prewarm);

            var item = pool.Get();
            Track(item);

            Assert.IsNotNull(item);
            Assert.IsTrue(item.gameObject.activeSelf, "Get() must hand back an ACTIVE instance");
        }

        // Contract: prewarmed instances start inactive, and Get() activates them.
        [Test]
        public void Prewarm_CreatesInactiveInstancesThatGetActivates()
        {
            var pool = new ObjectPool<Transform>(NewPrefab(), prewarm: 3);

            for (int i = 0; i < 3; i++)
            {
                var item = pool.Get();
                Track(item);
                Assert.IsNotNull(item);
                Assert.IsTrue(item.gameObject.activeSelf);
            }
        }

        // THE SECOND-CYCLE TEST. A pool that does not actually reuse is just a slower Instantiate, and the
        // failure is invisible — everything still works, only the NFR3 no-leak guarantee is gone.
        [Test]
        public void ReturnedInstance_IsHandedOutAgainRatherThanRecreated()
        {
            var pool = new ObjectPool<Transform>(NewPrefab());

            var first = pool.Get();
            Track(first);
            int firstId = first.GetInstanceID();

            pool.Return(first);
            Assert.IsFalse(first.gameObject.activeSelf, "Return() must deactivate");

            var second = pool.Get();
            Track(second);

            Assert.AreEqual(firstId, second.GetInstanceID(), "the pool did not reuse the returned instance");
            Assert.IsTrue(second.gameObject.activeSelf, "the reused instance must come back active");
        }

        // Several cycles, because one round trip proves less than the doc-comment claims.
        [Test]
        public void RepeatedGetReturnCycles_KeepReusingTheSameInstance()
        {
            var pool = new ObjectPool<Transform>(NewPrefab());

            var first = pool.Get();
            Track(first);
            int id = first.GetInstanceID();
            pool.Return(first);

            for (int cycle = 0; cycle < 10; cycle++)
            {
                var item = pool.Get();
                Assert.AreEqual(id, item.GetInstanceID(), $"stopped reusing on cycle {cycle}");
                pool.Return(item);
            }
        }

        // Contract: "Ignores null and double returns (logging a warning) so a misbehaving caller can't
        // corrupt the pool" — the named failure being "hand one instance to two callers, a classic
        // pool-corruption bug". So the test asserts the CORRUPTION cannot happen, not just that it warned.
        [Test]
        public void DoubleReturn_DoesNotHandTheSameInstanceToTwoCallers()
        {
            var pool = new ObjectPool<Transform>(NewPrefab());

            var item = pool.Get();
            Track(item);

            pool.Return(item);
            pool.Return(item);      // the mistake — warns, must be ignored

            var a = pool.Get();
            var b = pool.Get();
            Track(a);
            Track(b);

            Assert.AreNotEqual(a.GetInstanceID(), b.GetInstanceID(),
                "a double Return handed the same instance to two callers — the pool is corrupt");
        }

        [Test]
        public void ReturnNull_IsIgnored()
        {
            var pool = new ObjectPool<Transform>(NewPrefab());

            Assert.DoesNotThrow(() => pool.Return(null));

            var item = pool.Get();
            Track(item);
            Assert.IsNotNull(item, "the pool must still work after a null Return");
        }

        // Contract: "Skips idle instances that were destroyed while sitting in the pool (e.g. a scene
        // unload), so callers never receive a destroyed object." Unity overloads == so a destroyed object
        // compares equal to null — this pins that the pool honours that rather than handing one back.
        [Test]
        public void DestroyedIdleInstance_IsSkippedRatherThanHandedOut()
        {
            var pool = new ObjectPool<Transform>(NewPrefab());

            var item = pool.Get();
            pool.Return(item);

            // Kill it while it sits idle in the pool, the way a scene unload would.
            Object.DestroyImmediate(item.gameObject);

            var next = pool.Get();
            Track(next);

            Assert.IsNotNull(next, "the pool handed back a destroyed instance");
            Assert.IsTrue(next != null, "Unity's == overload must agree the instance is alive");
            Assert.IsTrue(next.gameObject.activeSelf);
        }

        // The same, but with a live instance behind the destroyed one, so the pool has to keep popping
        // rather than give up at the first corpse.
        [Test]
        public void DestroyedIdleInstance_DoesNotHideALiveOneBehindIt()
        {
            var pool = new ObjectPool<Transform>(NewPrefab());

            var a = pool.Get();
            var b = pool.Get();
            Track(a);
            Track(b);

            pool.Return(a);
            pool.Return(b);   // b is on top of the stack

            Object.DestroyImmediate(b.gameObject);

            var next = pool.Get();
            Track(next);

            Assert.IsNotNull(next);
            Assert.AreEqual(a.GetInstanceID(), next.GetInstanceID(),
                "the pool should have skipped the destroyed instance and reused the live one underneath");
        }
    }
}
