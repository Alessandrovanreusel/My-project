using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using CameraGame.World;

namespace CameraGame.Tests
{
    /// <summary>Story 2.2: TownBoundsConfig's fail-soft accessors (NFR8), its problem report, and the polygon helpers.</summary>
    public class TownBoundsConfigTests
    {
        private TownBoundsConfig _c;

        [SetUp] public void SetUp() => _c = ScriptableObject.CreateInstance<TownBoundsConfig>();
        [TearDown] public void TearDown() => Object.DestroyImmediate(_c);

        [Test]
        public void Defaults_HaveNoProblem()
        {
            Assert.IsFalse(_c.TryGetConfigProblem(out string p), p);
            Assert.AreEqual(TownBoundsConfig.DefaultBelowWorldY, _c.SafeBelowWorldY);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void NonFiniteBelowWorldY_FallsBack(float v)
        {
            _c.belowWorldY = v;
            Assert.AreEqual(TownBoundsConfig.DefaultBelowWorldY, _c.SafeBelowWorldY);
            Assert.IsTrue(_c.TryGetConfigProblem(out string p));
            StringAssert.Contains("belowWorldY", p);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void UnusableTimes_FallBack(float v)
        {
            _c.maxAirborneSeconds = v; _c.safeSampleInterval = v;
            Assert.AreEqual(TownBoundsConfig.DefaultMaxAirborneSeconds, _c.SafeMaxAirborneSeconds);
            Assert.AreEqual(TownBoundsConfig.DefaultSafeSampleInterval, _c.SafeSafeSampleInterval);
            Assert.IsTrue(_c.TryGetConfigProblem(out _));
        }

        [Test]
        public void NegativeOrInfiniteRestoreAge_FallsBack_ButZeroIsAllowed()
        {
            _c.minRestoreAge = 0f;
            Assert.AreEqual(0f, _c.SafeMinRestoreAge);
            _c.minRestoreAge = -1f;
            Assert.AreEqual(TownBoundsConfig.DefaultMinRestoreAge, _c.SafeMinRestoreAge);
            _c.minRestoreAge = float.PositiveInfinity;
            Assert.AreEqual(TownBoundsConfig.DefaultMinRestoreAge, _c.SafeMinRestoreAge);
        }

        [Test]
        public void ABakeRangeThatDoesNotRise_FallsBack()
        {
            _c.bakeYMin = 50f; _c.bakeYMax = 10f;
            Assert.AreEqual(TownBoundsConfig.DefaultBakeYMin, _c.SafeBakeYMin);
            Assert.AreEqual(TownBoundsConfig.DefaultBakeYMax, _c.SafeBakeYMax);
            Assert.IsTrue(_c.TryGetConfigProblem(out string p));
            StringAssert.Contains("bakeYMin", p);
        }

        [Test]
        public void ANonPolygon_FallsBackToTheMeasuredArea()
        {
            _c.playableArea = new[] { Vector2.zero, Vector2.one };
            Assert.AreSame(TownBoundsConfig.DefaultPlayableArea, _c.SafePlayableArea);
            _c.playableArea = new[] { Vector2.zero, new Vector2(1f, 0f), new Vector2(float.NaN, 1f) };
            Assert.AreSame(TownBoundsConfig.DefaultPlayableArea, _c.SafePlayableArea);
            Assert.IsTrue(_c.TryGetConfigProblem(out string p));
            StringAssert.Contains("playableArea", p);
        }

        [Test]
        public void Contains_IsTheEvenOddRule()
        {
            var square = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) };
            Assert.IsTrue(TownBoundsConfig.Contains(square, new Vector2(5, 5)));
            Assert.IsFalse(TownBoundsConfig.Contains(square, new Vector2(-1, 5)));
            Assert.IsFalse(TownBoundsConfig.Contains(square, new Vector2(5, 11)));
            Assert.IsFalse(TownBoundsConfig.Contains(null, new Vector2(5, 5)));
        }

        [Test]
        public void IsAllowed_RefusesNoGoAreas()
        {
            _c.noGoAreas = new[] { new TownBoundsConfig.Area { name = "pond", points = new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(10, 10), new Vector2(0, 10) } } };
            Assert.IsFalse(_c.IsAllowed(new Vector2(5, 5)));
            Assert.IsTrue(_c.IsAllowed(new Vector2(31, 19)));      // the pub
            Assert.IsFalse(_c.IsAllowed(new Vector2(0, 300)));     // the sea
        }

        [Test]
        public void TheShippedAsset_HasNoProblem_AndExcludesThePond()
        {
            var shipped = AssetDatabase.LoadAssetAtPath<TownBoundsConfig>("Assets/Data/World/TownBoundsConfig.asset");
            Assert.IsNotNull(shipped, "Assets/Data/World/TownBoundsConfig.asset is missing");
            Assert.IsFalse(shipped.TryGetConfigProblem(out string p), p);
            Assert.IsTrue(shipped.IsAllowed(new Vector2(31f, 19f)), "the pub must be inside the playable area");
            Assert.IsFalse(shipped.IsAllowed(new Vector2(-549f, -63f)), "the pond is a no-go area");
            Assert.Less(shipped.SafeBelowWorldY, -5f, "the floor of the world must sit below the lowest walkable ground (−5 m)");
            Assert.Greater(shipped.SafeBelowWorldY, -48f, "the floor of the world must sit above the buried block (tops −48 m)");
        }
    }
}
