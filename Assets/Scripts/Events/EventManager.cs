using UnityEngine;
using CameraGame.Core;

namespace CameraGame.Events
{
    /// <summary>
    /// The single spawner for event-actors (Story 1.6). Draws actors from an <see cref="ObjectPool{T}"/>
    /// (never Instantiate/Destroy in the loop — NFR3), caps how many run at once (NFR6), and returns each
    /// actor to the pool when it raises <see cref="EventActor.Despawned"/>. It subscribes on spawn and
    /// unsubscribes on return, so a pooled actor reused many times never accumulates handlers.
    ///
    /// No singleton, no DI container, no service locator — one plain MonoBehaviour (architecture
    /// §Code Organization).
    /// </summary>
    public class EventManager : MonoBehaviour
    {
        [SerializeField, Tooltip("The pooled actor prefab to spawn. Required — a null prefab disables the manager.")]
        private EventActor actorPrefab;

        [SerializeField, Min(1), Tooltip("Maximum actors active at once. 1 for the MVP slice (NFR6).")]
        private int maxConcurrent = 1;

        [SerializeField, Min(0f), Tooltip("Seconds to wait after an actor despawns before spawning the next.")]
        private float respawnDelay = 2f;

        [SerializeField, Tooltip("Optional spawn anchor (position/rotation). Falls back to this object's transform. Place it on the baked NavMesh.")]
        private Transform spawnPoint;

        private ObjectPool<EventActor> _pool;
        private int _activeCount;
        private float _respawnTimer;   // counts down; spawn when it reaches 0 and capacity is free

        private void Awake()
        {
            // AC3 fail-soft: a missing prefab means we can't pool anything — log once and stand down.
            if (actorPrefab == null)
            {
                GameLog.Error("Events", $"{name}: actorPrefab is missing — disabling EventManager.", this);
                enabled = false;
                return;
            }

            _pool = new ObjectPool<EventActor>(actorPrefab, prewarm: maxConcurrent, parent: transform);
        }

        private void Update()
        {
            // At capacity — nothing to do.
            if (_activeCount >= maxConcurrent) return;

            _respawnTimer -= Time.deltaTime;
            if (_respawnTimer > 0f) return;

            Spawn();
        }

        private void Spawn()
        {
            EventActor actor = _pool.Get();
            if (actor == null) return;   // pool disabled (e.g. null prefab guard) — stay quiet

            Transform anchor = spawnPoint != null ? spawnPoint : transform;
            actor.transform.SetPositionAndRotation(anchor.position, anchor.rotation);

            // Symmetric subscribe-on-spawn / unsubscribe-on-return: no handler accumulation across reuse.
            actor.Despawned += HandleDespawned;
            _activeCount++;
        }

        private void HandleDespawned(EventActor actor)
        {
            actor.Despawned -= HandleDespawned;
            _pool.Return(actor);
            _activeCount = Mathf.Max(0, _activeCount - 1);
            _respawnTimer = respawnDelay;   // pace the next spawn
        }

        private void OnDestroy()
        {
            // Destroy idle instances so the pool doesn't leak across scene loads (NFR3).
            _pool?.Clear();
        }
    }
}
