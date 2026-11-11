using UnityEngine;
using UnityEngine.AI;
using CameraGame.Core;

namespace CameraGame.World
{
    /// <summary>
    /// The player's safety net (Story 2.2, AC3, NFR8). Remembers the last few places the player stood on safe ground,
    /// and puts them back on the newest one at least <see cref="TownBoundsConfig.minRestoreAge"/> old whenever they
    /// have fallen out of the world:
    ///   (a) their feet are below <see cref="TownBoundsConfig.belowWorldY"/>;
    ///   (b) they are outside the playable area, or inside a no-go area (the backstop behind the invisible barriers);
    ///   (c) they are airborne with NO surface anywhere beneath them, for a moment — they fell through the world;
    ///   (d) they have been airborne too long — the last-resort backstop.
    ///
    /// "Safe ground" is ground they are standing on, within reach below the feet, no steeper than the controller can
    /// walk, inside the playable area, and on the NavMesh. Before the first safe sample, the spawn point (projected onto
    /// the NavMesh) is the fallback.
    ///
    /// Nothing is shown on screen (FR10: the game is diegetic). Each recovery logs ONE warning with both positions,
    /// because a place the player can fall through is a level defect worth finding.
    ///
    /// Runs after the controller has moved this frame (execution order 100, Update) and before the camera places the eye
    /// (LateUpdate), so a restore is never seen from the hole. Allocates nothing per frame (a fixed ring buffer and
    /// pre-allocated hit buffers). Dependency direction: World → Player only (it calls ThirdPersonController.Teleport).
    /// </summary>
    [DefaultExecutionOrder(100)]
    [RequireComponent(typeof(CharacterController))]
    public class FallRecovery : MonoBehaviour
    {
        [SerializeField] private TownBoundsConfig config;

        /// <summary>The bounds this runs on (null = the measured defaults).</summary>
        public TownBoundsConfig Config => config;

        private const int RingSize = 16;
        private readonly Vector3[] _samplePos = new Vector3[RingSize];
        private readonly float[] _sampleTime = new float[RingSize];
        private int _sampleCount, _sampleHead;
        private float _nextSampleTime, _airborne, _nothingBeneath;
        private Vector3 _fallback;
        private CharacterController _cc;
        private ThirdPersonController _tpc;
        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private Vector2[] _fallbackArea;

        /// <summary>How many times the player has been put back this session (for rigs and tests).</summary>
        public int Recoveries { get; private set; }
        public float LastRecoveryTime { get; private set; } = -1f;
        public string LastReason { get; private set; }
        public Vector3 LastFrom { get; private set; }
        public Vector3 LastTo { get; private set; }

        private float BelowWorldY => config != null ? config.SafeBelowWorldY : TownBoundsConfig.DefaultBelowWorldY;
        private float MaxAirborne => config != null ? config.SafeMaxAirborneSeconds : TownBoundsConfig.DefaultMaxAirborneSeconds;
        private float NothingBeneathFor => config != null ? config.SafeNothingBeneathSeconds : TownBoundsConfig.DefaultNothingBeneathSeconds;
        private float SampleInterval => config != null ? config.SafeSafeSampleInterval : TownBoundsConfig.DefaultSafeSampleInterval;
        private float MinRestoreAge => config != null ? config.SafeMinRestoreAge : TownBoundsConfig.DefaultMinRestoreAge;
        private float GroundProbe => config != null ? config.SafeGroundProbeDistance : TownBoundsConfig.DefaultGroundProbeDistance;
        private float NavProbe => config != null ? config.SafeNavMeshProbeDistance : TownBoundsConfig.DefaultNavMeshProbeDistance;

        private void Awake()
        {
            // Unity calls Awake on a DISABLED component too (lesson 69): a rig that switched this off owns the player.
            if (!enabled) return;
            _cc = GetComponent<CharacterController>();
            _tpc = GetComponent<ThirdPersonController>();
            _fallbackArea = TownBoundsConfig.DefaultPlayableArea;

            // Fail soft (NFR8): say ONCE what is wrong, then run on the measured defaults. Never throw.
            if (config == null)
                GameLog.Warn("World", "FallRecovery has no TownBoundsConfig assigned — running on the built-in measured defaults.");
            else if (config.TryGetConfigProblem(out string problem))
                GameLog.Warn("World", $"TownBoundsConfig '{config.name}': {problem}.");
        }

        private void Start()
        {
            if (_cc == null) Awake();
            Vector3 p = transform.position;
            _fallback = NavMesh.SamplePosition(p, out NavMeshHit hit, 50f, NavMesh.AllAreas) ? hit.position : p;
            _nextSampleTime = 0f;
        }

