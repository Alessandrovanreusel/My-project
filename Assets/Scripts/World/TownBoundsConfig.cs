using UnityEngine;

namespace CameraGame.World
{
    /// <summary>
    /// Where the town ends and what happens below it (Story 2.2). One asset, <c>Assets/Data/World/TownBoundsConfig.asset</c>,
    /// read by <see cref="FallRecovery"/> at runtime and by the editor tools that build the invisible barriers
    /// (<c>TownBounds</c>) and bake the whole-town NavMesh — so the playable area is written down exactly once.
    ///
    /// Every number here was MEASURED on the town (Story 2.2 Task 1, <c>_bmad-output/verification/2-2-walk-the-whole-town/census/</c>):
    ///   • the playable area is the plain plus the ring's inner slopes; its north edge is the water's edge (z 269.5) and
    ///     every other edge sits on a hillside, never across open plain;
    ///   • the lowest walkable ground inside it is about −5 m, so the floor of the world sits 10 m lower, at −15, which is
    ///     still far above the buried copy of the street block (y −68…−48) that nobody should ever stand on.
    ///
    /// ⚠️ Read every value through its <c>Safe*</c> accessor. [Min] and OnValidate are editor-only and this project
    /// hand-authors assets as YAML, so a NaN or an infinity typed into the file reaches the game intact. Story 2.1's
    /// review froze the player for good with a −Infinity, which is why every check here is "finite", not just "not NaN".
    /// </summary>
    [CreateAssetMenu(menuName = "CameraGame/World/Town Bounds Config", fileName = "TownBoundsConfig")]
    public class TownBoundsConfig : ScriptableObject
    {
        [System.Serializable]
        public struct Area
        {
            public string name;
            public Vector2[] points;   // Unity (x, z), in order around the edge
        }

        // Defaults = the measured values, and what the game falls back to (with one warning) when the asset is missing
        // or a value is unusable (NFR8).
        public const float DefaultBelowWorldY = -15f;
        public const float DefaultMaxAirborneSeconds = 3f;
        public const float DefaultNothingBeneathSeconds = 0.15f;
        public const float DefaultSafeSampleInterval = 0.25f;
        public const float DefaultMinRestoreAge = 0.5f;
        public const float DefaultGroundProbeDistance = 0.3f;
        public const float DefaultNavMeshProbeDistance = 0.5f;
        public const float DefaultBarrierClearance = 6f;
        public const float DefaultBarrierThickness = 1f;
        public const float DefaultBakeYMin = -8f;
        public const float DefaultBakeYMax = 110f;
        public const float DefaultBuriedTopY = -1f;

        /// <summary>The measured playable area (Story 2.2, 1c). See the class comment.</summary>
        public static readonly Vector2[] DefaultPlayableArea =
        {
            new Vector2(-680f, 269.5f), new Vector2(210f, 269.5f), new Vector2(210f, 215f), new Vector2(260f, 165f),
            new Vector2(260f, -320f), new Vector2(215f, -340f), new Vector2(200f, -400f), new Vector2(-680f, -400f),
        };

        [Header("The world's edges (Unity x, z)")]
        [Tooltip("The playable area as a polygon, in order around its edge. Invisible barriers stand on its edges, the NavMesh " +
                 "is baked inside its bounding box, and leaving it puts the player back.")]
        public Vector2[] playableArea = (Vector2[])DefaultPlayableArea.Clone();

        [Tooltip("Places inside the playable area the player may not enter (the dark pond). Barriers stand around each; " +
                 "being found inside one puts the player back, exactly like leaving the playable area.")]
        public Area[] noGoAreas = new Area[0];

        [Header("Below the world")]
        [Tooltip("Feet below this height (metres) = fell out of the world. Between the lowest walkable ground and anything buried.")]
        public float belowWorldY = DefaultBelowWorldY;

        [Tooltip("Backstop: airborne this long (seconds) with no landing = fell out of the world.")]
        [Min(0.1f)] public float maxAirborneSeconds = DefaultMaxAirborneSeconds;

        [Tooltip("Airborne with NO surface anywhere below (down to belowWorldY) for this long (seconds) = fell through the world.")]
        [Min(0f)] public float nothingBeneathSeconds = DefaultNothingBeneathSeconds;

        [Tooltip("How often (seconds) the last safe ground is remembered while the player stands on it.")]
        [Min(0.01f)] public float safeSampleInterval = DefaultSafeSampleInterval;

