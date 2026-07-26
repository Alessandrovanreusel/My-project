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
        private AudioSource _cueSource;          // optional — carries BOTH the looping cue bed and the per-phase accents
        private Renderer[] _renderers;           // all child renderers (incl. inactive), for whole-subject bounds

        // Fail-soft readiness flags resolved once in Awake.
        private bool _animReady;                 // has a runtime controller → CrossFade is safe
        private bool _navReady;                  // the NavMeshAgent component exists (still gated on enabled/on-mesh at use site)

        // NavMesh routing (Story 1.7). The route is a scene component the manager hands in via Begin();
        // the actor only walks toward waypoints during phases whose PhaseConfig.advanceAlongRoute is true.
        private EventRoute _route;
        private int _waypointIndex;

        // Cached cue rolloff curve + the tunables it was built from (see GetRolloffCurve).
        private AnimationCurve _rolloffCurve;
        private float _curveFalloffStart;
        private float _curveRadius;

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

        /// <summary>
        /// Distance from the peak WINDOW rather than from its start — see <see cref="ISubject.PeakOffset"/>
        /// for why the difference decides whether the money shot scores full marks.
        ///
        /// Derived rather than stored: <see cref="TimeToPeak"/> already carries the clock and
        /// <see cref="_phase"/> already says where we are, so there is no second piece of state to drift
        /// (Story 1.9's review: two sources of truth is this project's recurring failure mode). Branching on
        /// the PHASE, not on the sign of TimeToPeak, so an actor that has not been <see cref="Begin"/>-run
        /// yet — TimeToPeak 0, phase Spawn — reports "before the peak" rather than "1.5 s after it".
        /// </summary>
        public float PeakOffset
        {
            get
            {
                if (_phase == EventPhase.Peak) return 0f;                 // anywhere in the money shot
                if (_phase < EventPhase.Peak) return Mathf.Max(0f, TimeToPeak);

                // After the peak. TimeToPeak is already −peakDuration at the window's end, so adding the
                // duration back re-bases the countdown onto the window's END: 0 the instant the peak
                // finishes, growing negative from there. Min(0) so a mis-authored zero-length peak or a
                // frame overshoot can never report a POSITIVE offset from the far side.
                float peakDuration = definition != null ? definition.GetPhase(EventPhase.Peak).duration : 0f;
                return Mathf.Min(0f, TimeToPeak + peakDuration);
            }
        }

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

            // Same rule as the animation check above: fail-soft must not mean invisible. An ABSENT cue is a
            // perfectly valid silent event and says nothing; but a cue that was authored and then cannot
            // possibly work is an authoring mistake, and it fails in the worst way — the cue is audible, so
            // everything sounds fine until someone notices they can't tell which direction it is coming from.
            // Awake-only, so this costs nothing per frame AND the warning cannot spam once per spawn.
            //
            // Gated on "any cue at all", not just loopCue: a per-phase accent plays through the SAME
            // AudioSource (EnterPhase → PlayOneShot), so an accent-only event needs exactly the same rig.
            if (HasAnyCue())
            {
                if (_cueSource == null)
                {
                    GameLog.Warn("Events", $"{SubjectId}: a cue clip is assigned but this prefab has no AudioSource — the event will be silent.");
                }
                else
                {
                    // Unity logs "Can not play a disabled audio source" as an ERROR, once per Play/PlayOneShot
                    // — i.e. once per spawn forever, under no GameLog category anyone would think to grep.
                    if (!_cueSource.enabled)
                        GameLog.Warn("Events", $"{SubjectId}: the cue AudioSource is disabled — the event will be silent.");

                    if (_cueSource.spatialBlend < 1f)
                        GameLog.Warn("Events", $"{SubjectId}: cue AudioSource spatialBlend is {_cueSource.spatialBlend:0.##}; it must be 1.0 (fully 3D) for the cue to be directional.");

                    WarnIfNotMono(definition.loopCue);
                    foreach (EventPhase p in (EventPhase[])Enum.GetValues(typeof(EventPhase)))
                        WarnIfNotMono(definition.GetPhase(p).cue);

                    // The distances are as easy to mis-author as the clips and get no Inspector feedback at
                    // all: [Min(0f)] permits cueRadius = 0, and nothing relates the two fields to each other.
                    if (!TryResolveCueDistances(out _, out _, out string distanceProblem))
                        GameLog.Warn("Events", $"{SubjectId}: {distanceProblem} — falling back to the prefab's own rolloff settings.");
                    else if (distanceProblem != null)
                        GameLog.Warn("Events", $"{SubjectId}: {distanceProblem}");
                }
            }
        }

        /// <summary>True if this definition authors any cue at all — the loop bed or a per-phase accent.</summary>
        private bool HasAnyCue()
        {
            if (definition.loopCue != null) return true;
            foreach (EventPhase p in (EventPhase[])Enum.GetValues(typeof(EventPhase)))
                if (definition.GetPhase(p).cue != null) return true;
            return false;
        }

        /// <summary>Warns (Awake-only) about a stereo cue clip — Unity spatialises MONO clips only.</summary>
        private void WarnIfNotMono(AudioClip clip)
        {
            if (clip != null && clip.channels > 1)
                GameLog.Warn("Events", $"{SubjectId}: cue clip '{clip.name}' has {clip.channels} channels — Unity only spatialises MONO clips, so this cue will be audible but not locatable.");
        }

        /// <summary>
        /// Resolves the authored cue distances into a pair the audio rig can actually use, so that Begin() and
        /// the Awake validation agree on what "usable" means.
        ///
        /// Returns false when the authoring is broken beyond rescue (radius ≤ 0 — and NaN too, since NaN fails
        /// every comparison, so the <c>&gt;</c> tests catch it for free). Returns true with a non-null
        /// <paramref name="problem"/> when the values were usable only after clamping: silently rewriting a
        /// designer's number is exactly the "fail-soft means invisible" trap the rest of this class avoids.
        /// </summary>
        private bool TryResolveCueDistances(out float falloffStart, out float radius, out string problem)
        {
            falloffStart = definition.cueFalloffStart;
            radius = definition.cueRadius;
            problem = null;

            if (!(radius > 0f))
            {
                problem = $"cueRadius is {radius}, which cannot describe an audible range";
                return false;
            }

            // The curve needs its flat region strictly inside the radius. Both bounds below rewrite authored
            // data, which is why they hand back a problem string rather than clamping quietly.
            float minStart = radius * 0.001f;
            float maxStart = radius * 0.5f;
            if (!(falloffStart > 0f) || falloffStart < minStart)
            {
                problem = $"cueFalloffStart {falloffStart} is too small for cueRadius {radius} — using {minStart}";
                falloffStart = minStart;
            }
            else if (falloffStart > maxStart)
            {
                problem = $"cueFalloffStart {falloffStart} is more than half of cueRadius {radius} — using {maxStart}";
                falloffStart = maxStart;
            }
            return true;
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

            // Diegetic cue (Story 1.8). The looping bed starts HERE, not in OnEnable, for exactly the reason the
            // FSM does: a prewarmed or pooled instance parked at the manager's y=20 anchor must stay silent
            // until it has been placed at a real spawn point. (playOnAwake is false on the prefab for the same
            // reason — do not turn it on.)
            //
            // Every field is re-applied on EVERY Begin(), deliberately. A pooled actor returns from
            // SetActive(false) with its AudioSource stopped but its clip/loop/distances still set, which makes
            // "it's already configured, it'll just play again" look true — and that exact assumption about
            // surviving pool state is what cost Story 1.7 a Critical bug on the NavMeshAgent. Assume nothing
            // came back from the pool; state it all again.
            if (_cueSource != null)
            {
                // Silence first, unconditionally. Covers both a cue-less event and the case where the manager
                // re-Get()s this actor while an old bed is still sounding.
                _cueSource.Stop();

                // Rolloff is configured for ANY cue, not just a loop bed — note this sits OUTSIDE the loopCue
                // branch below, deliberately. Per-phase accents play through this same AudioSource
                // (EnterPhase → PlayOneShot), so an accent-only event that skipped this block would fall back
                // to the prefab's Logarithmic mode, whose attenuation plateaus and never reaches silence. That
                // is the exact "audible across the whole town" bug this story fixed for the bed; leaving the
                // accent path on the old mode would reintroduce it one field over.
                if (TryResolveCueDistances(out float falloffStart, out float radius, out _))
                {
                    // Distances come from the DEFINITION, in world units, not from the prefab. This world is
                    // ~4× metric (see EventDefinition.cueRadius), so the design's "~25 m" is ~100 units here —
                    // and when the open world-scale question is finally settled, retuning every event is one
                    // data edit each instead of a hunt through prefabs.
                    //
                    // minDistance is written for the Inspector's benefit only: under Custom rolloff the engine
                    // takes attenuation from the curve alone and ignores it (see GetRolloffCurve).
                    _cueSource.minDistance = falloffStart;
                    _cueSource.maxDistance = radius;

                    // CUSTOM rolloff, not Logarithmic. Unity defines maxDistance as "the distance where the
                    // sound STOPS attenuating" — not where it becomes silent. Under Logarithmic the volume
                    // therefore flattens out at minDistance/maxDistance (8/100 = 0.08, about -22 dB) and holds
                    // that level to infinity, so the actor stayed faintly audible from anywhere in the town
                    // (confirmed by ear, 2026-07-25). Worse, the plateau is a RATIO: shrinking cueRadius
                    // *raises* it (8/60 = 0.13), so this cannot be tuned away with distance — only the curve
                    // shape fixes it. The curve below keeps the natural inverse-distance falloff near the
                    // source (which is what makes "walk toward the louder side" work) and genuinely reaches
                    // zero at cueRadius, satisfying AC1's "inaudible well beyond the radius".
                    _cueSource.rolloffMode = AudioRolloffMode.Custom;
                    _cueSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff,
                                              GetRolloffCurve(falloffStart, radius));
                }
                // else: distances are un-authorable-bad. Awake already warned once; leave the prefab's own
                // settings in place so the cue is at least audible rather than silently dropped.

                if (definition.loopCue != null)
                {
                    _cueSource.clip = definition.loopCue;
                    _cueSource.loop = true;                          // a one-shot bed would go quiet mid-lifecycle
                    _cueSource.Play();
                }
                else
                {
                    // A cue-less event is valid (same principle as a route-less one), so drop the clip and stay
                    // silent rather than leaving a previous definition's bed loaded on a pooled actor.
                    _cueSource.clip = null;
                }
            }

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

                    // Stop the cue BEFORE signalling Despawned (Story 1.8). Despawned is what causes the
                    // manager to pool us, and SetActive(false) would silence the source anyway — but only if
                    // the manager pools us immediately. Any subscriber that defers the return (a second
                    // listener, or a future fade-out/animated despawn) would otherwise leave a disembodied
                    // cue looping at the despawn point with nobody there.
                    if (_cueSource != null)
                        _cueSource.Stop();

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

        /// <summary>
        /// Builds the cue's distance-attenuation curve, cached so respawning doesn't re-allocate.
        ///
        /// Shape: <c>volume(x) = (x0/x) · (1-x)/(1-x0)</c> where x is distance/cueRadius and x0 is
        /// cueFalloffStart/cueRadius. The first term is plain inverse-distance — the natural falloff a real
        /// sound has, and the reason the volume gradient reads as "getting warmer" when you walk toward it.
        /// The second term fades that to exactly zero at cueRadius, which is the part Unity's built-in
        /// Logarithmic mode will not do. Full volume is held from 0 out to cueFalloffStart.
        ///
        /// (Note that with Custom rolloff the curve alone defines attenuation — minDistance is ignored by
        /// the engine, so cueFalloffStart is baked in here as the flat region rather than read from it.)
        /// </summary>
        private AnimationCurve GetRolloffCurve(float falloffStart, float radius)
        {
            // Rebuild only when the tunables actually change, so a steady configuration allocates nothing per
            // spawn. NOTE this does NOT make the values live-editable: the curve and the distances are pushed
            // to the AudioSource only from Begin(), so an Inspector edit during Play is picked up at the NEXT
            // spawn — a full lifecycle plus EventManager's respawn delay later, not immediately.
            if (_rolloffCurve != null
                && Mathf.Approximately(_curveFalloffStart, falloffStart)
                && Mathf.Approximately(_curveRadius, radius))
                return _rolloffCurve;

            // Guard the degenerate authoring cases (radius 0, or a falloff start past the radius) so we can
            // never divide by zero or emit a curve that rises — fail-soft, as everywhere else in this class.
            float x0 = radius > 0f ? Mathf.Clamp(falloffStart / radius, 0.001f, 0.5f) : 0.5f;

            const int samples = 10;
            var keys = new Keyframe[samples + 1];
            keys[0] = new Keyframe(0f, 1f);          // full volume from the listener's nose out to x0

            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)(samples - 1);
                float x = Mathf.Lerp(x0, 1f, t * t);  // t² clusters samples near the source, where it bends most
                float y = x >= 1f ? 0f : (x0 / x) * (1f - x) / (1f - x0);
                keys[i + 1] = new Keyframe(x, y);
            }

            var curve = new AnimationCurve(keys);

            // Smooth from key 2 onward ONLY. keys[0] and keys[1] both sit at value 1 — they are the flat
            // full-volume plateau — and SmoothTangents would give key 1 a tangent averaged from its
            // neighbours (steeply negative) while key 0 kept zero. Hermite between two equal-valued keys with
            // mismatched tangents BULGES: ≈1.018 at the shipped 8/100 values, and ≈11% if cueFalloffStart ever
            // widens toward half the radius. A rolloff curve that amplifies is not what "full volume" means.
            for (int i = 2; i < curve.length; i++)
                curve.SmoothTangents(i, 0f);          // default flat tangents would make the rest ripple
            FlattenTangents(curve, 0);
            FlattenTangents(curve, 1);

            _rolloffCurve = curve;
            _curveFalloffStart = falloffStart;
            _curveRadius = radius;
            return curve;
        }

        /// <summary>Forces one keyframe's tangents flat. AnimationCurve keys are structs, so the key has to be
        /// copied out, edited and moved back — mutating <c>curve[i].inTangent</c> directly changes a copy.</summary>
        private static void FlattenTangents(AnimationCurve curve, int index)
        {
            Keyframe k = curve[index];
            k.inTangent = 0f;
            k.outTangent = 0f;
            curve.MoveKey(index, k);
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
