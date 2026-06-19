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

        private EventPhase _phase;
        private float _timer;
        private bool _running;                   // true from Begin() until despawn; gates Update and latches the single Despawn signal

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
        }

        /// <summary>
        /// Starts (or restarts) the lifecycle. The <see cref="EventManager"/> calls this AFTER it has
        /// positioned the actor, so phase side-effects (cue/anim) fire at the spawn location. Driving the
        /// FSM from an explicit call rather than OnEnable means neither a prewarm Instantiate nor a pooled
        /// re-Get() (both of which toggle the GameObject active) runs a spurious lifecycle.
        /// </summary>
        public void Begin()
        {
            if (!enabled || definition == null) return;   // invalid config already disabled us in Awake

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
            _timer -= Time.deltaTime;
            TimeToPeak -= Time.deltaTime;   // continuous — keeps counting through and past the peak

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
            if (_animReady && phase.AnimStateHash != 0)
                _animator.CrossFade(phase.AnimStateHash, 0.2f);

            // Cue fail-soft: only when both a clip and an AudioSource are present.
            if (phase.cue != null && _cueSource != null)
                _cueSource.PlayOneShot(phase.cue);

            // Cross-system seam: announce the peak (optional channel = simply don't raise).
            if (next == EventPhase.Peak)
                eventPeaked?.Raise(this);

            PhaseChanged?.Invoke(next);
            GameLog.Info("Events", $"{SubjectId} → {next}");
        }
    }
}