        [Tooltip("The player is put back on the newest safe spot at least this old (seconds), never on the lip of the hole.")]
        [Min(0f)] public float minRestoreAge = DefaultMinRestoreAge;

        [Tooltip("Safe ground: the ground must be within this distance (metres) below the feet.")]
        [Min(0.01f)] public float groundProbeDistance = DefaultGroundProbeDistance;

        [Tooltip("Safe ground: the NavMesh must be within this distance (metres) of the feet.")]
        [Min(0.01f)] public float navMeshProbeDistance = DefaultNavMeshProbeDistance;

        [Header("Authoring (editor tools only)")]
        [Tooltip("Map pieces whose top is below this height (metres) are buried under the plain and get no collider (ColliderRule).")]
        public float buriedTopY = DefaultBuriedTopY;

        [Tooltip("How far above the highest ground along an edge each invisible barrier reaches (metres). Above a jump, so " +
                 "nobody can stand on top of one.")]
        [Min(3f)] public float barrierClearance = DefaultBarrierClearance;

        [Tooltip("Thickness of each invisible barrier (metres).")]
        [Min(0.1f)] public float barrierThickness = DefaultBarrierThickness;

        [Tooltip("The whole-town NavMesh bake volume's floor (metres): just below the lowest walkable ground, far above the buried block.")]
        public float bakeYMin = DefaultBakeYMin;

        [Tooltip("The whole-town NavMesh bake volume's ceiling (metres): above the highest reachable ground.")]
        public float bakeYMax = DefaultBakeYMax;

        // ---------------------------------------------------------------------------------------------------------
        // Safe accessors
        // ---------------------------------------------------------------------------------------------------------

        /// <summary>The floor of the world. It must be finite AND below the NavMesh bake floor (bakeYMin, just under the
        /// lowest walkable ground): a floor above walkable ground would put back a player who is standing on it, every frame.</summary>
        public float SafeBelowWorldY => IsFinite(belowWorldY) && belowWorldY < SafeBakeYMin ? belowWorldY : Mathf.Min(DefaultBelowWorldY, SafeBakeYMin - 1f);
        public float SafeMaxAirborneSeconds => Positive(maxAirborneSeconds, DefaultMaxAirborneSeconds);
        public float SafeNothingBeneathSeconds => NonNegative(nothingBeneathSeconds, DefaultNothingBeneathSeconds);
        public float SafeSafeSampleInterval => Positive(safeSampleInterval, DefaultSafeSampleInterval);
        public float SafeMinRestoreAge => NonNegative(minRestoreAge, DefaultMinRestoreAge);
        public float SafeGroundProbeDistance => Positive(groundProbeDistance, DefaultGroundProbeDistance);
        public float SafeNavMeshProbeDistance => Positive(navMeshProbeDistance, DefaultNavMeshProbeDistance);
        public float SafeBarrierClearance => Positive(barrierClearance, DefaultBarrierClearance);
        public float SafeBarrierThickness => Positive(barrierThickness, DefaultBarrierThickness);
        public float SafeBuriedTopY => IsFinite(buriedTopY) ? buriedTopY : DefaultBuriedTopY;
        public float SafeBakeYMin => IsFinite(bakeYMin) && IsFinite(bakeYMax) && bakeYMin < bakeYMax ? bakeYMin : DefaultBakeYMin;
        public float SafeBakeYMax => IsFinite(bakeYMin) && IsFinite(bakeYMax) && bakeYMin < bakeYMax ? bakeYMax : DefaultBakeYMax;

        /// <summary>The playable area, or the measured default when the one typed is not a usable polygon.</summary>
        public Vector2[] SafePlayableArea => IsUsablePolygon(playableArea) ? playableArea : DefaultPlayableArea;

        public static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private static float Positive(float v, float fallback) => !IsFinite(v) || v <= 0f ? fallback : v;
        private static float NonNegative(float v, float fallback) => !IsFinite(v) || v < 0f ? fallback : v;

        /// <summary>At least three finite points enclosing a non-zero area.</summary>
        public static bool IsUsablePolygon(Vector2[] poly)
        {
            if (poly == null || poly.Length < 3) return false;
            foreach (Vector2 p in poly)
                if (!IsFinite(p.x) || !IsFinite(p.y)) return false;
            return Mathf.Abs(SignedArea(poly)) > 1f;
        }

