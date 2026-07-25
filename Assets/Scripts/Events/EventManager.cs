using System.Collections.Generic;
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

        [SerializeField, Tooltip("Optional NavMesh route the spawned actor walks (Story 1.7). Null = the actor runs its lifecycle in place.")]
        private EventRoute route;

        private ObjectPool<EventActor> _pool;
        private float _respawnTimer;   // counts down; spawn when it reaches 0 and capacity is free

        // The actors currently running a lifecycle. This replaced a plain int counter in Story 1.9: grading
        // needs the actual subjects, not just how many there are, and keeping both would have been two
        // sources of truth for the same fact. Kept exactly as symmetric as the Despawned subscribe/unsubscribe
        // below — added in Spawn(), removed in HandleDespawned(), never anywhere else.
        private readonly List<EventActor> _active = new List<EventActor>();

        /// <summary>
        /// The actors currently alive, for systems that need to look at the live world — grading reads this
        /// on capture (Story 1.9). Read-only to callers so nobody can desync the manager's own bookkeeping.
        ///
        /// ⚠️ Actors are POOLED: an entry here is only valid for as long as it is in this list. Read it live
        /// at the moment you need it and never cache an element across frames, or you will be holding a
        /// recycled instance describing a different event (see ISubject's liveness contract).
        /// </summary>
        public IReadOnlyList<EventActor> ActiveActors => _active;

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
            if (_active.Count >= maxConcurrent) return;

            _respawnTimer -= Time.deltaTime;
            if (_respawnTimer > 0f) return;

            Spawn();
        }

        private void Spawn()
        {
            EventActor actor = _pool.Get();
            if (actor == null) return;   // pool disabled (e.g. null prefab guard) — stay quiet

            // The actor disables itself in Awake on an invalid EventDefinition (AC3). A disabled actor
            // never runs its FSM, so it would never raise Despawned — counting it would pin a concurrency
            // slot forever and silently wedge spawning. Detect it once, return it, and stand the manager
            // down with one clear error (symmetric to the actor's own fail-soft).
            if (!actor.enabled)
            {
                GameLog.Error("Events", $"{name}: pooled actor '{actor.name}' is disabled (invalid EventDefinition?) — disabling EventManager.", this);
                _pool.Return(actor);
                enabled = false;
                return;
            }

            Transform anchor = spawnPoint != null ? spawnPoint : transform;
            actor.transform.SetPositionAndRotation(anchor.position, anchor.rotation);

            // Symmetric subscribe-on-spawn / unsubscribe-on-return: no handler accumulation across reuse.
            actor.Despawned += HandleDespawned;

            // Start the lifecycle AFTER positioning so the Spawn-phase cue/anim fire at the spawn point,
            // not at the prefab's authored pose (the actor no longer self-starts from OnEnable). The scene
            // route (if any) is handed in here so the actor walks pub→alley (Story 1.7).
            actor.Begin(route);
            _active.Add(actor);
        }

        private void HandleDespawned(EventActor actor)
        {
            actor.Despawned -= HandleDespawned;
            _pool.Return(actor);

            // Remove rather than decrement. The old counter needed a Mathf.Max(0, ...) floor to survive a
            // double-despawn; List.Remove is naturally idempotent (it returns false for an absent entry), so
            // the guard is now structural instead of arithmetic.
            _active.Remove(actor);

            _respawnTimer = respawnDelay;   // pace the next spawn
        }

        private void OnDestroy()
        {
            // Destroy idle instances so the pool doesn't leak across scene loads (NFR3).
            _pool?.Clear();

            // Drop the live references too: after a scene unload these point at destroyed objects, and a
            // late reader (a queued capture, a lingering coroutine) would otherwise walk a list of corpses.
            _active.Clear();
        }
    }
}