        private bool Allowed(Vector3 p)
        {
            var xz = new Vector2(p.x, p.z);
            return config != null ? config.IsAllowed(xz) : TownBoundsConfig.Contains(_fallbackArea, xz);
        }

        private static bool IsFinite(Vector3 v) =>
            TownBoundsConfig.IsFinite(v.x) && TownBoundsConfig.IsFinite(v.y) && TownBoundsConfig.IsFinite(v.z);

        private void Update()
        {
            if (_cc == null || !_cc.enabled) return;
            Vector3 p = transform.position;
            float dt = Time.deltaTime;
            bool grounded = _cc.isGrounded;
            if (grounded) { _airborne = 0f; _nothingBeneath = 0f; }
            else _airborne += dt;

            string reason = null;
            if (!IsFinite(p)) reason = "its position is not a number";
            else if (p.y < BelowWorldY) reason = $"its feet went below the world (y < {BelowWorldY})";
            else if (!Allowed(p)) reason = "it left the playable area";
            else if (!grounded)
            {
                if (_airborne > MaxAirborne) reason = $"it was airborne for over {MaxAirborne} s";
                else if (NothingBeneath(p))
                {
                    _nothingBeneath += dt;
                    if (_nothingBeneath > NothingBeneathFor) reason = "it was falling with nothing beneath it";
                }
                else _nothingBeneath = 0f;
            }

            if (reason != null) Recover(reason, p);
            else if (grounded && Time.time >= _nextSampleTime) TrySample(p);
        }

        /// <summary>True when a sphere the width of the body, cast straight down from the feet, finds no surface above
        /// the floor of the world.</summary>
        private bool NothingBeneath(Vector3 p)
        {
            float r = Mathf.Max(0.05f, _cc.radius * 0.9f);
            Vector3 from = p + Vector3.up * (r + 0.05f);
            float dist = Mathf.Max(0.1f, from.y - BelowWorldY);
            int n = Physics.SphereCastNonAlloc(from, r, Vector3.down, _hits, dist, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (_hits[i].collider != _cc) return false;
            return true;
        }

        private void TrySample(Vector3 p)
        {
            _nextSampleTime = Time.time + SampleInterval;
            int n = Physics.RaycastNonAlloc(p + Vector3.up * 0.1f, Vector3.down, _hits, 0.1f + GroundProbe, ~0, QueryTriggerInteraction.Ignore);
            bool ground = false;
            for (int i = 0; i < n; i++)
            {
                if (_hits[i].collider == _cc) continue;
                if (Vector3.Angle(_hits[i].normal, Vector3.up) <= _cc.slopeLimit) { ground = true; break; }
            }
            if (!ground || !Allowed(p)) return;
            if (!NavMesh.SamplePosition(p, out NavMeshHit _, NavProbe, NavMesh.AllAreas)) return;
            _samplePos[_sampleHead] = p;
            _sampleTime[_sampleHead] = Time.time;
            _sampleHead = (_sampleHead + 1) % RingSize;
            if (_sampleCount < RingSize) _sampleCount++;
        }

        private void Recover(string reason, Vector3 from)
        {
            float now = Time.time;
            Vector3 to = _fallback;
            // Newest first (the newest sample sits just before the head): take the newest safe spot old enough not to
            // be the lip of the hole, and forget the k-1 newer ones — they led here.
            int found = 0;
            for (int k = 1; k <= _sampleCount; k++)
            {
                int idx = (_sampleHead - k + RingSize) % RingSize;
                if (now - _sampleTime[idx] >= MinRestoreAge) { to = _samplePos[idx]; found = k; break; }
            }
            if (found > 0)
            {
                _sampleHead = (_sampleHead - (found - 1) + RingSize) % RingSize;
                _sampleCount -= found - 1;
            }
            else
            {
                _sampleHead = 0;
                _sampleCount = 0;
            }

            Vector3 target = to + Vector3.up * 0.05f;
            if (_tpc != null) _tpc.Teleport(target);
            else
            {
                _cc.enabled = false;
                transform.position = target;
                _cc.enabled = true;
            }
            _airborne = 0f;
            _nothingBeneath = 0f;
            _nextSampleTime = now + SampleInterval;
            Recoveries++;
            LastRecoveryTime = now;
            LastReason = reason;
            LastFrom = from;
            LastTo = target;
            GameLog.Warn("World", $"The player fell out of the world ({reason}) at {from} — put back on safe ground at {target}.");
        }
    }
}
