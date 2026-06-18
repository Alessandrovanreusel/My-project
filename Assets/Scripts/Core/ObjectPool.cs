using System.Collections.Generic;
using UnityEngine;

namespace CameraGame.Core
{
    /// <summary>
    /// Minimal prefab-based pool for Components (e.g. EventActor). Used by EventManager (Story 1.6)
    /// so gameplay never Instantiate/Destroys in a loop — satisfies the GDD no-object-leak metric (NFR3).
    /// </summary>
    public class ObjectPool<T> where T : Component
    {
        private readonly T _prefab;
        private readonly Transform _parent;
        private readonly Stack<T> _idle = new();

        public ObjectPool(T prefab, int prewarm = 0, Transform parent = null)
        {
            _prefab = prefab;
            _parent = parent;
            for (int i = 0; i < prewarm; i++) { var item = Create(); item.gameObject.SetActive(false); _idle.Push(item); }
        }

        public T Get()
        {
            T item = _idle.Count > 0 ? _idle.Pop() : Create();
            item.gameObject.SetActive(true);
            return item;
        }

        public void Return(T item)
        {
            item.gameObject.SetActive(false);
            _idle.Push(item);
        }

        private T Create() => Object.Instantiate(_prefab, _parent);
    }
}
