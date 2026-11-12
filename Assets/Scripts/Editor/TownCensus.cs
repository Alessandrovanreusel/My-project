using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// Story 2.2, Task 1a: measures the town as Unity holds it, in EDIT mode (static geometry only), and writes
    /// the evidence every later task is drawn on.
    ///
    ///   census.json   — every map renderer grouped by class (base name, or the particle family for scatter):
    ///                   count, triangles, bounds, heights, colliders, and how many sit on the surface vs buried.
    ///   topdown.png   — an orthographic Unity render of the playable area, plus topdown.json holding the
    ///                   world → pixel transform. Every later trace is drawn on this image.
    ///   grid_*.f32/u8 — a 2 m column grid over the same area: the top surface a person could stand on once the
    ///                   scatter is gone (y and slope), the water surface, and what was hit. Python reads these.
    ///   road_edges.csv — the raised road slab's edge height above the ground beside it, every 2 m, both sides.
    ///   rig.json      — the oil rig: its bounds, its deck heights, and its distance from the shore.
    ///
    /// Read-only: it renders with a temporary hidden camera and never saves the scene. If the scene was clean
    /// when it started and something marked it dirty, it is reopened from disk at the end.
    /// </summary>
    public static class TownCensus
    {
        public const string Root = "_bmad-output/verification/2-2-walk-the-whole-town";
        public const string CensusDir = Root + "/census";

        // The top-down canvas (Unity metres). Covers the plain, the ring's inner faces, the shore and the oil rig.
        public const float XMin = -700f, XMax = 300f, ZMin = -450f, ZMax = 650f;
        public const float MetresPerPixel = 0.5f;
        public const float GridStep = 2f;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string F(float v, string f = "0.###") => v.ToString(f, Inv);

        /// <summary>The class a map renderer belongs to: the particle family for scatter ("scatter:Cube"), else
        /// the base name (text before the first '.').</summary>
        public static string ClassOf(string objectName)
        {
            string[] parts = objectName.Split('|');
            if (parts.Length >= 2) return "scatter:" + parts[1].Split('.')[0];
            int i = objectName.IndexOf('.');
            return i > 0 ? objectName.Substring(0, i) : objectName;
        }

        public static bool IsWater(Renderer r) =>
            r != null && r.sharedMaterial != null && r.sharedMaterial.name.StartsWith("Water");

        [MenuItem("Tools/Town/Census (edit mode)")]
        public static void Run()
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[Town] Census runs in edit mode — stop play first."); return; }
            Scene scene = SceneManager.GetActiveScene();
            bool wasDirty = scene.isDirty;
            Directory.CreateDirectory(CensusDir);
            var log = new StringBuilder($"TOWN CENSUS {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} scene {scene.path}\n");
            try
            {
                GameObject map = GameObject.Find("game map");
                if (map == null) { Debug.LogError("[Town] No 'game map' in the open scene."); return; }
                Physics.SyncTransforms();
                var kinds = Classes(map, log);
                TopDown(log);
                Grid(kinds, log);
                RoadEdges(kinds, log);
                Rig(map, kinds, log);
            }
            finally
            {
                File.WriteAllText(Path.Combine(CensusDir, "census-log.txt"), log.ToString());
                if (!wasDirty && scene.isDirty && File.Exists(scene.path))
                {
                    EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
                    Debug.Log("[Town] The census dirtied the scene — reopened it from disk.");
                }
                Debug.Log("[Town] Census done → " + CensusDir);
            }
        }

        // ------------------------------------------------------------------------------------------------
        // census.json
        // ------------------------------------------------------------------------------------------------

        private class Group
        {
            public int count, colliders, surface, buried; public long tris;
            public Bounds b; public bool init;
            public float hMin = float.MaxValue, hMax, fpMin = float.MaxValue, fpMax;
            public string example; public bool water;
        }

        /// <summary>Collider → kind: 0 surface, 1 water, 2 scatter (no file written).</summary>
        public static Dictionary<Collider, int> Kinds(GameObject map)
        {
            var kinds = new Dictionary<Collider, int>();
            foreach (MeshRenderer r in map.GetComponentsInChildren<MeshRenderer>(true))
            {
                bool water = IsWater(r);
                bool scatter = ClassOf(r.name).StartsWith("scatter:");
                foreach (MeshCollider c in r.GetComponents<MeshCollider>()) kinds[c] = water ? 1 : scatter ? 2 : 0;
            }
            return kinds;
        }

        /// <summary>Collider → kind: 0 surface, 1 water, 2 scatter. Also writes census.json.</summary>
        private static Dictionary<Collider, int> Classes(GameObject map, StringBuilder log)
        {
            var kinds = new Dictionary<Collider, int>();
            var groups = new SortedDictionary<string, Group>();
            int renderers = 0, cols = 0;
            foreach (MeshRenderer r in map.GetComponentsInChildren<MeshRenderer>(true))
            {
                renderers++;
                string cls = ClassOf(r.name);
                if (!groups.TryGetValue(cls, out Group g)) { g = new Group { example = r.name }; groups[cls] = g; }
                g.count++;
                var mf = r.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null)
                    for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) g.tris += mf.sharedMesh.GetIndexCount(s) / 3;
                Bounds rb = r.bounds;
                if (!g.init) { g.b = rb; g.init = true; } else g.b.Encapsulate(rb);
                g.hMin = Mathf.Min(g.hMin, rb.size.y); g.hMax = Mathf.Max(g.hMax, rb.size.y);
                float fp = Mathf.Max(rb.size.x, rb.size.z);
                g.fpMin = Mathf.Min(g.fpMin, fp); g.fpMax = Mathf.Max(g.fpMax, fp);
                if (rb.max.y < -20f) g.buried++; else g.surface++;
                bool water = IsWater(r);
                g.water |= water;
                foreach (MeshCollider c in r.GetComponents<MeshCollider>())
                {
                    g.colliders++; cols++;
                    kinds[c] = water ? 1 : cls.StartsWith("scatter:") ? 2 : 0;
                }
            }
            var sb = new StringBuilder("{\n");
            sb.Append($" \"renderers\": {renderers}, \"mesh_colliders\": {cols}, \"classes\": {groups.Count},\n");
            sb.Append(" \"note_deactivated_child\": \"SampleScene overrides m_IsActive 0 on FBX fileID 65702495048144492; no sub-asset of today's FBX has that fileID (68,837 checked, 0 cameras, 0 lights): an orphaned override, most likely the old embedded Blender camera. Kept as is.\",\n");
            sb.Append(" \"groups\": [\n");
            bool first = true;
            foreach (var kv in groups)
            {
                Group g = kv.Value;
                if (!first) sb.Append(",\n");
                first = false;
                sb.Append($"  {{\"class\": \"{Esc(kv.Key)}\", \"count\": {g.count}, \"triangles\": {g.tris}, \"colliders\": {g.colliders}, " +
                          $"\"surface\": {g.surface}, \"buried\": {g.buried}, \"water\": {(g.water ? "true" : "false")}, " +
                          $"\"min\": [{F(g.b.min.x)}, {F(g.b.min.y)}, {F(g.b.min.z)}], \"max\": [{F(g.b.max.x)}, {F(g.b.max.y)}, {F(g.b.max.z)}], " +
                          $"\"height\": [{F(g.hMin)}, {F(g.hMax)}], \"footprint\": [{F(g.fpMin)}, {F(g.fpMax)}], \"example\": \"{Esc(g.example)}\"}}");
            }
            sb.Append("\n ]\n}\n");
            File.WriteAllText(Path.Combine(CensusDir, "census.json"), sb.ToString());
            log.AppendLine($"census: {renderers} renderers, {cols} mesh colliders, {groups.Count} classes");
            return kinds;
        }

        private static string Esc(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"");

        // ------------------------------------------------------------------------------------------------
        // topdown.png + topdown.json
        // ------------------------------------------------------------------------------------------------

        public static void TopDown(StringBuilder log)
        {
            int w = Mathf.RoundToInt((XMax - XMin) / MetresPerPixel), h = Mathf.RoundToInt((ZMax - ZMin) / MetresPerPixel);
            var go = new GameObject("TownCensus TopDown (temp)") { hideFlags = HideFlags.HideAndDontSave };
            try
            {
                Camera cam = go.AddComponent<Camera>();
                cam.enabled = false;
                cam.orthographic = true;
                cam.orthographicSize = (ZMax - ZMin) * 0.5f;
                cam.aspect = (float)w / h;
                cam.nearClipPlane = 1f; cam.farClipPlane = 1200f;
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = Color.black;
                // Euler(90,0,0): forward = -Y, up = +Z → image top = +z, image right = +x.
                go.transform.SetPositionAndRotation(new Vector3((XMin + XMax) * 0.5f, 600f, (ZMin + ZMax) * 0.5f), Quaternion.Euler(90f, 0f, 0f));
                var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32);
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture prev = RenderTexture.active;
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                RenderTexture.active = prev;
                cam.targetTexture = null;
                rt.Release();
                Object.DestroyImmediate(rt);
                File.WriteAllBytes(Path.Combine(CensusDir, "topdown.png"), tex.EncodeToPNG());
                Object.DestroyImmediate(tex);
            }
            finally { Object.DestroyImmediate(go); }
            File.WriteAllText(Path.Combine(CensusDir, "topdown.json"),
                "{\n \"note\": \"Unity orthographic top-down, image top = +z, right = +x. px = (x - x_min) / m_per_px, py_from_top = (z_max - z) / m_per_px.\",\n" +
                $" \"x_min\": {F(XMin)}, \"x_max\": {F(XMax)}, \"z_min\": {F(ZMin)}, \"z_max\": {F(ZMax)}, \"m_per_px\": {F(MetresPerPixel)}, " +
                $"\"width\": {w}, \"height\": {h}\n}}\n");
            log.AppendLine($"topdown: {w}x{h} px, {MetresPerPixel} m/px");
        }

        // ------------------------------------------------------------------------------------------------
        // the column grid
        // ------------------------------------------------------------------------------------------------

        private static readonly RaycastHit[] Hits = new RaycastHit[64];

        /// <summary>Top surface (not scatter, not water), its slope, the top water hit, and the top hit's kind at one column.</summary>
        public static void Column(Dictionary<Collider, int> kinds, float x, float z, out float surfY, out float slope,
                                  out float waterY, out int topKind, out Collider surfCol)
        {
            surfY = float.NaN; slope = float.NaN; waterY = float.NaN; topKind = -1; surfCol = null;
            int n = Physics.RaycastNonAlloc(new Vector3(x, 400f, z), Vector3.down, Hits, 900f, ~0, QueryTriggerInteraction.Ignore);
            float topY = float.NegativeInfinity;
            for (int i = 0; i < n; i++)
            {
                RaycastHit h = Hits[i];
                int k = kinds.TryGetValue(h.collider, out int kk) ? kk : 3;   // 3 = not the map
                if (k == 3) continue;
                if (h.point.y > topY) { topY = h.point.y; topKind = k; }
                if (k == 1) { if (float.IsNaN(waterY) || h.point.y > waterY) waterY = h.point.y; continue; }
                if (k == 2) continue;
                if (float.IsNaN(surfY) || h.point.y > surfY)
                {
                    surfY = h.point.y;
                    slope = Vector3.Angle(h.normal, Vector3.up);
                    surfCol = h.collider;
                }
            }
        }

        private static void Grid(Dictionary<Collider, int> kinds, StringBuilder log)
        {
            int nx = Mathf.RoundToInt((XMax - XMin) / GridStep), nz = Mathf.RoundToInt((ZMax - ZMin) / GridStep);
            var surf = new float[nx * nz]; var slope = new float[nx * nz]; var water = new float[nx * nz];
            var kind = new byte[nx * nz];
            double t0 = EditorApplication.timeSinceStartup;
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    float x = XMin + (i + 0.5f) * GridStep, z = ZMin + (j + 0.5f) * GridStep;
                    Column(kinds, x, z, out float sy, out float sl, out float wy, out int tk, out _);
                    int idx = j * nx + i;
                    surf[idx] = sy; slope[idx] = sl; water[idx] = wy; kind[idx] = (byte)(tk < 0 ? 255 : tk);
                }
            WriteF32(Path.Combine(CensusDir, "grid_surface_y.f32"), surf);
            WriteF32(Path.Combine(CensusDir, "grid_slope_deg.f32"), slope);
            WriteF32(Path.Combine(CensusDir, "grid_water_y.f32"), water);
            File.WriteAllBytes(Path.Combine(CensusDir, "grid_top_kind.u8"), kind);
            File.WriteAllText(Path.Combine(CensusDir, "grid.json"),
                $"{{\"nx\": {nx}, \"nz\": {nz}, \"step\": {F(GridStep)}, \"x_min\": {F(XMin)}, \"z_min\": {F(ZMin)}, " +
                "\"order\": \"row-major, row j = z index from z_min, cell centre = min + (i + 0.5) * step\", " +
                "\"surface\": \"top hit that is neither scatter nor water (NaN = none)\", \"kind\": \"top hit: 0 surface, 1 water, 2 scatter, 255 nothing\"}\n");
            log.AppendLine($"grid: {nx}x{nz} columns at {GridStep} m in {F((float)(EditorApplication.timeSinceStartup - t0), "0.0")} s");
        }

        private static void WriteF32(string path, float[] a)
        {
            var bytes = new byte[a.Length * 4];
            System.Buffer.BlockCopy(a, 0, bytes, 0, bytes.Length);
            File.WriteAllBytes(path, bytes);
        }

        // ------------------------------------------------------------------------------------------------
        // the road slab's edges
        // ------------------------------------------------------------------------------------------------

        private static void RoadEdges(Dictionary<Collider, int> kinds, StringBuilder log)
        {
            var roads = new List<Collider>();
            foreach (var kv in kinds)
                if (kv.Key.name.StartsWith("Plane.004_M3_0")) roads.Add(kv.Key);
            var csv = new StringBuilder("x,road_z_min,road_z_max,road_y_south,ground_y_south,edge_south_m,road_y_north,ground_y_north,edge_north_m,ground_south_hit,ground_north_hit\n");
            int rows = 0;
            for (float x = -620f; x <= 284f; x += 2f)
            {
                float zLo = float.NaN, zHi = float.NaN, yLo = 0, yHi = 0;
                for (float z = -20f; z <= 12f; z += 0.05f)
                {
                    if (!RoadY(roads, x, z, out float y)) continue;
                    if (float.IsNaN(zLo)) { zLo = z; yLo = y; }
                    zHi = z; yHi = y;
                }
                if (float.IsNaN(zLo)) continue;
                GroundBeside(kinds, roads, x, zLo - 0.3f, out float gS, out string hS);
                GroundBeside(kinds, roads, x, zHi + 0.3f, out float gN, out string hN);
                csv.AppendLine($"{F(x)},{F(zLo)},{F(zHi)},{F(yLo)},{F(gS)},{F(yLo - gS)},{F(yHi)},{F(gN)},{F(yHi - gN)},{hS},{hN}");
                rows++;
            }
            File.WriteAllText(Path.Combine(CensusDir, "road_edges.csv"), csv.ToString());
            log.AppendLine($"road edges: {rows} stations over {roads.Count} road colliders");
        }

        private static bool RoadY(List<Collider> roads, float x, float z, out float y)
        {
            y = float.NegativeInfinity;
            var ray = new Ray(new Vector3(x, 60f, z), Vector3.down);
            foreach (Collider c in roads)
                if (c.Raycast(ray, out RaycastHit h, 120f) && h.point.y > y) y = h.point.y;
            return !float.IsNegativeInfinity(y);
        }

        private static void GroundBeside(Dictionary<Collider, int> kinds, List<Collider> roads, float x, float z, out float y, out string hit)
        {
            y = float.NaN; hit = "";
            int n = Physics.RaycastNonAlloc(new Vector3(x, 60f, z), Vector3.down, Hits, 200f, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = Hits[i].collider;
                if (roads.Contains(c)) continue;
                if (!kinds.TryGetValue(c, out int k) || k != 0) continue;
                if (float.IsNaN(y) || Hits[i].point.y > y) { y = Hits[i].point.y; hit = c.name.Replace(",", " "); }
            }
        }

        // ------------------------------------------------------------------------------------------------
        // the oil rig
        // ------------------------------------------------------------------------------------------------

        private static void Rig(GameObject map, Dictionary<Collider, int> kinds, StringBuilder log)
        {
            bool init = false; Bounds rb = default; int n = 0;
            foreach (MeshRenderer r in map.GetComponentsInChildren<MeshRenderer>(true))
            {
                Vector3 c = r.bounds.center;
                if (c.z < 480f || c.z > 600f || c.x < -460f || c.x > -320f || IsWater(r)) continue;
                n++;
                if (!init) { rb = r.bounds; init = true; } else rb.Encapsulate(r.bounds);
            }
            var decks = new SortedDictionary<int, int>();
            for (float x = rb.min.x; x <= rb.max.x; x += 1f)
                for (float z = rb.min.z; z <= rb.max.z; z += 1f)
                {
                    Column(kinds, x, z, out float sy, out float sl, out _, out _, out _);
                    if (!float.IsNaN(sy) && sl < 30f && sy > 20f) { int k = Mathf.RoundToInt(sy); decks[k] = decks.TryGetValue(k, out int v) ? v + 1 : 1; }
                }
            // Nearest dry ground: scan the grid around the rig for a column whose surface is above the water.
            float best = float.MaxValue; Vector3 bestP = Vector3.zero;
            for (float x = -700f; x <= 300f; x += 2f)
                for (float z = 200f; z <= 650f; z += 2f)
                {
                    Column(kinds, x, z, out float sy, out _, out float wy, out _, out _);
                    if (float.IsNaN(sy) || (!float.IsNaN(wy) && wy >= sy)) continue;
                    if (sy > 20f) continue;   // the rig's own decks
                    Vector3 p = new Vector3(x, sy, z);
                    float d = Mathf.Sqrt(rb.SqrDistance(new Vector3(x, rb.center.y, z)));
                    if (d < best) { best = d; bestP = p; }
                }
            var sb = new StringBuilder("{\n");
            sb.Append($" \"renderers\": {n}, \"min\": [{F(rb.min.x)}, {F(rb.min.y)}, {F(rb.min.z)}], \"max\": [{F(rb.max.x)}, {F(rb.max.y)}, {F(rb.max.z)}],\n");
            sb.Append(" \"deck_heights_m_cells\": {");
            bool first = true;
            foreach (var kv in decks) { if (!first) sb.Append(", "); first = false; sb.Append($"\"{kv.Key}\": {kv.Value}"); }
            sb.Append("},\n");
            sb.Append($" \"nearest_dry_ground\": [{F(bestP.x)}, {F(bestP.y)}, {F(bestP.z)}], \"distance_to_rig_bounds_m\": {F(best)}\n}}\n");
            File.WriteAllText(Path.Combine(CensusDir, "rig.json"), sb.ToString());
            log.AppendLine($"rig: {n} renderers, bounds {rb.min} .. {rb.max}, nearest dry ground {bestP} at {F(best)} m");
        }
    }
}
