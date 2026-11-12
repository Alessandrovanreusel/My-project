using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using CameraGame.World;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// Story 2.2 (AC3, CLOSE attempt 2): the VISIBLE edge of the town. On the mountain ring the terrain is walkable all
    /// the way to the map's edge, so the invisible <c>TownBounds</c> barriers were what stopped the player there (1,642 of
    /// 2,271 m, <c>close/ring_contact.json</c>). AC3 says the terrain stops them. This tool puts a line of the map's own
    /// boulders along those stretches, so the player is stopped BY a rock they can see. The barriers stay as the backstop.
    ///
    /// <c>Tools/Town/Build Town Edge</c> (re-runnable, idempotent: a fixed seed per ring side, and the old root is
    /// replaced):
    ///   • reads the ring from <see cref="TownBoundsConfig.playableArea"/> and skips the shore (the polygon's north edge,
    ///     where the water stops the player);
    ///   • measures every metre with downward rays (the census rule: terrain and the map's solid pieces, never scatter or
    ///     water): a boulder is needed wherever the ground just inside is walkable (≤ 45°) and the ground just outside is
    ///     too, i.e. wherever only the barrier would stop the player. Runs are widened and short gaps closed, so the line
    ///     reads as continuous;
    ///   • places copies of one map boulder (mesh + material + source tilt, from 'game map') under the scene root
    ///     <c>TownEdge</c>: varied yaw and scale, overlapping by half, buried so no gap opens under them, with the top at
    ///     least <see cref="MinTopAboveGround"/> above the ground beside them (the jump apex is 1.26 m). Static, Default
    ///     layer, a MeshCollider each, so the bake carves them and the player meets the rock's inner face;
    ///   • checks itself: a 0.6 m capsule swept from inside toward the barrier at walking, jump-apex and 1.8 m heights must
    ///     meet a boulder on every half-metre of every run, or it reports the gap.
    /// Names never contain "Edge": the edge-walk rig classifies a stop on a collider named "Edge …" as a TownBounds barrier.
    /// Marks the scene dirty and never saves it (save in a separate call).
    ///
    /// <c>Tools/Town/Measure Ring Contact (NavMesh)</c> measures where the player can still reach a ring barrier: the part of
    /// the baked NavMesh connected to the pub (its agent is the player's body: 0.30 / 1.77 / climb 0.35 / 45°) that comes
    /// within <see cref="ContactDistance"/> of the barrier line. Re-bake first, or it measures the old bake.
    /// </summary>
    public static class TownEdgeBuilder
    {
        public const string RootName = "TownEdge";
        /// <summary>The boulder copied along the ring. Decided by photographing both candidates from the eye at ring_b180
        /// (story file, CLOSE attempt 2): the white faceted rock the villa's wall and the plain's scatter are made of.</summary>
        public const string DefaultSource = "Plane.008|Icosphere.002_M3_0|Dupli|128.3";
        public const float WalkableSlope = 45f;
        public const float MinTopAboveGround = 1.8f;
        public const float ContactDistance = 1.1f;          // barrier inner face 0.5 m + agent radius 0.3 m + voxel slack
        private const float Step = 1f;                       // measuring step along the ring
        private const float Widen = 6f;                      // a run is widened by this much at both ends
        private const float CloseGap = 20f;                  // gaps shorter than this between runs are closed
        private const float CornerReach = 10f;               // a run this close to a corner is carried to the corner
        private const float ExposedMin = 2.4f, ExposedMax = 3.2f;   // top above the ground beside the boulder
        private const float MaxWidth = 13f;                  // no boulder wider than this (keeps the inner face near the line)
        private const float Overlap = 0.5f;                  // centre spacing = this × the boulder's narrower width
        private const int Seed = 22022;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string F(float v, string f = "0.##") => v.ToString(f, Inv);

        [MenuItem("Tools/Town/Build Town Edge")]
        public static void BuildMenu() => Build(DefaultSource, "edge-build");

        [MenuItem("Tools/Town/Measure Ring Contact (NavMesh)")]
        public static void ContactMenu() => MeasureContact("edge-build/contact");

        private struct Side { public int index; public Vector2 a, b, dir, inward; public float length; public string name; }

        /// <summary>The ring sides of the playable area: every edge but the shore (the polygon's northernmost horizontal edge).</summary>
        private static List<Side> RingSides(Vector2[] poly)
        {
            float sign = TownBoundsConfig.SignedArea(poly) >= 0f ? 1f : -1f;
            float maxZ = float.MinValue;
            foreach (Vector2 p in poly) maxZ = Mathf.Max(maxZ, p.y);
            var sides = new List<Side>();
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
                float len = Vector2.Distance(a, b);
                if (len < 0.01f) continue;
                if (Mathf.Abs(a.y - b.y) < 0.01f && a.y >= maxZ - 0.01f) continue;   // the shore: the water stops them
                Vector2 d = (b - a) / len;
                Vector2 left = new Vector2(-d.y, d.x);
                sides.Add(new Side { index = i, a = a, b = b, dir = d, inward = left * sign, length = len,
                                     name = $"Ring side {i} ({F(a.x, "0")}, {F(a.y, "0")}) to ({F(b.x, "0")}, {F(b.y, "0")})" });
            }
            return sides;
        }

        public static string Build(string sourceName, string reportSub)
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[TownEdge] Build in edit mode."); return "playing"; }
            var cfg = AssetDatabase.LoadAssetAtPath<TownBoundsConfig>(AddMeshColliders.TownBoundsConfigPath);
            GameObject map = GameObject.Find("game map");
            if (cfg == null || map == null) { Debug.LogWarning("[TownEdge] Needs TownBoundsConfig and 'game map'."); return "missing"; }
            MeshRenderer src = null;
            foreach (MeshRenderer r in map.GetComponentsInChildren<MeshRenderer>(true))
                if (r.name == sourceName) { src = r; break; }
            MeshFilter srcMf = src != null ? src.GetComponent<MeshFilter>() : null;
            if (srcMf == null || srcMf.sharedMesh == null) { Debug.LogWarning("[TownEdge] No boulder named '" + sourceName + "' under 'game map'."); return "no source"; }

            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            Physics.SyncTransforms();
            Dictionary<Collider, int> kinds = TownCensus.Kinds(map);

            var root = new GameObject(RootName);
            const StaticEditorFlags flags = StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                                            StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic;
            GameObjectUtility.SetStaticEditorFlags(root, flags);
            Vector2[] poly = cfg.SafePlayableArea;
            var sb = new StringBuilder($"Town edge built {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} from '{sourceName}' (mesh {srcMf.sharedMesh.name}, material {src.sharedMaterial.name})\n");
            var samples = new StringBuilder("side,u,x,z,inner_slope,outer_slope_1,outer_slope_3,needed,run\n");
            int total = 0; float neededM = 0f, runM = 0f;
            var boulders = new List<Transform>();
            var fillersAll = new List<Transform>();
            int filled = 0, fillCount = 0;
            var runsBySide = new Dictionary<int, bool[]>();
            var groups = new Dictionary<int, Transform>();
            var sideOf = new Dictionary<int, Side>();
            foreach (Side s in RingSides(poly))
            {
                int n = Mathf.Max(1, Mathf.RoundToInt(s.length / Step));
                var need = new bool[n + 1];
                var inner = new float[n + 1]; var out1 = new float[n + 1]; var out3 = new float[n + 1];
                for (int k = 0; k <= n; k++)
                {
                    Vector2 p = Vector2.Lerp(s.a, s.b, (float)k / n);
                    inner[k] = Slope(kinds, p + s.inward * 2f, out _);
                    float in3 = Slope(kinds, p + s.inward * 3f, out _);
                    out1[k] = Slope(kinds, p - s.inward * 1f, out _);
                    out3[k] = Slope(kinds, p - s.inward * 3f, out _);
                    bool innerWalk = Walkable(inner[k]) || Walkable(in3);
                    bool outerWalk = Walkable(out1[k]) || Walkable(out3[k]);
                    need[k] = innerWalk && outerWalk;
                    if (need[k]) neededM += s.length / n;
                }
                // runs: widen, close short gaps, carry to a corner nearby
                var run = new bool[n + 1];
                int widen = Mathf.RoundToInt(Widen / Step);
                for (int k = 0; k <= n; k++)
                    if (need[k]) for (int j = Mathf.Max(0, k - widen); j <= Mathf.Min(n, k + widen); j++) run[j] = true;
                int gap = Mathf.RoundToInt(CloseGap / Step);
                for (int k = 0, last = -1; k <= n; k++)
                {
                    if (!run[k]) continue;
                    if (last >= 0 && k - last > 1 && k - last <= gap) for (int j = last; j <= k; j++) run[j] = true;
                    last = k;
                }
                int reach = Mathf.RoundToInt(CornerReach / Step);
                for (int k = 0; k <= Mathf.Min(n, reach); k++) if (run[k]) { for (int j = 0; j <= k; j++) run[j] = true; break; }
                for (int k = n; k >= Mathf.Max(0, n - reach); k--) if (run[k]) { for (int j = k; j <= n; j++) run[j] = true; break; }

                runsBySide[s.index] = run;
                var group = new GameObject(s.name);
                GameObjectUtility.SetStaticEditorFlags(group, flags);
                group.transform.SetParent(root.transform, false);
                groups[s.index] = group.transform; sideOf[s.index] = s;
                var rng = new System.Random(Seed + s.index * 7919);
                int placed = 0;
                for (int k = 0; k <= n; k++)
                {
                    Vector2 p = Vector2.Lerp(s.a, s.b, (float)k / n);
                    samples.AppendLine($"{s.index},{F(k * s.length / n, "0.#")},{F(p.x, "0.#")},{F(p.y, "0.#")},{F(inner[k], "0.#")},{F(out1[k], "0.#")},{F(out3[k], "0.#")},{(need[k] ? 1 : 0)},{(run[k] ? 1 : 0)}");
                }
                // place along each run; start 3 m before it and end 3 m past it (clamped to the polygon at placement)
                int r0 = -1;
                for (int k = 0; k <= n + 1; k++)
                {
                    bool on = k <= n && run[k];
                    if (on && r0 < 0) r0 = k;
                    if (!on && r0 >= 0)
                    {
                        float u0 = r0 * s.length / n - 3f, u1 = (k - 1) * s.length / n + 3f;
                        runM += Mathf.Min(s.length, u1 - 3f) - Mathf.Max(0f, u0 + 3f);
                        sb.AppendLine($"  {s.name}: run {F(Mathf.Max(0f, u0 + 3f), "0")}..{F(Mathf.Min(s.length, u1 - 3f), "0")} m");
                        float u = u0;
                        while (u <= u1)
                        {
                            Transform t = Place(group.transform, src, srcMf, kinds, s, u, rng, poly, placed, out float spacing, fillersAll);
                            if (t != null) { boulders.Add(t); placed++; }
                            filled += fillersAll.Count - fillCount; fillCount = fillersAll.Count;
                            u += spacing;
                        }
                        r0 = -1;
                    }
                }
                sb.AppendLine($"{s.name}: {F(s.length, "0")} m, {placed} boulders");
                total += placed;
            }
            Physics.SyncTransforms();
            // patch pass: one more boulder at every station the check still finds a gap at (a dip between two tops, a
            // corner), then check again. Deterministic, so the tool stays idempotent.
            var gapAt = new List<KeyValuePair<int, float>>();
            string gaps = CheckGaps(root.transform, kinds, runsBySide, poly, sb, gapAt);
            int patched = 0;
            for (int round = 1; round <= 3 && gapAt.Count > 0; round++)
            {
                sb.AppendLine($"pass {round} " + gaps);
                int lastSide = -1; float lastU = -99f;
                var todo = new List<KeyValuePair<int, float>>(gapAt);
                gapAt.Clear();
                foreach (KeyValuePair<int, float> g in todo)
                {
                    if (g.Key == lastSide && g.Value - lastU < 1.5f) continue;
                    lastSide = g.Key; lastU = g.Value;
                    var rngP = new System.Random(Seed + 31 * patched + g.Key);
                    Transform t = Place(groups[g.Key], src, srcMf, kinds, sideOf[g.Key], g.Value, rngP, poly, 900 + patched, out _, fillersAll);
                    if (t != null) { boulders.Add(t); patched++; }
                }
                Physics.SyncTransforms();
                gaps = $"after {patched} patch boulders, " + CheckGaps(root.transform, kinds, runsBySide, poly, sb, gapAt);
            }
            total += patched;
            filled = fillersAll.Count;
            EditorSceneManager.MarkSceneDirty(root.scene);
            sb.AppendLine($"needed (only the barrier would stop the player): {F(neededM, "0")} m; boulder runs {F(runM, "0")} m; {total} boulders + {filled} fillers stacked under them on steep ground, under {RootName}. Scene NOT saved.");
            sb.AppendLine(gaps);
            boulders.AddRange(fillersAll);
            string dir = TownCensus.Root + "/" + reportSub;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "build.txt"), sb.ToString());
            File.WriteAllText(Path.Combine(dir, "samples.csv"), samples.ToString());
            var list = new StringBuilder("name,x,y,z,yaw,scale,width_min,top_y,ground_y,exposed_m,bottom_y,ground_min_y\n");
            foreach (Transform t in boulders)
            {
                var info = TrueBounds(t);
                list.AppendLine($"{t.name},{F(t.position.x)},{F(t.position.y)},{F(t.position.z)},{F(t.eulerAngles.y, "0")},{F(t.lossyScale.x, "0.###")},{F(Mathf.Min(info.size.x, info.size.z))},{F(info.max.y)},{F(_lastGround[t])},{F(info.max.y - _lastGround[t])},{F(info.min.y)},{F(_lastGroundMin[t])}");
            }
            File.WriteAllText(Path.Combine(dir, "boulders.csv"), list.ToString());
            _lastGround.Clear(); _lastGroundMin.Clear();
            Debug.Log($"[TownEdge] {total} boulders built — {dir}/build.txt");
            return sb.ToString();
        }

        private static readonly Dictionary<Transform, float> _lastGround = new Dictionary<Transform, float>();
        private static readonly Dictionary<Transform, float> _lastGroundMin = new Dictionary<Transform, float>();

        private static Transform Place(Transform parent, MeshRenderer src, MeshFilter srcMf, Dictionary<Collider, int> kinds,
                                       Side s, float u, System.Random rng, Vector2[] poly, int index, out float spacing,
                                       List<Transform> fillers = null)
        {
            float yaw = (float)rng.NextDouble() * 360f;
            float exposed = Mathf.Lerp(ExposedMin, ExposedMax, (float)rng.NextDouble());
            float vary = Mathf.Lerp(0.85f, 1.15f, (float)rng.NextDouble());
            var go = new GameObject($"Boulder {s.index}-{index:000}");
            go.transform.SetParent(parent, false);
            go.transform.rotation = Quaternion.Euler(0f, yaw, 0f) * src.transform.rotation;
            go.transform.localScale = src.transform.lossyScale;
            go.AddComponent<MeshFilter>().sharedMesh = srcMf.sharedMesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = src.sharedMaterials;
            mr.shadowCastingMode = src.shadowCastingMode;
            mr.receiveShadows = src.receiveShadows;
            Bounds b1 = TrueBounds(go.transform);          // at the source's own scale (k = 1)
            float h1 = Mathf.Max(0.01f, b1.size.y), w1 = Mathf.Max(0.01f, Mathf.Min(b1.size.x, b1.size.z));
            // half-buried: the boulder is about twice as tall as it stands out of the ground
            float k = Mathf.Min(2f * exposed * vary / h1, MaxWidth / w1);
            go.transform.localScale = src.transform.lossyScale * k;
            Bounds b = TrueBounds(go.transform);
            float wMin = Mathf.Min(b.size.x, b.size.z);
            spacing = Mathf.Max(1f, Overlap * wMin);
            float inset = Mathf.Max(0f, 0.5f * wMin - 1f);  // the outer face about 1 m past the barrier line
            Vector2 p = Vector2.Lerp(s.a, s.b, Mathf.Clamp01(u / s.length)) + s.dir * (u - Mathf.Clamp(u, 0f, s.length)) + s.inward * inset;
            if (!TownBoundsConfig.Contains(poly, p)) { Object.DestroyImmediate(go); return null; }
            // the ground: at the centre, where the player meets the inner face, and up to 6 m further in (a running jump
            // from higher ground inside reaches 1.26 m above where it took off); the lowest under the footprint
            float gRef = float.MinValue, gMin = float.MaxValue;
            float half = 0.5f * wMin;
            // (CLOSE attempt 2: a 9 x 5 grid of the ground a player can STAND on, ≤ 45°, up to 8 m in from the inner face;
            // four fixed spots missed the higher ground on steep, cross-sloping corners and left 94 m of jump gaps)
            if (Ground(kinds, p, out float gc)) gRef = gc;
            for (int e = 0; e <= 8; e++)
                for (int l = -2; l <= 2; l++)
                {
                    Vector2 q = p + s.inward * (half + e) + s.dir * (half * 0.5f * l);
                    float sl = Slope(kinds, q, out float g);
                    if (!float.IsNaN(g) && Walkable(sl)) gRef = Mathf.Max(gRef, g);
                }
            Vector2 gMinAt = p;
            for (int i = -1; i <= 1; i++)
                for (int j = -1; j <= 1; j++)
                {
                    Vector2 q = p + s.dir * (i * half) + s.inward * (j * half);
                    if (Ground(kinds, q, out float g) && g < gMin) { gMin = g; gMinAt = q; }
                }
            if (gRef == float.MinValue) { Object.DestroyImmediate(go); return null; }
            if (gMin == float.MaxValue) gMin = gRef;
            // never grown to reach the ground below: a grown rock's exposed cap is a gentle 24-33° ramp that a jump from
            // the higher ground behind lands on (CLOSE attempt 2, measured at (-550.5, -400)). It stays about a half-buried
            // sphere, ~60° at jump height, and the fillers below bury its base instead
            go.transform.position = new Vector3(p.x, 0f, p.y);
            b = TrueBounds(go.transform);
            // the top is what stops the player: always `exposed` above the highest ground they can stand on beside it
            go.transform.position += new Vector3(0f, gRef + exposed - b.max.y, 0f);
            Finish(go, srcMf, gRef, gMin);
            // where the ground falls away under it by more than one boulder can span (steep ground, up to ~25 m of relief
            // across a 13 m footprint), stack fillers under it, shifted toward the lowest ground, until it is buried:
            // never air under the line (CLOSE attempt 2: 36 of 907 boulders had been sunk to keep their bottom buried,
            // which left their top below the 1.8 m minimum, 17 of them below the ground)
            Bounds stack = TrueBounds(go.transform);
            Vector2 low = p + (gMinAt - p) * 0.6f;
            for (int f = 0; stack.min.y > gMin - 0.3f && f < 8; f++)
            {
                var fill = new GameObject($"Boulder {s.index}-{index:000}{(char)('a' + f)}");
                fill.transform.SetParent(parent, false);
                fill.transform.rotation = Quaternion.Euler(0f, (yaw + 97f * (f + 1)) % 360f, 0f) * src.transform.rotation;
                fill.transform.localScale = go.transform.localScale;
                fill.AddComponent<MeshFilter>().sharedMesh = srcMf.sharedMesh;
                var fr = fill.AddComponent<MeshRenderer>();
                fr.sharedMaterials = src.sharedMaterials;
                fr.shadowCastingMode = src.shadowCastingMode;
                fr.receiveShadows = src.receiveShadows;
                fill.transform.position = new Vector3(low.x, 0f, low.y);
                Bounds fb = TrueBounds(fill.transform);
                // overlap the one above by half its height
                fill.transform.position += new Vector3(0f, stack.min.y + 0.5f * fb.size.y - fb.max.y, 0f);
                Finish(fill, srcMf, gRef, gMin);
                stack.Encapsulate(TrueBounds(fill.transform));
                fillers?.Add(fill.transform);
            }
            return go.transform;
        }

        /// <summary>The boulder's real world-space extent, from its mesh's vertices. Never Renderer.bounds: for a rotated mesh
        /// that is the box around the rotated LOCAL box, which overstated this tilted rock's top by ~1.1 m (CLOSE attempt 2,
        /// measured at (210, 249.5): bounds top y 3.4, the rock's surface 2.3, so the line stood 1.4 m tall, not 2.5).</summary>
        private static Bounds TrueBounds(Transform t)
        {
            Mesh m = t.GetComponent<MeshFilter>().sharedMesh;
            if (!_verts.TryGetValue(m, out Vector3[] v)) { v = m.vertices; _verts[m] = v; }
            Matrix4x4 w = t.localToWorldMatrix;
            var b = new Bounds(w.MultiplyPoint3x4(v[0]), Vector3.zero);
            for (int i = 1; i < v.Length; i++) b.Encapsulate(w.MultiplyPoint3x4(v[i]));
            return b;
        }

        private static readonly Dictionary<Mesh, Vector3[]> _verts = new Dictionary<Mesh, Vector3[]>();

        private static void Finish(GameObject go, MeshFilter srcMf, float gRef, float gMin)
        {
            var mc = go.AddComponent<MeshCollider>();
            mc.sharedMesh = srcMf.sharedMesh;
            mc.convex = false;
            go.layer = 0;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                                                       StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            _lastGround[go.transform] = gRef;
            _lastGroundMin[go.transform] = gMin;
        }

        /// <summary>Can the player's body (a 0.30 m-radius, 1.77 m capsule) reach the barrier on a boulder run? Checked on
        /// every half-metre of every run the build decided (so a missing boulder shows as a gap, never as "no run here"),
        /// by walking the capsule ALONG THE GROUND from 14 m inside toward the line, half a metre at a time:
        ///   walk   — feet on the ground + a step (0.35 m). Stopped by a boulder: fine. Stopped by ground steeper than 45°
        ///            rising in front: the terrain stops them, fine. Reaches the barrier's inner face (0.8 m inside the line,
        ///            capsule centre): a WALK GAP;
        ///   jump   — walking and jumping along the arc (PlayerConfig: apex 1.26 m at mid-jump, ~5.7 m long) over the surface
        ///            profile, rock tops included (see ProfileReachesFace), the barrier's face is reached: a JUMP GAP;
        ///   margin — the same from the last free spot's ground + 1.8 m (the brief's minimum height): a MARGIN GAP when the
        ///            rock is lower than that.
        /// The earlier version swept horizontally from the ground 10 m inside, which on a slope runs underground and passes
        /// beneath the boulder (1,070 of its 1,100 walk gaps): measured on the profile instead.</summary>
        private static string CheckGaps(Transform root, Dictionary<Collider, int> kinds, Dictionary<int, bool[]> runs, Vector2[] poly, StringBuilder log,
                                        List<KeyValuePair<int, float>> gapAt = null)
        {
            var mine = new HashSet<Collider>(root.GetComponentsInChildren<Collider>());
            int stations = 0, gapWalk = 0, gapJump = 0, gapMargin = 0, byRock = 0, byTerrain = 0; float checkedM = 0f;
            var gapList = new StringBuilder();
            const float Inner = 14f, Face = 0.8f, StepD = 0.5f;
            int nd = Mathf.RoundToInt((Inner - Face) / StepD);
            var gy = new float[nd + 1]; var sl = new float[nd + 1];
            var su = new float[nd + 1]; var ss = new float[nd + 1];
            ReadJump(out float apex, out float jumpLen);
            log.AppendLine($"gap check jump model: apex {F(apex)} m, length {F(jumpLen)} m (PlayerConfig sprint speed x time in the air)");
            foreach (Side s in RingSides(poly))
            {
                if (!runs.TryGetValue(s.index, out bool[] run)) continue;
                int n1 = run.Length - 1;
                int n = Mathf.RoundToInt(s.length / 0.5f);
                Vector3 dir = new Vector3(-s.inward.x, 0f, -s.inward.y);
                for (int k = 0; k <= n; k++)
                {
                    float t = (float)k / n;
                    if (!run[Mathf.Clamp(Mathf.RoundToInt(t * n1), 0, n1)]) continue;
                    Vector2 p = Vector2.Lerp(s.a, s.b, t);
                    checkedM += s.length / n; stations++;
                    for (int i = 0; i <= nd; i++)
                    {
                        float d = Inner - i * StepD;
                        TownCensus.Column(kinds, p.x + s.inward.x * d, p.y + s.inward.y * d, out gy[i], out sl[i], out _, out _, out _);
                    }
                    int start = 0;
                    while (start <= nd && float.IsNaN(gy[start])) start++;
                    if (start > nd) continue;
                    string stop = "";
                    int free = start;
                    for (int i = start; i <= nd; i++)
                    {
                        if (float.IsNaN(gy[i])) continue;
                        float d = Inner - i * StepD;
                        Vector2 q = p + s.inward * d;
                        if (Overlaps(mine, q, gy[i] + 0.35f)) { stop = "rock"; break; }
                        if (i > start && !float.IsNaN(gy[i - 1]) && gy[i] > gy[i - 1] && sl[i] > WalkableSlope) { stop = "terrain"; break; }
                        free = i;
                    }
                    if (stop == "terrain") { byTerrain++; continue; }
                    if (stop == "")
                    {
                        gapWalk++;
                        gapAt?.Add(new KeyValuePair<int, float>(s.index, t * s.length));
                        gapList.AppendLine($"    walk gap: {s.name} at ({F(p.x, "0.#")}, {F(p.y, "0.#")})");
                        continue;
                    }
                    byRock++;
                    float dFree = Inner - free * StepD;
                    Vector2 from = p + s.inward * dFree;
                    float reach = dFree - Face;
                    // the jump: can a player who walks AND jumps reach the barrier's face? Measured on the surface they
                    // would actually move over (the ground, or the rock's top where a rock is higher, at body width), with
                    // the controller's two moves: a walk (up a step of 0.35 m, or a slope of 45° or less; down always)
                    // and the jump's real arc (PlayerConfig apex at mid-jump). Earlier models of the jump (apex held flat;
                    // "touches a standable facet") reported 269-614 false gaps on flat ground under 2.4 m rocks
                    // (CLOSE attempt 2, measured at (210, 260) and (210, 254)).
                    for (int i = start; i <= nd; i++)
                    {
                        float d = Inner - i * StepD;
                        float rt = RockTop(mine, p + s.inward * d, s.dir, out float rs);
                        bool rock = !float.IsNaN(rt) && (float.IsNaN(gy[i]) || rt > gy[i]);
                        su[i] = rock ? rt : gy[i];
                        ss[i] = rock ? rs : sl[i];
                    }
                    bool jumpGap = ProfileReachesFace(su, ss, start, nd, StepD, apex, jumpLen);
                    float takeoff = gy[free];
                    bool marginGap = !Sweep(mine, from, dir, gy[free] + 1.8f, reach);
                    if (jumpGap || marginGap) gapAt?.Add(new KeyValuePair<int, float>(s.index, t * s.length));
                    if (jumpGap)
                    {
                        gapJump++;
                        gapList.AppendLine($"    jump gap: {s.name} at ({F(p.x, "0.#")}, {F(p.y, "0.#")}), takeoff ground {F(takeoff, "0.##")}");
                    }
                    if (marginGap)
                    {
                        gapMargin++;
                        gapList.AppendLine($"    margin gap: {s.name} at ({F(p.x, "0.#")}, {F(p.y, "0.#")}), rock lower than ground {F(gy[free], "0.##")} + 1.8");
                    }
                }
            }
            log.Append(gapList);
            return $"GAP CHECK (terrain-following): {stations} half-metre stations over {F(checkedM, "0")} m of boulder run; " +
                   $"stopped by a boulder {byRock}, by ground steeper than {WalkableSlope}° {byTerrain}; " +
                   $"WALK GAPS {gapWalk} ({F(gapWalk * 0.5f, "0.#")} m), JUMP GAPS {gapJump} ({F(gapJump * 0.5f, "0.#")} m), 1.8 m MARGIN GAPS {gapMargin} ({F(gapMargin * 0.5f, "0.#")} m)";
        }

        /// <summary>The player's jump, read from PlayerConfig: the apex (jump height, never under the 1.26 m measured in
        /// play) and how far a sprinting jump travels before it is back at its takeoff height.</summary>
        private static void ReadJump(out float apex, out float length)
        {
            float jump = 1.2f, gravity = -15f, sprint = 7f;
            var pc = AssetDatabase.LoadAssetAtPath<ScriptableObject>("Assets/Data/Player/PlayerConfig.asset");
            if (pc != null)
            {
                var so = new SerializedObject(pc);
                SerializedProperty j = so.FindProperty("jumpHeight"), g = so.FindProperty("gravity"), sp = so.FindProperty("sprintSpeed");
                if (j != null) jump = j.floatValue;
                if (g != null) gravity = g.floatValue;
                if (sp != null) sprint = sp.floatValue;
            }
            apex = Mathf.Max(1.26f, jump);
            length = sprint * 2f * Mathf.Sqrt(2f * apex / Mathf.Max(0.1f, Mathf.Abs(gravity)));
        }

        /// <summary>True when the capsule with its feet at <paramref name="feet"/>, standing at <paramref name="q"/>, touches a boulder.</summary>
        private static bool Overlaps(HashSet<Collider> mine, Vector2 q, float feet)
        {
            Vector3 bottom = new Vector3(q.x, feet + 0.3f, q.y);
            foreach (Collider c in Physics.OverlapCapsule(bottom, bottom + Vector3.up * (1.77f - 0.6f), 0.3f))
                if (mine.Contains(c)) return true;
            return false;
        }

        /// <summary>True when the capsule, feet at <paramref name="feet"/>, meets a boulder on its way from
        /// <paramref name="from"/> over <paramref name="reach"/> metres (or starts inside one).</summary>
        private static bool Sweep(HashSet<Collider> mine, Vector2 from, Vector3 dir, float feet, float reach)
        {
            if (Overlaps(mine, from, feet)) return true;
            Vector3 bottom = new Vector3(from.x, feet + 0.3f, from.y);
            foreach (RaycastHit h in Physics.CapsuleCastAll(bottom, bottom + Vector3.up * (1.77f - 0.6f), 0.3f, dir, Mathf.Max(0.01f, reach)))
                if (mine.Contains(h.collider)) return true;
            return false;
        }

        /// <summary>The highest boulder surface under a 0.6 m body centred on <paramref name="q"/> (three rays across it,
        /// along the ring), and that surface's slope in degrees; NaN where no boulder is under it.</summary>
        private static float RockTop(HashSet<Collider> mine, Vector2 q, Vector2 along, out float slope)
        {
            float top = float.NaN; slope = float.NaN;
            for (int o = -1; o <= 1; o++)
            {
                Vector2 r = q + along * (0.3f * o);
                foreach (RaycastHit h in Physics.RaycastAll(new Vector3(r.x, 600f, r.y), Vector3.down, 1200f, 1))
                    if (mine.Contains(h.collider) && (float.IsNaN(top) || h.point.y > top))
                    { top = h.point.y; slope = Vector3.Angle(h.normal, Vector3.up); }
            }
            return top;
        }

        /// <summary>Flood the surface profile (index 0 = furthest inside, <paramref name="last"/> = the barrier's face) from
        /// <paramref name="start"/> with the controller's moves: walk to the next spot when it rises no more than a step
        /// (0.35 m), or no more than 45° between the two spots on a walkable surface, and jump from any walkable spot along the arc
        /// feet = takeoff + 4·apex·x/L·(1 − x/L), reaching every spot the arc clears until the first surface above it.
        /// True when the face is reached: only the invisible barrier would stop them there.</summary>
        private static bool ProfileReachesFace(float[] su, float[] ss, int start, int last, float stepD, float apex, float length)
        {
            var reach = new bool[last + 1];
            reach[start] = true;
            int span = Mathf.FloorToInt(length / stepD);
            for (int i = start; i <= last; i++)
            {
                if (!reach[i] || float.IsNaN(su[i])) continue;
                float rise = i < last ? su[i + 1] - su[i] : 0f;   // a rise is walked when it is a step, or a ≤ 45° slope all the way
                // up a step only from a surface they stand on (the controller cannot climb between steep facets); down always
                if (i < last && !float.IsNaN(su[i + 1]) && (rise <= 0f || (Walkable(ss[i]) && (rise <= 0.35f || (rise <= stepD && Walkable(ss[i + 1])))))) reach[i + 1] = true;
                if (!Walkable(ss[i])) continue;
                for (int k = i + 1; k <= Mathf.Min(last, i + span); k++)
                {
                    if (float.IsNaN(su[k])) break;
                    float f = (k - i) * stepD / length;
                    if (su[k] > su[i] + 4f * apex * f * (1f - f)) break;
                    reach[k] = true;
                }
            }
            return reach[last];
        }

        private static bool Walkable(float slope) => !float.IsNaN(slope) && slope <= WalkableSlope;

        private static float Slope(Dictionary<Collider, int> kinds, Vector2 p, out float y)
        {
            TownCensus.Column(kinds, p.x, p.y, out y, out float slope, out _, out _, out _);
            return slope;
        }

        private static bool Ground(Dictionary<Collider, int> kinds, Vector2 p, out float y)
        {
            TownCensus.Column(kinds, p.x, p.y, out y, out _, out _, out _, out _);
            return !float.IsNaN(y);
        }

        // ------------------------------------------------------------------------------------------------------------
        // Ring contact, measured on the baked NavMesh
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>Where can the player still reach a ring barrier? Takes the baked NavMesh's triangles, welds them, keeps
        /// the island connected to the pub, and marks every half-metre of ring line that island comes within
        /// <see cref="ContactDistance"/> of (measured inward from the line). Each contact is classified by the ground just
        /// beyond the line: walkable there means nothing visible stops the player — only the barrier.</summary>
        public static string MeasureContact(string outSub)
        {
            var cfg = AssetDatabase.LoadAssetAtPath<TownBoundsConfig>(AddMeshColliders.TownBoundsConfigPath);
            GameObject map = GameObject.Find("game map");
            if (cfg == null || map == null) return "missing";
            Physics.SyncTransforms();
            Dictionary<Collider, int> kinds = TownCensus.Kinds(map);
            Vector2[] poly = cfg.SafePlayableArea;
            List<Side> sides = RingSides(poly);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            Vector3[] v = tri.vertices; int[] idx = tri.indices;
            // weld by position (5 cm) and union the triangles' vertices
            var weld = new Dictionary<long, int>();
            var vid = new int[v.Length];
            for (int i = 0; i < v.Length; i++)
            {
                long key = ((long)Mathf.RoundToInt(v[i].x * 20f) + 1000000L) * 4000000000000L
                         + ((long)Mathf.RoundToInt(v[i].y * 20f) + 100000L) * 20000000L
                         + ((long)Mathf.RoundToInt(v[i].z * 20f) + 1000000L);
                if (!weld.TryGetValue(key, out int w)) { w = weld.Count; weld[key] = w; }
                vid[i] = w;
            }
            var parent = new int[weld.Count];
            for (int i = 0; i < parent.Length; i++) parent[i] = i;
            System.Func<int, int> find = null;
            find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            for (int t = 0; t < idx.Length; t += 3)
            {
                int a = find(vid[idx[t]]), b = find(vid[idx[t + 1]]), c = find(vid[idx[t + 2]]);
                parent[b] = a; parent[find(c)] = find(a);
            }
            // the pub's island
            Vector3 pub = new Vector3(31f, 0.455f, 19f);
            GameObject sp = GameObject.Find("EventManager/SpawnPoint");
            if (sp != null) pub = sp.transform.position;
            int pubRoot = -1; float best = float.MaxValue;
            for (int i = 0; i < v.Length; i++)
            {
                float d = (v[i] - pub).sqrMagnitude;
                if (d < best) { best = d; pubRoot = find(vid[i]); }
            }
            // bins along each side
            var bins = new Dictionary<int, bool[]>();
            foreach (Side s in sides) bins[s.index] = new bool[Mathf.RoundToInt(s.length / 0.5f) + 1];
            int mainTris = 0;
            for (int t = 0; t < idx.Length; t += 3)
            {
                if (find(vid[idx[t]]) != pubRoot) continue;
                mainTris++;
                Vector3 A = v[idx[t]], B = v[idx[t + 1]], C = v[idx[t + 2]];
                Vector2 a2 = new Vector2(A.x, A.z), b2 = new Vector2(B.x, B.z), c2 = new Vector2(C.x, C.z);
                Vector2 mn = Vector2.Min(a2, Vector2.Min(b2, c2)), mx = Vector2.Max(a2, Vector2.Max(b2, c2));
                foreach (Side s in sides)
                {
                    // quick reject: the triangle's box must come within the contact band of this side
                    if (SegBoxDistance(s.a, s.b, mn, mx) > ContactDistance + 0.5f) continue;
                    // rasterise the part of the triangle inside the band at 0.2 m
                    Vector2 bmn = Vector2.Max(mn, Vector2.Min(s.a, s.b) - Vector2.one * 2f);
                    Vector2 bmx = Vector2.Min(mx, Vector2.Max(s.a, s.b) + Vector2.one * 2f);
                    for (float x = Mathf.Floor(bmn.x / 0.2f) * 0.2f; x <= bmx.x; x += 0.2f)
                        for (float z = Mathf.Floor(bmn.y / 0.2f) * 0.2f; z <= bmx.y; z += 0.2f)
                        {
                            Vector2 q = new Vector2(x, z);
                            Vector2 rel = q - s.a;
                            float along = Vector2.Dot(rel, s.dir), inward = Vector2.Dot(rel, s.inward);
                            if (inward < -0.5f || inward > ContactDistance || along < 0f || along > s.length) continue;
                            if (!InTriangle(q, a2, b2, c2)) continue;
                            bins[s.index][Mathf.Clamp(Mathf.RoundToInt(along / 0.5f), 0, bins[s.index].Length - 1)] = true;
                        }
                }
            }
            var csv = new StringBuilder("side,u,x,z,contact,outside_walkable,boulder_across\n");
            var log = new StringBuilder($"Ring contact on the baked NavMesh {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}: {v.Length} vertices, {idx.Length / 3} triangles, " +
                                        $"{mainTris} on the pub's island; contact = that island within {ContactDistance} m of the barrier line ({sw.Elapsed.TotalSeconds:0.0} s)\n");
            GameObject edgeRoot = GameObject.Find(RootName);
            var mine = edgeRoot != null ? new HashSet<Collider>(edgeRoot.GetComponentsInChildren<Collider>()) : new HashSet<Collider>();
            float ringM = 0f, contactM = 0f, openM = 0f, bareM = 0f;
            foreach (Side s in sides)
            {
                bool[] bin = bins[s.index];
                float sc = 0f, so = 0f, sn = 0f;
                var runs = new StringBuilder();
                Vector2 runStart = Vector2.zero; bool inRun = false; Vector2 last = Vector2.zero;
                for (int k = 0; k < bin.Length; k++)
                {
                    Vector2 p = s.a + s.dir * Mathf.Min(s.length, k * 0.5f);
                    bool outsideWalk = Walkable(Slope(kinds, p - s.inward * 1f, out _)) || Walkable(Slope(kinds, p - s.inward * 3f, out _));
                    bool boulder = false;
                    if (bin[k])
                        for (float d = -1f; d <= 9f && !boulder; d += 1f)
                        {
                            Vector2 q = p + s.inward * d;
                            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(q.x, 400f, q.y), Vector3.down, 900f))
                                if (mine.Contains(h.collider)) { boulder = true; break; }
                        }
                    csv.AppendLine($"{s.index},{F(k * 0.5f, "0.#")},{F(p.x, "0.#")},{F(p.y, "0.#")},{(bin[k] ? 1 : 0)},{(outsideWalk ? 1 : 0)},{(boulder ? 1 : 0)}");
                    float seg = s.length / bin.Length;
                    if (bin[k]) { sc += seg; if (outsideWalk) { so += seg; if (!boulder) sn += seg; } }
                    bool open = bin[k] && outsideWalk;
                    if (open && !inRun) { inRun = true; runStart = p; }
                    if (!open && inRun) { inRun = false; runs.Append($" ({F(runStart.x, "0")},{F(runStart.y, "0")})-({F(last.x, "0")},{F(last.y, "0")})"); }
                    last = p;
                }
                if (inRun) runs.Append($" ({F(runStart.x, "0")},{F(runStart.y, "0")})-({F(last.x, "0")},{F(last.y, "0")})");
                ringM += s.length; contactM += sc; openM += so; bareM += sn;
                log.AppendLine($"{s.name}: {F(s.length, "0")} m; barrier reached on {F(sc, "0")} m; of which walkable beyond {F(so, "0")} m, and of that with no boulder across the line (an invisible stop) {F(sn, "0.#")} m{(runs.Length > 0 ? ";" + runs : "")}");
            }
            log.AppendLine($"RING TOTAL: {F(ringM, "0")} m of barrier; reached {F(contactM, "0")} m; reached with walkable ground beyond {F(openM, "0.#")} m; of that with NO BOULDER across the line (an invisible stop) {F(bareM, "0.#")} m");
            string dir = TownCensus.Root + "/" + outSub;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "contact.txt"), log.ToString());
            File.WriteAllText(Path.Combine(dir, "contact.csv"), csv.ToString());
            Debug.Log("[TownEdge] " + log.ToString().Replace("\n", " | "));
            return log.ToString();
        }

        private static float SegBoxDistance(Vector2 a, Vector2 b, Vector2 mn, Vector2 mx)
        {
            // distance from the segment to the box, sampled (good enough for a reject test)
            float len = Vector2.Distance(a, b), best = float.MaxValue;
            int n = Mathf.Max(1, Mathf.CeilToInt(len / 1f));
            Vector2 c = (mn + mx) * 0.5f, e = (mx - mn) * 0.5f;
            for (int i = 0; i <= n; i++)
            {
                Vector2 p = Vector2.Lerp(a, b, (float)i / n);
                Vector2 d = new Vector2(Mathf.Max(0f, Mathf.Abs(p.x - c.x) - e.x), Mathf.Max(0f, Mathf.Abs(p.y - c.y) - e.y));
                best = Mathf.Min(best, d.magnitude);
            }
            return best;
        }

        private static bool InTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = (p.x - b.x) * (a.y - b.y) - (a.x - b.x) * (p.y - b.y);
            float d2 = (p.x - c.x) * (b.y - c.y) - (b.x - c.x) * (p.y - c.y);
            float d3 = (p.x - a.x) * (c.y - a.y) - (c.x - a.x) * (p.y - a.y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }
}