        public static float SignedArea(Vector2[] poly)
        {
            float a = 0f;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
                a += poly[j].x * poly[i].y - poly[i].x * poly[j].y;
            return a * 0.5f;
        }

        /// <summary>Even-odd point-in-polygon test, (x, z) in the polygon's plane. Pure; allocates nothing.</summary>
        public static bool Contains(Vector2[] poly, Vector2 p)
        {
            if (poly == null || poly.Length < 3) return false;
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                Vector2 a = poly[i], b = poly[j];
                if ((a.y > p.y) != (b.y > p.y) && p.x < (b.x - a.x) * (p.y - a.y) / (b.y - a.y) + a.x)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>True when (x, z) is somewhere the player may stand: inside the playable area and in no no-go area.</summary>
        public bool IsAllowed(Vector2 p)
        {
            if (!Contains(SafePlayableArea, p)) return false;
            if (noGoAreas != null)
                foreach (Area a in noGoAreas)
                    if (IsUsablePolygon(a.points) && Contains(a.points, p)) return false;
            return true;
        }

        /// <summary>The axis-aligned (x, z) bounds of the playable area: min in (x, y), max in (z, w).</summary>
        public Vector4 PlayableBounds2D()
        {
            Vector2[] poly = SafePlayableArea;
            Vector4 b = new Vector4(float.MaxValue, float.MaxValue, float.MinValue, float.MinValue);
            foreach (Vector2 p in poly) { b.x = Mathf.Min(b.x, p.x); b.y = Mathf.Min(b.y, p.y); b.z = Mathf.Max(b.z, p.x); b.w = Mathf.Max(b.w, p.y); }
            return b;
        }

        /// <summary>The first value that is unusable and therefore replaced by its default — reported with both numbers,
        /// because the value typed is not the value used and nothing else would say so.</summary>
        public bool TryGetConfigProblem(out string problem)
        {
            problem = null;
            if (!IsUsablePolygon(playableArea)) problem = $"playableArea is not a polygon of 3+ finite points with an area — using the measured default ({DefaultPlayableArea.Length} points)";
            else if (SafeBelowWorldY != belowWorldY) problem = $"belowWorldY {belowWorldY} is not finite or not below the walkable ground's floor (bakeYMin {SafeBakeYMin}) — using {SafeBelowWorldY}";
            else if (SafeMaxAirborneSeconds != maxAirborneSeconds) problem = $"maxAirborneSeconds {maxAirborneSeconds} is not a positive time — using {SafeMaxAirborneSeconds}";
            else if (SafeNothingBeneathSeconds != nothingBeneathSeconds) problem = $"nothingBeneathSeconds {nothingBeneathSeconds} is negative or not finite — using {SafeNothingBeneathSeconds}";
            else if (SafeSafeSampleInterval != safeSampleInterval) problem = $"safeSampleInterval {safeSampleInterval} is not a positive time — using {SafeSafeSampleInterval}";
            else if (SafeMinRestoreAge != minRestoreAge) problem = $"minRestoreAge {minRestoreAge} is negative or not finite — using {SafeMinRestoreAge}";
            else if (SafeGroundProbeDistance != groundProbeDistance) problem = $"groundProbeDistance {groundProbeDistance} is not a positive distance — using {SafeGroundProbeDistance}";
            else if (SafeNavMeshProbeDistance != navMeshProbeDistance) problem = $"navMeshProbeDistance {navMeshProbeDistance} is not a positive distance — using {SafeNavMeshProbeDistance}";
            else if (SafeBarrierClearance != barrierClearance) problem = $"barrierClearance {barrierClearance} is not a positive height — using {SafeBarrierClearance}";
            else if (SafeBarrierThickness != barrierThickness) problem = $"barrierThickness {barrierThickness} is not a positive thickness — using {SafeBarrierThickness}";
            else if (SafeBuriedTopY != buriedTopY) problem = $"buriedTopY {buriedTopY} is not finite — using {SafeBuriedTopY}";
            else if (SafeBakeYMin != bakeYMin || SafeBakeYMax != bakeYMax) problem = $"bakeYMin {bakeYMin} / bakeYMax {bakeYMax} are not a finite rising range — using {SafeBakeYMin} / {SafeBakeYMax}";
            else if (noGoAreas != null)
                foreach (Area a in noGoAreas)
                    if (!IsUsablePolygon(a.points)) { problem = $"no-go area '{a.name}' is not a polygon — it is ignored"; break; }
            return problem != null;
        }
    }
}
