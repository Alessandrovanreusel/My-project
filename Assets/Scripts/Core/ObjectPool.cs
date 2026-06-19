using System.Collections.Generic;
using UnityEngine;

namespace CameraGame.Core
{
    /// <summary>
    /// Minimal prefab-based pool for Components (e.g. EventActor). Used by EventManager (Story 1.6)
    /// so gameplay never Instantiate/Destroys in a loop — satisfies the GDD no-object-leak metric (NFR3).
    ///
    /// Hardened in Story 1.6 (its first real consumer): tolerant of destroyed idle instances, guards
    /// against null/double Return, validates the prefab, and exposes Clear() so the idle stack does not
    /// leak across scene loads.
    /// </summary>
    public class ObjectPool<T> where T : Component
    {
        private readonly T _prefab;
        private readonly Transform _parent;
        private readonly Stack<T> _idle = new();

        // Tracks which instances are currently idle (in the stack). Lets us detect a double-Return —
        // the same object handed back twice — which would otherwise hand one instance to two callers,
        // a classic pool-corruption bug.
        private readonly HashSet<T> _idleSet = new();

        // True only when the pool was constructed with a usable prefab. A null-prefab pool no-ops
        // every operation rather than throwing.
        private readonly bool _valid;

        public ObjectPool(T prefab, int prewarm = 0, Transform parent = null)
        {
            _prefab = prefab;
            _parent = parent;

            // A null prefab means Create() would throw on every Get(). Fail soft: log once and leave
            // the pool inert (Get() returns null, Return()/Clear() no-op).
            if (_prefab == null)
            {
                GameLog.Error("Pool", $"ObjectPool<{typeof(T).Name}> created with a null prefab — pool disabled.");
                _valid = false;
                return;
            }

            _valid = true;

            // A negative prewarm is almost certainly a config slip; clamp instead of looping negatively.
            if (prewarm < 0) prewarm = 0;

            for (int i = 0; i < prewarm; i++)
            {
                var item = Create();
                item.gameObject.SetActive(false);
                _idle.Push(item);
                _idleSet.Add(item);
            }
        }

        /// <summary>
        /// Returns an active instance from the pool, creating one if none are idle. Skips idle instances
        /// that were destroyed while sitting in the pool (e.g. a scene unload), so callers never receive
        /// a destroyed object.
        /// </summary>
        public T Get()
        {
            if (!_valid) return null;

            T item = null;

            // Pop until we find a live instance. Unity overloads == so a destroyed object compares
            // equal to null — that's the case we skip (recreating instead).
            while (_idle.Count > 0)
            {
                var candidate = _idle.Pop();
                _idleSet.Remove(candidate);
                if (candidate != null) { item = candidate; break; }
            }

            if (item == null) item = Create();

            item.gameObject.SetActive(true);
            return item;
        }

        /// <summary>
        /// Returns an instance to the pool and deactivates it. Ignores null and double returns (logging
        /// a warning) so a misbehaving caller can't corrupt the pool. SetActive(false) is the only reset
        /// applied here — per-instance state reset is the pooled object's own job (its OnEnable re-inits).
        /// </summary>
        public void Return(T item)
        {
            if (!_valid) return;

            if (item == null)
            {
                GameLog.Warn("Pool", $"Return(null) on ObjectPool<{typeof(T).Name}> — ignored.");
                return;
            }

            if (_idleSet.Contains(item))
            {
                GameLog.Warn("Pool", $"Double Return of '{item.name}' to ObjectPool<{typeof(T).Name}> — ignored.");
                return;
            }

            item.gameObject.SetActive(false);
            _idle.Push(item);
            _idleSet.Add(item);
        }

        /// <summary>
        /// Destroys all idle instances and empties the pool. Call on scene teardown (EventManager.OnDestroy)
        /// so the idle stack doesn't leak across scene loads (NFR3).
        /// </summary>
        public void Clear()
        {
            while (_idle.Count > 0)
            {
                var item = _idle.Pop();
                if (item != null) Object.Destroy(item.gameObject);
            }
            _idleSet.Clear();
        }

        private T Create() => Object.Instantiate(_prefab, _parent);
    }
}
