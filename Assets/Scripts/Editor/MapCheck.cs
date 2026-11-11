using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// Story 2.2, AC1 (AR9): is <c>Assets/Models/game map .fbx</c> the newest town-map .blend?
    /// <c>Tools/Town/Check Map Against .blend</c> runs HEADLESS Blender as a subprocess (never Alexv's live session, never
    /// the Blender MCP, never a save of the .blend) with <c>tools/map/map_check.py</c>, which measures what an export of
    /// the .blend would contain (depsgraph-evaluated instances) and what the FBX contains (imported into an empty scene).
    /// This class adds Unity's own column (what the importer made of the FBX), compares the three by base name, and
    /// writes <c>report.json</c> + <c>report.md</c> plus ONE console line: MATCH, DIFFERS (n names) or CANNOT CHECK (why).
    ///
    /// Rules: count and triangles must be equal; non-instanced objects' exact vertex bounds and instances' origin bounds
    /// within 1 cm. A bounds difference on a group whose .blend objects are animated, constrained or modified ("volatile")
    /// is listed as EXPLAINED and does not flip the verdict (measured: an animated object 0.13 m, a Solidify 0.06 m,
    /// triangles identical); a count or triangle difference always does.
    ///
    /// Fail-soft (NFR8): no Blender, no .blend (the shared folder is git-ignored and absent on other machines), a timeout
    /// or a crash give ONE warning and CANNOT CHECK — never an exception. Paths can be overridden in EditorPrefs.
    /// </summary>
    public static class MapCheck
    {
        public const string FbxPath = "Assets/Models/game map .fbx";
        public const string DefaultBlend = "CAMERA GAME SHARED FOLDER/Word Map/game map .blend";
        public const string DefaultBlender = @"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe";
        public const string BlenderPrefKey = "CameraGame.MapCheck.BlenderPath";
        public const string BlendPrefKey = "CameraGame.MapCheck.BlendPath";
        public const string OutPrefKey = "CameraGame.MapCheck.OutputDir";
        public const float ToleranceMetres = 0.01f;
        private const int TimeoutMs = 15 * 60 * 1000;
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        [System.Serializable] public class Group { public string name; public int count; public long triangles; public int instances; public int volatile_; public float[] min; public float[] max; public float[] origin_min; public float[] origin_max; }
        [System.Serializable] private class Raw { public string blend; public string fbx; public Group[] blend_groups; public Group[] fbx_groups; public string[] fbx_cameras; public string[] fbx_lights; public string[] blend_cameras; public string[] blend_lights; }

        public static string BlenderPath => EditorPrefs.GetString(BlenderPrefKey, DefaultBlender);
        public static string BlendPath => EditorPrefs.GetString(BlendPrefKey, DefaultBlend);
        public static string OutputDir => EditorPrefs.GetString(OutPrefKey, TownCensus.Root + "/map-check/real");

        [MenuItem("Tools/Town/Check Map Against .blend")]
        public static void Menu() => Run(BlenderPath, BlendPath, OutputDir);

        /// <summary>
        /// Re-exports the .blend OVER <c>Assets/Models/game map .fbx</c> in place (same path, same .meta, so the GUID and
        /// — with the same names and hierarchy — every child fileID survive), with the settings proven in
        /// <c>map-check/reexport-proof.md</c> (tools/map/map_export.py). Refuses unless the check has just said DIFFERS:
        /// a re-export adds a ~77 MB LFS object and is a decision, never a routine. After it: re-import, count the scene
        /// colliders that still resolve, and run the drunk's route trace.
        /// </summary>
        [MenuItem("Tools/Town/Re-export Map from .blend")]
        public static void ReexportMenu()
        {
            string verdict = Run(BlenderPath, BlendPath, OutputDir);
            if (!verdict.StartsWith("DIFFERS")) { Debug.LogWarning("[MapCheck] Re-export refused: the check says " + verdict + " — nothing to re-export."); return; }
            var psi = new ProcessStartInfo(BlenderPath,
                $"-b --factory-startup \"{Path.GetFullPath(BlendPath)}\" --python \"{Path.GetFullPath("tools/map/map_export.py")}\" -- \"{Path.GetFullPath(FbxPath)}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            using (Process p = Process.Start(psi))
            {
                System.Threading.Tasks.Task<string> so = p.StandardOutput.ReadToEndAsync();
                if (!p.WaitForExit(TimeoutMs)) { try { p.Kill(); } catch { } Debug.LogWarning("[MapCheck] Re-export timed out."); return; }
                File.WriteAllText(Path.Combine(OutputDir, "reexport-log.txt"), so.Result);
            }
            AssetDatabase.ImportAsset(FbxPath, ImportAssetOptions.ForceUpdate);
            Debug.Log("[MapCheck] Re-exported over " + FbxPath + " and re-imported. Now count the resolving colliders and run the route trace.");
        }

        /// <summary>
        /// AC1/AR8: switch camera (and light) import off on the town map, so no Blender camera can ever override Main
        /// Camera, re-import, and record the cost and the read-backs (map-check/camera-import-off.txt): the importer
        /// flags, the renderers under the scene's map, and how many scene MeshColliders resolve a mesh before and after.
        /// </summary>
        [MenuItem("Tools/Town/Switch Map Camera and Light Import Off")]
        public static void CameraImportOff()
        {
            var mi = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
            var sb = new StringBuilder($"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} — {FbxPath}\n");
            sb.AppendLine($"before: importCameras {mi.importCameras}, importLights {mi.importLights}; {SceneMapCounts()}");
            mi.importCameras = false;
            mi.importLights = false;
            var sw = Stopwatch.StartNew();
            mi.SaveAndReimport();
            sb.AppendLine($"SaveAndReimport took {sw.Elapsed.TotalSeconds:0.0} s");
            var after = (ModelImporter)AssetImporter.GetAtPath(FbxPath);
            sb.AppendLine($"after:  importCameras {after.importCameras}, importLights {after.importLights}; {SceneMapCounts()}");
            Directory.CreateDirectory(TownCensus.Root + "/map-check");
            File.AppendAllText(TownCensus.Root + "/map-check/camera-import-off.txt", sb.ToString());
            Debug.Log("[MapCheck] " + sb.ToString().Replace("\n", " | "));
        }

        /// <summary>Renderers under the open scene's map, and its MeshColliders that resolve a mesh / do not.</summary>
        public static string SceneMapCounts()
        {
            GameObject map = GameObject.Find("game map");
            if (map == null) return "no 'game map' in the open scene";
            int rend = map.GetComponentsInChildren<MeshRenderer>(true).Length, ok = 0, nul = 0, inactive = 0;
            foreach (MeshCollider c in map.GetComponentsInChildren<MeshCollider>(true)) { if (c.sharedMesh != null) ok++; else nul++; }
            foreach (Transform t in map.GetComponentsInChildren<Transform>(true)) if (!t.gameObject.activeSelf) inactive++;
            return $"scene map: {rend} renderers, MeshColliders resolving {ok} / null {nul}, inactive children {inactive}";
        }

        /// <summary>Runs the check and returns the verdict line. Never throws.</summary>
        public static string Run(string blender, string blend, string outDir)
        {
            string verdict;
            try { verdict = RunInner(blender, blend, outDir); }
            catch (System.Exception e) { verdict = $"CANNOT CHECK ({e.GetType().Name}: {e.Message})"; Warn(outDir, verdict); return verdict; }
            if (verdict.StartsWith("CANNOT")) Warn(outDir, verdict);
            else Debug.Log("[MapCheck] " + verdict + " — " + Path.Combine(outDir, "report.md"));
            return verdict;
        }

        private static void Warn(string outDir, string verdict)
        {
            Debug.LogWarning("[MapCheck] " + verdict);
            try { Directory.CreateDirectory(outDir); File.WriteAllText(Path.Combine(outDir, "report.md"), "# Map check\n\n**" + verdict + "**\n"); }
            catch (IOException) { }
        }

        private static string RunInner(string blender, string blend, string outDir)
        {
            if (string.IsNullOrEmpty(blender) || !File.Exists(blender)) return $"CANNOT CHECK (no Blender at '{blender}')";
            if (string.IsNullOrEmpty(blend) || !File.Exists(blend)) return $"CANNOT CHECK (no .blend at '{blend}')";
            if (!File.Exists(FbxPath)) return $"CANNOT CHECK (no FBX at '{FbxPath}')";
            Directory.CreateDirectory(outDir);
            string json = Path.GetFullPath(Path.Combine(outDir, "blender.json"));
            if (File.Exists(json)) File.Delete(json);
            var psi = new ProcessStartInfo(blender,
                $"-b --factory-startup \"{Path.GetFullPath(blend)}\" --python \"{Path.GetFullPath("tools/map/map_check.py")}\" -- \"{Path.GetFullPath(FbxPath)}\" \"{json}\"")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            var sw = Stopwatch.StartNew();
            string stdout;
            using (Process p = Process.Start(psi))
            {
                System.Threading.Tasks.Task<string> so = p.StandardOutput.ReadToEndAsync();
                System.Threading.Tasks.Task<string> se = p.StandardError.ReadToEndAsync();
                if (!p.WaitForExit(TimeoutMs)) { try { p.Kill(); } catch { } return $"CANNOT CHECK (Blender timed out after {TimeoutMs / 1000} s)"; }
                stdout = so.Result + se.Result;
            }
            File.WriteAllText(Path.Combine(outDir, "blender-log.txt"), stdout);
            if (!File.Exists(json)) return "CANNOT CHECK (Blender wrote no result — see blender-log.txt)";
            Raw raw = JsonUtility.FromJson<Raw>(File.ReadAllText(json).Replace("\"volatile\":", "\"volatile_\":"));
            double blenderSeconds = sw.Elapsed.TotalSeconds;

            Dictionary<string, Group> b = Index(raw.blend_groups), f = Index(raw.fbx_groups), u = UnityColumn();
            var names = new SortedSet<string>(b.Keys); names.UnionWith(f.Keys); names.UnionWith(u.Keys);
            var differs = new List<string>(); var explained = new List<string>(); var unityDiffers = new List<string>();
            var md = new StringBuilder();
            foreach (string n in names)
            {
                b.TryGetValue(n, out Group gb); f.TryGetValue(n, out Group gf); u.TryGetValue(n, out Group gu);
                string why = Compare(gb, gf, out bool hard);
                if (why != null)
                {
                    if (hard || gb == null || gb.volatile_ == 0) { differs.Add(n); md.AppendLine($"| **{n}** | .blend vs FBX | {why} |"); }
                    else { explained.Add(n); md.AppendLine($"| {n} | .blend vs FBX | EXPLAINED (volatile: {gb.volatile_} animated/modified object(s)) — {why} |"); }
                }
                string whyU = Compare(gf, gu, out _);
                if (whyU != null) { unityDiffers.Add(n); md.AppendLine($"| {n} | FBX vs Unity | {whyU} |"); }
            }
            string verdict = differs.Count == 0 ? "MATCH" : $"DIFFERS ({differs.Count} names: {string.Join(", ", differs)})";
            long tb = 0, tf = 0, tu = 0; int cb = 0, cf = 0, cu = 0;
            foreach (Group g in b.Values) { tb += g.triangles; cb += g.count; }
            foreach (Group g in f.Values) { tf += g.triangles; cf += g.count; }
            foreach (Group g in u.Values) { tu += g.triangles; cu += g.count; }
            var report = new StringBuilder();
            report.AppendLine($"# Map check — {verdict}\n");
            report.AppendLine($"- .blend: `{raw.blend}`\n- FBX: `{FbxPath}`\n- run {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}, Blender {blenderSeconds:0.0} s\n");
            report.AppendLine("| column | groups | meshes | triangles | cameras | lights |\n|---|---|---|---|---|---|");
            report.AppendLine($"| .blend (evaluated) | {b.Count} | {cb} | {tb} | {Join(raw.blend_cameras)} | {Join(raw.blend_lights)} |");
            report.AppendLine($"| FBX (imported in Blender) | {f.Count} | {cf} | {tf} | {Join(raw.fbx_cameras)} | {Join(raw.fbx_lights)} |");
            report.AppendLine($"| Unity (the imported model) | {u.Count} | {cu} | {tu} | – | – |\n");
            report.AppendLine($"Rules: count and triangles equal; bounds within {ToleranceMetres} m (exact vertex bounds; instances by origin). " +
                              "Unity's bounds are converted to Blender's axes with (bx, by, bz) = (−ux, −uz, uy).\n");
            report.AppendLine($"**Differ:** {differs.Count} · **Explained (volatile):** {explained.Count} · **FBX vs Unity:** {unityDiffers.Count}\n");
            if (md.Length > 0) report.AppendLine("| group | pair | difference |\n|---|---|---|\n" + md);
            File.WriteAllText(Path.Combine(outDir, "report.md"), report.ToString());
            var rj = new StringBuilder("{\n");
            rj.Append($" \"verdict\": \"{verdict}\",\n \"differs\": [{Quote(differs)}],\n \"explained\": [{Quote(explained)}],\n \"fbx_vs_unity\": [{Quote(unityDiffers)}],\n");
            rj.Append($" \"totals\": {{\"blend\": [{b.Count}, {cb}, {tb}], \"fbx\": [{f.Count}, {cf}, {tf}], \"unity\": [{u.Count}, {cu}, {tu}]}},\n \"blender_seconds\": {blenderSeconds.ToString("0.0", Inv)}\n}}\n");
            File.WriteAllText(Path.Combine(outDir, "report.json"), rj.ToString());
            return verdict;
        }

        private static string Join(string[] a) => a == null || a.Length == 0 ? "none" : string.Join(", ", a);
        private static string Quote(List<string> l) { var sb = new StringBuilder(); for (int i = 0; i < l.Count; i++) sb.Append(i > 0 ? ", " : "").Append('"').Append(l[i].Replace("\"", "'")).Append('"'); return sb.ToString(); }

        private static Dictionary<string, Group> Index(Group[] list)
        {
            var d = new Dictionary<string, Group>();
            if (list != null) foreach (Group g in list) d[g.name] = g;
            return d;
        }

        /// <summary>Null when equal within the rules; otherwise what differs. hard = a count or triangle difference.</summary>
        public static string Compare(Group a, Group c, out bool hard)
        {
            hard = false;
            if (a == null && c == null) return null;
            if (a == null || c == null) { hard = true; return a == null ? "missing on the left" : "missing on the right"; }
            var sb = new StringBuilder();
            if (a.count != c.count) { hard = true; sb.Append($"count {a.count} vs {c.count}; "); }
            if (a.triangles != c.triangles) { hard = true; sb.Append($"triangles {a.triangles} vs {c.triangles}; "); }
            float db = Delta(a.min, a.max, c.min, c.max), dorg = Delta(a.origin_min, a.origin_max, c.origin_min, c.origin_max);
            if (db > ToleranceMetres) sb.Append($"bounds differ by {db.ToString("0.###", Inv)} m; ");
            if (dorg > ToleranceMetres) sb.Append($"instance origins differ by {dorg.ToString("0.###", Inv)} m; ");
            return sb.Length == 0 ? null : sb.ToString().TrimEnd(' ', ';');
        }

        private static bool Empty(float[] v) => v == null || v.Length < 3 || v[0] > 1e17f;

        private static float Delta(float[] amin, float[] amax, float[] cmin, float[] cmax)
        {
            if (Empty(amin) && Empty(cmin)) return 0f;
            if (Empty(amin) || Empty(cmin)) return float.PositiveInfinity;
            float d = 0f;
            for (int k = 0; k < 3; k++) d = Mathf.Max(d, Mathf.Max(Mathf.Abs(amin[k] - cmin[k]), Mathf.Abs(amax[k] - cmax[k])));
            return d;
        }

        /// <summary>What Unity imported: every MeshFilter under the model asset, grouped the same way, in Blender axes.</summary>
        public static Dictionary<string, Group> UnityColumn()
        {
            var d = new Dictionary<string, Group>();
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (root == null) return d;
            foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
            {
                if (mf.sharedMesh == null) continue;
                string name = mf.name;
                string[] parts = name.Split('|');
                bool inst = parts.Length >= 3;
                string key = parts.Length >= 2 && parts[1].Length > 0 ? parts[1].Split('.')[0] : name.Split('.')[0];
                if (!d.TryGetValue(key, out Group g)) { g = new Group { name = key, min = Init(1e18f), max = Init(-1e18f), origin_min = Init(1e18f), origin_max = Init(-1e18f) }; d[key] = g; }
                g.count++;
                Mesh m = mf.sharedMesh;
                for (int s = 0; s < m.subMeshCount; s++) g.triangles += m.GetIndexCount(s) / 3;
                Matrix4x4 l2w = mf.transform.localToWorldMatrix;   // the asset's root sits at the origin with its import rotation, as the scene instance does
                if (inst)
                {
                    g.instances++;
                    Grow(g.origin_min, g.origin_max, ToBlender(l2w.MultiplyPoint3x4(Vector3.zero)));
                    continue;
                }
                foreach (Vector3 v in m.vertices) Grow(g.min, g.max, ToBlender(l2w.MultiplyPoint3x4(v)));
            }
            return d;
        }

        private static float[] Init(float v) => new[] { v, v, v };
        private static Vector3 ToBlender(Vector3 u) => new Vector3(-u.x, -u.z, u.y);
        private static void Grow(float[] mn, float[] mx, Vector3 p)
        {
            for (int k = 0; k < 3; k++) { mn[k] = Mathf.Min(mn[k], p[k]); mx[k] = Mathf.Max(mx[k], p[k]); }
        }
    }
}
