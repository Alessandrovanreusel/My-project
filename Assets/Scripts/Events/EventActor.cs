using System;
using UnityEngine;
using UnityEngine.AI;
using CameraGame.Core;

namespace CameraGame.Events
{
    /// <summary>
    /// The generic, data-driven event-actor (Story 1.6). Driven entirely by per-phase timers from an
    /// <see cref="EventDefinition"/>, it advances Spawn → Build → Peak → WindDown → Despawn and exposes
    /// its state through <see cref="ISubject"/> so the grader can read it without knowing the concrete
    /// event type. On Despawn it raises <see cref="Despawned"/> (it never destroys itself or references
    /// the manager) so the pooling <see cref="EventManager"/> can return it.
    ///
    /// Animation, NavMesh, and audio are all OPTIONAL and fail-soft — this is what lets the engine be
    /// verified in 1.6 with a controller-less, route-less stub before the real Town Drunk lands in 1.7.
    ///
    /// The lifecycle is started by the manager via <see cref="Begin"/> AFTER positioning — not from
    /// OnEnable — so neither a prewarm Instantiate nor a pooled re-Get() (both of which toggle the
    /// GameObject active) can run a spurious, mis-placed lifecycle (Story 1.6 review).
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent), typeof(Animator))]
    public class EventActor : MonoBehaviour, ISubject
    {
        [SerializeField, Tooltip("The data driving this actor's lifecycle. Required — an invalid one disables the actor.")]
        private EventDefinition definition;

        [SerializeField, Tooltip("Optional. Raised once when this actor reaches its Peak. Null = simply don't raise.")]
        private EventPeakedChannel eventPeaked;

        // Cached components (cached in Awake, never GetComponent in Update — consistency rule).
        private Animator _animator;
        private NavMeshAgent _agent;             // cached for Story 1.7's NavMesh routing (fail-soft; unused in 1.6)
        private AudioSource _cueSource;          // optional — a 3D directional rig is Story 1.8
        private Renderer[] _renderers;           // all child renderers (incl. inactive), for whole-subject bounds

        // Fail-soft readiness flags resolved once in Awake.
        private bool _animReady;                 // has a runtime controller → CrossFade is safe
        private bool _navReady;                  // the NavMeshAgent component exists (still gated on enabled/on-mesh at use site)

        // NavMesh routing (Story 1.7). The route is a scene component the manager hands in via Begin();
        // the actor only walks toward waypoints during phases whose PhaseConfig.advanceAlongRoute is true.
        private EventRoute _route;
        private int _waypointIndex;

        private EventPhase _phase;
        private float _timer;
        private bool _running;                   // true from Begin() until despawn; gates Update and latches the single Despawn signal

        /// <summary>
        /// The single fail-soft gate for every NavMeshAgent access (AC3, NFR8). True only when the agent
        /// exists, is enabled, and is actually placed on the baked NavMesh. Off-mesh/disabled ⇒ false ⇒ we
        /// skip all movement and let the timer FSM run in place rather than throwing into Update.
        /// </summary>
        private bool NavUsable => _navReady && _agent.enabled && _agent.isOnNavMesh;

        // --- ISubject -------------------------------------------------------------------------------

        /// <summary>World-space bounds encapsulating every child renderer (whole subject), so grading
        /// frames the entire body. Fail-soft to a zero-size point at the actor's position if none exist.</summary>
        public Bounds Bounds
        {
            get
            {
                if (_renderers == null || _renderers.Length == 0)
                    return new Bounds(transform.position, Vector3.zero);

                bool started = false;
                Bounds b = default;
                foreach (var r in _renderers)
                {
                    if (r == null) continue;
                    if (!started) { b = r.bounds; started = true; }
                    else b.Encapsulate(r.bounds);
                }
                return started ? b : new Bounds(transform.position, Vector3.zero);
            }
        }

        public bool IsAtPeak => _phase == EventPhase.Peak;

        /// <summary>Seconds until the peak. Decrements continuously and goes negative after the peak so
        /// the timing grader (1.10) can use Mathf.Abs for a symmetric window — do NOT stop at the peak.</summary>
        public float TimeToPeak { get; private set; }

        public string SubjectId => definition != null ? definition.Id : name;

        // --- Local (intra-system) signals -----------------------------------------------------------

        /// <summary>Raised on Despawn so the EventManager can return this actor to the pool. The actor
        /// never references the manager (architecture: avoid singletons; decoupled cross-system signals).</summary>
        public event Action<EventActor> Despawned;

        /// <summary>Raised on every phase transition (for future listeners; unused in 1.6 beyond logging).</summary>
        public event Action<EventPhase> PhaseChanged;

        // --- Lifecycle ------------------------------------------------------------------------------

        private void Awake()
        {
            // AC3: validate config in Awake; on failure log ONE clear error and disable gracefully.
            if (definition == null)
            {
                GameLog.Error("Events", $"{name}: EventDefinition is missing — disabling actor.", this);
                enabled = false;
                return;
            }
            if (!definition.IsValid(out string reason))
            {
                GameLog.Error("Events", $"{name}: {reason} — disabling actor.", this);
                enabled = false;
                return;
            }

            // Cache components. NavMeshAgent + Animator are guaranteed by RequireComponent; both are
            // treated fail-soft below. AudioSource is genuinely optional (may be null).
            _animator = GetComponent<Animator>();
            _agent = GetComponent<NavMeshAgent>();
            _cueSource = GetComponent<AudioSource>();

            // includeInactive: true so a child renderer that is inactive at Awake (revealed later in a
            // phase, e.g. a prop at Peak) still contributes to Bounds for grading.
            _renderers = GetComponentsInChildren<Renderer>(true);

            // Animation is only safe to drive when a controller is assigned (the stub has none in 1.6).
            _animReady = _animator != null && _animator.runtimeAnimatorController != null;

            // The agent is RequireComponent-guaranteed, but it may be disabled or off-mesh — those are
            // checked at the use site via NavUsable, not here. _navReady just records the component exists.
            _navReady = _agent != null;

            // Fail-soft also means silent, so validate the animation data ONCE here instead of discovering it
            // in play: CrossFade on a hash that names no state is a no-op, which surfaces as a drunk sliding
            // along the route in bind pose with a completely clean console. Awake-only ⇒ no per-frame cost.
            if (_animReady)
            {
                foreach (EventPhase p in (EventPhase[])Enum.GetValues(typeof(EventPhase)))
                {
                    EventDefinition.PhaseConfig cfg = definition.GetPhase(p);
                    if (cfg.AnimStateHash != 0 && !_animator.HasState(0, cfg.AnimStateHash))
                        GameLog.Warn("Events", $"{SubjectId}: phase {p} names animator state '{cfg.animStateName}', which does not exist on layer 0 of '{_animator.runtimeAnimatorController.name}'.");
                }
            }
        }

        /// <summary>
        /// Starts (or restarts) the lifecycle. The <see cref="EventManager"/> calls this AFTER it has
        /// positioned the actor, so phase side-effects (cue/anim) fire at the spawn location. Driving the
        /// FSM from an explicit call rather than OnEnable means neither a prewarm Instantiate nor a pooled
        /// re-Get() (both of which toggle the GameObject active) runs a spurious lifecycle.
        /// </summary>
        public void Begin(EventRoute route = null)
        {
            if (!enabled || definition == null) return;   // invalid config already disabled us in Awake

            // Store the route the manager handed in (null = stand-still lifecycle, fully valid). Reset the
            // walk progress so a pooled actor reused for the next cycle starts from the first waypoint.
            _route = route;
            _waypointIndex = 0;

            // The agent ships DISABLED on the prefab so a prewarmed/pooled instance that is momentarily off
            // the NavMesh (e.g. parked at the manager's y=20 anchor) never logs Unity's "Failed to create
            // agent". Note this only bites on the FIRST spawn: nothing re-disables the agent on despawn, so a
            // pooled instance comes back with it already enabled. That is fine — the Warp below is what
            // actually makes reuse correct, and it runs either way.
            if (_navReady && !_agent.enabled)
                _agent.enabled = true;

            // Re-sync the agent to the spawn point. This is NOT belt-and-braces. Enabling an agent — or
            // re-activating a pooled GameObject — registers it at whatever position it currently occupies, and
            // from that moment the agent is AUTHORITATIVE: it writes its internal position into the transform
            // every frame. The manager has already moved our transform to the spawn point, so without an
            // unconditional Warp the agent simply drags the body back to where it despawned.
            //
            // This guard used to read `&& !_agent.isOnNavMesh`, which skipped the Warp in exactly the
            // pooled-reuse case it existed for: the previous despawn point is ON the mesh, so the condition
            // read false and every event after the first replayed at the alley (confirmed by runtime polling,
            // 2026-07-25). Warp is idempotent and cheap — just always do it.
            if (_navReady && _agent.enabled)
            {
                _agent.Warp(transform.position);

                // A pooled agent also carries the previous cycle's path and isStopped flag. Clear both so the
                // fresh lifecycle starts from waypoint 0 instead of resuming the last cycle's destination.
                if (_agent.isOnNavMesh)
                {
                    _agent.ResetPath();
                    _agent.isStopped = false;
                }
            }

            // AC3 — the owed isOnNavMesh guard (deferred from Story 1.6). If we were given a real route but
            // the agent can't use the NavMesh (disabled or the spawn point is off-mesh), warn once PER SPAWN
            // (there is no latch — a permanently misconfigured spawn point will log every respawn) and carry
            // on: the timed FSM still runs to completion, the drunk just performs it in place. Recoverable ⇒
            // Warn + continue (architecture §Error Handling), never an exception in Update.
            if (_route != null && _route.HasWaypoints && !NavUsable)
                GameLog.Warn("Events", $"{SubjectId}: spawn point is off the NavMesh — drunk will run its lifecycle in place.");

            _running = true;

            // Peak begins after Spawn + Build elapse, so seed the countdown with their combined duration.
            TimeToPeak = definition.GetPhase(EventPhase.Spawn).duration
                       + definition.GetPhase(EventPhase.Build).duration;

            _timer = 0f;                    // EnterPhase carries the overshoot remainder; start clean.
            EnterPhase(EventPhase.Spawn);
        }

        private void Update()
        {
            // Only run between Begin() and despawn — never on an instance that is merely active (prewarm,
            // or Get() before the manager has called Begin()).
            if (!_running) return;

            // AC3: never throw from Update. The only state touched here is timers + the data-driven FSM.
            // NOTE (2026-07-25): clamping this step (Mathf.Min(Time.deltaTime, 0.1f)) to stop a frame hitch
            // collapsing the 1.5 s Peak was tried and REVERTED. Clamping decouples the FSM clock from
            // wall-clock: any frame slower than the clamp stretches the lifecycle instead of keeping time, and
            // in a low-fps editor the 24.5 s event ran well past a minute. The single-frame-Peak risk is real
            // but hypothetical; silently slowing every event under load is not. If this is revisited, fix it
            // in EnterPhase's overshoot carry (clamp how much negative _timer is carried), not on the input.
            _timer -= Time.deltaTime;
            TimeToPeak -= Time.deltaTime;   // continuous — keeps counting through and past the peak

            // Walk progress (Story 1.7): during a walking phase, step to the next waypoint once the agent has
            // arrived at the current one. Purely cosmetic/positional — the timers below own phase advancement,
            // so arriving early just idles the body at the last waypoint and arriving late stops it mid-route.
            // Guarded so it adds no per-frame allocation and cannot throw (AC2/AC3).
            if (NavUsable && _route != null && _route.HasWaypoints
                && definition.GetPhase(_phase).advanceAlongRoute
                && !_agent.pathPending && _agent.remainingDistance <= _agent.stoppingDistance
                && _waypointIndex < _route.Count - 1)
            {
                _waypointIndex++;
                SetDestinationToCurrentWaypoint();
            }

            if (_timer <= 0f)
                Advance();
        }

        /// <summary>Steps to the next phase. On Despawn, signals the manager instead of destroying self.</summary>
        private void Advance()
        {
            switch (_phase)
            {
                case EventPhase.Spawn:    EnterPhase(EventPhase.Build);    break;
                case EventPhase.Build:    EnterPhase(EventPhase.Peak);     break;
                case EventPhase.Peak:     EnterPhase(EventPhase.WindDown); break;
                case EventPhase.WindDown: EnterPhase(EventPhase.Despawn);  break;
                case EventPhase.Despawn:
                    // Lifecycle complete — hand ourselves back exactly once. Clearing _running first stops
                    // Update re-entering this case if anything delays the manager's SetActive(false)
                    // (e.g. a second Despawned subscriber, or a future deferred/animated return).
                    _running = false;

                    // NOTE (2026-07-25): re-disabling the agent here — to restore the prefab's "ships
                    // disabled" invariant across pooling — was tried and REVERTED. It wedged the lifecycle:
                    // the actor stayed active post-despawn with its agent disabled and never cycled again.
                    // The pooled-reuse position bug is fully fixed by Begin()'s unconditional Warp, which does
                    // not care whether the agent survived the pool enabled. Do not re-add this without a
                    // Play-mode test across at least two spawn cycles.
                    Despawned?.Invoke(this);
                    break;
            }
        }

        /// <summary>Enters a phase: sets the timer, fires fail-soft animation/cue, and raises signals.</summary>
        private void EnterPhase(EventPhase next)
        {
            _phase = next;
            EventDefinition.PhaseConfig phase = definition.GetPhase(next);
            _timer += phase.duration;       // carry any overshoot from the previous phase so the timeline
                                            // doesn't drift later than wall-clock across many transitions.

            // Animation fail-soft: only CrossFade when a controller exists and this phase names a state.
            // CrossFadeInFixedTime, NOT CrossFade: the plain overload's duration is NORMALIZED — a fraction of
            // the target clip's length, not seconds. With 0.2f that meant ~0.79 s of blend on the 3.97 s
            // DrunkStagger, i.e. over half of the 1.5 s Peak was a transition rather than the money shot
            // (2026-07-25 review). In fixed time it is a flat 0.2 s whatever the clip length.
            if (_animReady && phase.AnimStateHash != 0)
                _animator.CrossFadeInFixedTime(phase.AnimStateHash, 0.2f);

            // Cue fail-soft: only when both a clip and an AudioSource are present.
            if (phase.cue != null && _cueSource != null)
                _cueSource.PlayOneShot(phase.cue);

            // Cross-system seam: announce the peak (optional channel = simply don't raise).
            if (next == EventPhase.Peak)
                eventPeaked?.Raise(this);

            // Movement (Story 1.7): walking phases (advanceAlongRoute) head for the current waypoint; standing
            // phases (Spawn, Peak) stop in place — so the drunk staggers stationary at Peak (the money shot).
            // Purely positional: phases still advance on timers only, so this never gates the FSM (AC2/AC3).
            if (NavUsable && _route != null && _route.HasWaypoints)
            {
                _agent.isStopped = !phase.advanceAlongRoute;
                if (phase.advanceAlongRoute)
                    SetDestinationToCurrentWaypoint();
            }

            PhaseChanged?.Invoke(next);
            GameLog.Info("Events", $"{SubjectId} → {next}");
        }

        /// <summary>Points the agent at the current waypoint. Gated by NavUsable/HasWaypoints at every call
        /// site, so SetDestination is never invoked off-mesh (where it would warn and return false).</summary>
        private void SetDestinationToCurrentWaypoint()
        {
            // SetDestination returns false when the target cannot be mapped onto the NavMesh. Swallowing that
            // made an unreachable waypoint indistinguishable from a healthy route: remainingDistance stays at
            // Infinity, the arrival test in Update never fires again, and the actor plays its walk animation
            // standing still for the rest of its lifecycle with nothing in the console (2026-07-25 review).
            if (!_agent.SetDestination(_route.GetWaypoint(_waypointIndex)))
                GameLog.Warn("Events", $"{SubjectId}: waypoint {_waypointIndex} could not be mapped onto the NavMesh — the route will not advance past it.");
        }
    }
}
