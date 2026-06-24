using UnityEngine;

namespace CameraGame.Events
{
    /// <summary>
    /// Data-driven definition of a single event-actor's lifecycle (Story 1.6). One asset per kind of
    /// event (e.g. "TownDrunk"). The <see cref="EventActor"/> reads per-phase timing/animation/cue from
    /// here and advances on its own timers — so ~80% of future events become data, not new code (AR2).
    ///
    /// The NavMesh route is deliberately NOT stored here: a route is world positions/Transforms that a
    /// project-asset SO can't reference. Story 1.7 decides the route's representation in the scene.
    /// </summary>
    [CreateAssetMenu(menuName = "CameraGame/Events/Event Definition", fileName = "EventDefinition")]
    public class EventDefinition : ScriptableObject
    {
        /// <summary>Per-phase tunables: how long the phase lasts, what to animate, what cue to play.</summary>
        [System.Serializable]
        public class PhaseConfig
        {
            [Min(0f), Tooltip("How long this phase lasts, in seconds.")]
            public float duration = 1f;

            [Tooltip("Animator state to CrossFade into on entering this phase. Leave empty for no animation change this phase.")]
            public string animStateName = "";

            [Tooltip("Optional audio cue played once on entering this phase. Leave empty for none.")]
            public AudioClip cue;

            [Tooltip("If true, the actor walks toward the next route waypoint during this phase; if false it stands still.")]
            public bool advanceAlongRoute = false;

            // Cached so we don't re-hash the string on every phase entry. Not serialized — recomputed
            // lazily and invalidated by EventDefinition.OnValidate when the name is edited.
            [System.NonSerialized] private bool _hashCached;
            [System.NonSerialized] private int _animStateHash;

            /// <summary>
            /// Animator.StringToHash of <see cref="animStateName"/>, cached. Returns 0 when the name is
            /// empty — the actor treats 0 as "no animation this phase" (mirrors the AnimHashes pattern).
            /// </summary>
            public int AnimStateHash
            {
                get
                {
                    if (!_hashCached)
                    {
                        _animStateHash = string.IsNullOrEmpty(animStateName) ? 0 : Animator.StringToHash(animStateName);
                        _hashCached = true;
                    }
                    return _animStateHash;
                }
            }

            internal void InvalidateHashCache() => _hashCached = false;
        }

        [Tooltip("The SubjectId the spawned actor reports to grading (e.g. \"TownDrunk\"). Must be non-empty.")]
        public string Id;

        [Min(0f), Tooltip("Radius (metres) within which this event's diegetic cue is audible. Used by Story 1.8.")]
        public float cueRadius = 25f;

        [Tooltip("Spawn phase — the actor appears.")]            public PhaseConfig spawn = new();
        [Tooltip("Build phase — tension/anticipation rises toward the peak.")] public PhaseConfig build = new();
        [Tooltip("Peak phase — the photogenic moment; the actor raises EventPeaked here.")] public PhaseConfig peak = new();
        [Tooltip("Wind-down phase — the moment passes.")]        public PhaseConfig windDown = new();
        [Tooltip("Despawn phase — the actor returns itself to the pool when this elapses.")] public PhaseConfig despawn = new();

        /// <summary>Maps a phase enum to its config — the architecture's <c>definition.GetPhase(next)</c> call site.</summary>
        public PhaseConfig GetPhase(EventPhase phase)
        {
            switch (phase)
            {
                case EventPhase.Spawn:    return spawn;
                case EventPhase.Build:    return build;
                case EventPhase.Peak:     return peak;
                case EventPhase.WindDown: return windDown;
                case EventPhase.Despawn:  return despawn;
                default:                  return spawn;
            }
        }

        /// <summary>
        /// True when this definition is safe to run. The actor calls this in Awake; on false it logs the
        /// reason and disables itself (AC3 fail-soft). Checks: non-empty Id, all five phases present,
        /// no negative durations.
        /// </summary>
        public bool IsValid(out string reason)
        {
            if (string.IsNullOrWhiteSpace(Id))
            {
                reason = $"EventDefinition '{name}' has an empty Id";
                return false;
            }

            foreach (EventPhase p in System.Enum.GetValues(typeof(EventPhase)))
            {
                PhaseConfig cfg = GetPhase(p);
                if (cfg == null)
                {
                    reason = $"EventDefinition '{name}' is missing the {p} phase config";
                    return false;
                }
                if (cfg.duration < 0f)
                {
                    reason = $"EventDefinition '{name}' has a negative duration on the {p} phase";
                    return false;
                }
            }

            // A zero-duration Peak would make IsAtPeak true for only a single frame, which a poll-based
            // grader can miss. Other phases may legitimately be 0, but the Peak must have real duration.
            if (peak.duration <= 0f)
            {
                reason = $"EventDefinition '{name}' must give the Peak phase a duration > 0";
                return false;
            }

            reason = null;
            return true;
        }

        // Recompute cached animation hashes after any Inspector edit.
        private void OnValidate()
        {
            spawn?.InvalidateHashCache();
            build?.InvalidateHashCache();
            peak?.InvalidateHashCache();
            windDown?.InvalidateHashCache();
            despawn?.InvalidateHashCache();
        }
    }
}
