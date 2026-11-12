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
    /// Story 2.2 (AC2/AC3/AC4, CLOSE attempt 3): seal the mountain foot.
    ///
    /// The terrain is a patchwork of single-sided tiles (the plain's <c>Plane*</c> pieces and the mountains' <c>Landscape*</c>
    /// tiles) that overlap at different heights. Where a tile's edge (its lip) hovers above the tile or plain beneath it, the
    /// ground continues underneath, and a 1.77 m body walks in: CLOSE attempt 2 measured 45,716 m² of the pub's NavMesh island
    /// under a surface, reproduced it with a CharacterController capsule (8 m under the mountain at (174, −296)), and found two
    /// channels that reach the ring barrier under the mountain (south at x ≈ −330, east at z −250…−330). From under a tile its
    /// underside is a culled back-face, so the view passes through the mountain too.
    ///
    /// <c>Tools/Town/Seal Mountain Foot</c> (re-runnable, idempotent; the mesh asset keeps its GUID):
    ///   • finds every boundary edge of every terrain tile (welded by position) inside the playable area (+ a small margin);
    ///   • measures, every metre along it, the floor straight beneath the lip (the first map surface below: never scatter,
    ///     never the edge boulders or the barriers). Where the lip hovers <see cref="MinGap"/> or more above that floor (and the
    ///     floor is not water: the shore is the barrier's job), it hangs a vertical SKIRT from the lip down to
    ///     <see cref="Bury"/> under the floor;
    ///   • the skirt is double-sided, rendered with the lip tile's own material, collidable (one MeshCollider), static, on the
    ///     Default layer, under the scene root <c>TownSeal</c>. Its mesh lives in <see cref="MeshPath"/> (overwritten in place).
    ///   Chosen over a line of edge boulders by measurement and by photograph (story file, CLOSE attempt 3): most of the
    ///   hovering lip stands higher than any boulder's 2.4–3.2 m top, so boulders would leave the see-through gap above them.
    ///
    /// <c>Tools/Town/Measure Under Mountain (NavMesh)</c>: the baked NavMesh's pub island, each triangle classified by what
    /// covers it (a Landscape tile, the plain, anything else such as a roof) more than <see cref="CoverMin"/> above it; the
    /// pockets under a Landscape tile (connected groups), the entrances (edges between an open and a covered triangle) and a
    /// probe list for <c>ProbeEntrances</c>. Re-bake first, or it measures the old bake.
    ///
    /// <c>ProbeEntrances</c>: the controller's own body (the player's CharacterController settings, on a temporary object,
    /// edit mode) driven by <c>Move</c> from the open side of every entrance toward the covered side. Reports where it stops.
    /// Names never contain "Edge": the edge-walk rig classifies a stop on a collider named "Edge …" as a TownBounds barrier.
    /// Marks the scene dirty and never saves it (save in a separate call).
    /// </summary>
    public static class TownSealBuilder
    {
        public const string RootName = "TownSeal";
        public const string MeshPath = "Assets/Data/World/TownSeal.asset";
        public const float MinGap = 0.3f;        // a lip this far above its floor gets a skirt (any step the body can't take)
        public const float MaxGap = 80f;         // deeper than this: no floor in reach (the void under the map's outer tiles)
        public const float Bury = 0.6f;          // the skirt runs this far under the floor
        public const float Margin = 3f;          // lips this close outside the playable area are sealed too
        public const float PieceLength = 1f;     // a lip edge is cut into pieces no longer than this
        public const float CoverMin = 1f;        // NavMesh this far under a surface counts as covered
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
        private static string F(float v, string f = "0.##") => v.ToString(f, Inv);

        [MenuItem("Tools/Town/Seal Mountain Foot")]
        public static void BuildMenu() => Build("seal/build");

        [MenuItem("Tools/Town/Measure Under Mountain (NavMesh)")]
        public static void MeasureMenu() => Measure("seal/measure");

        /// <summary>The terrain tiles: every Landscape tile, and the plain's big pieces (material Material.001, ≥ 100 m).</summary>
        public static List<MeshCollider> Terrain(GameObject map)
        {
            var list = new List<MeshCollider>();
            foreach (MeshCollider c in map.GetComponentsInChildren<MeshCollider>(true))
            {
                if (c.sharedMesh == null || c.name.Contains("|")) continue;
                if (c.name.StartsWith("Landscape")) { list.Add(c); continue; }
                var r = c.GetComponent<MeshRenderer>();
                Bounds b = c.bounds;
                if (r != null && r.sharedMaterial != null && r.sharedMaterial.name == "Material.001" && Mathf.Max(b.size.x, b.size.z) >= 100f) list.Add(c);
            }
            return list;
        }

        public static bool IsTerrain(Collider c) => c != null && (c.name.StartsWith("Landscape") || IsPlain(c));

        private static bool IsPlain(Collider c)
        {
            if (c == null || c.name.StartsWith("Landscape") || c.name.Contains("|")) return false;
            var r = c.GetComponent<MeshRenderer>();
            Bounds b = c.bounds;
            return r != null && r.sharedMaterial != null && r.sharedMaterial.name == "Material.001" && Mathf.Max(b.size.x, b.size.z) >= 100f;
        }

        private struct Piece { public Vector3 a, b; public float fa, fb; public int mat; public string tile; }

        public static string Build(string reportSub)
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[TownSeal] Build in edit mode."); return "playing"; }
            var cfg = AssetDatabase.LoadAssetAtPath<TownBoundsConfig>(AddMeshColliders.TownBoundsConfigPath);
            GameObject map = GameObject.Find("game map");
            if (cfg == null || map == null) { Debug.LogWarning("[TownSeal] Needs TownBoundsConfig and 'game map'."); return "missing"; }
            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            Physics.SyncTransforms();
            Dictionary<Collider, int> kinds = TownCensus.Kinds(map);
            Vector2[] poly = cfg.SafePlayableArea;
            Vector4 pb = cfg.PlayableBounds2D();
            var region = new Bounds(new Vector3((pb.x + pb.z) * 0.5f, 0f, (pb.y + pb.w) * 0.5f), new Vector3(pb.z - pb.x + 2f * Margin, 10000f, pb.w - pb.y + 2f * Margin));
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var log = new StringBuilder($"Town seal built {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}: min gap {MinGap} m, bury {Bury} m, margin {Margin} m, pieces ≤ {PieceLength} m\n");
            var csv = new StringBuilder("tile,x0,z0,x1,z1,lip_y,floor_y,gap_m,floor_collider\n");
            var mats = new List<Material>();
            var pieces = new List<Piece>();
            float lipM = 0f, waterM = 0f, voidM = 0f, flushM = 0f, seamM = 0f, noGoM = 0f;
            var hist = new float[5]; string[] histName = { "0.3-1.77", "1.77-3.2", "3.2-10", "10-30", ">30" };
            foreach (MeshCollider tile in Terrain(map))
            {
                Bounds tb = tile.bounds;
                if (!tb.Intersects(region)) continue;
                Mesh m = tile.sharedMesh;
                Vector3[] v = m.vertices; int[] tri = m.triangles;
                Matrix4x4 w = tile.transform.localToWorldMatrix;
                var weld = new Dictionary<Vector3Int, int>();
                var pos = new List<Vector3>();
                var id = new int[v.Length];
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3 p = w.MultiplyPoint3x4(v[i]);
                    var key = new Vector3Int(Mathf.RoundToInt(p.x * 100f), Mathf.RoundToInt(p.y * 100f), Mathf.RoundToInt(p.z * 100f));
                    if (!weld.TryGetValue(key, out int k)) { k = pos.Count; weld[key] = k; pos.Add(p); }
                    id[i] = k;
                }
                var count = new Dictionary<long, int>();
                var third = new Dictionary<long, int>();   // the owning triangle's third vertex: which side is the tile
                for (int t = 0; t < tri.Length; t += 3)
                    for (int e = 0; e < 3; e++)
                    {
                        int a = id[tri[t + e]], b = id[tri[t + (e + 1) % 3]];
                        if (a == b) continue;
                        long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                        count.TryGetValue(key, out int c); count[key] = c + 1;
                        third[key] = id[tri[t + (e + 2) % 3]];
                    }
                Material mat = tile.GetComponent<MeshRenderer>() != null ? tile.GetComponent<MeshRenderer>().sharedMaterial : null;
                int mi = mats.IndexOf(mat);
                if (mi < 0) { mi = mats.Count; mats.Add(mat); }
                float tileM = 0f;
                foreach (var kv in count)
                {
                    if (kv.Value != 1) continue;
                    Vector3 A = pos[(int)(kv.Key >> 32)], B = pos[(int)(kv.Key & 0xffffffffL)];
                    Vector2 mid = new Vector2((A.x + B.x) * 0.5f, (A.z + B.z) * 0.5f);
                    if (!Near(poly, mid, Margin) && !Near(poly, new Vector2(A.x, A.z), Margin) && !Near(poly, new Vector2(B.x, B.z), Margin)) continue;
                    if (InNoGo(cfg, mid)) { noGoM += Vector2.Distance(new Vector2(A.x, A.z), new Vector2(B.x, B.z)); continue; }
                    // outward: horizontal, perpendicular to the edge, away from the tile's own triangle
                    Vector3 eh = B - A; eh.y = 0f;
                    Vector3 outward = eh.sqrMagnitude > 1e-8f ? Vector3.Cross(Vector3.up, eh.normalized) : Vector3.forward;
                    Vector3 toTri = pos[third[kv.Key]] - A; toTri.y = 0f;
                    if (Vector3.Dot(outward, toTri) > 0f) outward = -outward;
                    float len = Vector3.Distance(A, B);
                    int n = Mathf.Max(1, Mathf.CeilToInt(len / PieceLength));
                    Vector3 prev = A;
                    float fPrev = Floor(kinds, tile, A, out string cPrev, out bool wPrev);
                    for (int s = 1; s <= n; s++)
                    {
                        Vector3 cur = Vector3.Lerp(A, B, (float)s / n);
                        float fCur = Floor(kinds, tile, cur, out string cCur, out bool wCur);
                        float segM = Vector3.Distance(new Vector3(prev.x, 0, prev.z), new Vector3(cur.x, 0, cur.z));
                        float gPrev = float.IsNaN(fPrev) ? float.NaN : prev.y - fPrev, gCur = float.IsNaN(fCur) ? float.NaN : cur.y - fCur;
                        if (Seam(kinds, prev, outward) && Seam(kinds, cur, outward)) seamM += segM;
                        else if (wPrev || wCur) waterM += segM;
                        else if (float.IsNaN(gPrev) && float.IsNaN(gCur)) voidM += segM;
                        else
                        {
                            float g = Mathf.Max(float.IsNaN(gPrev) ? 0f : gPrev, float.IsNaN(gCur) ? 0f : gCur);
                            if (g >= MinGap)
                            {
                                float fa = float.IsNaN(fPrev) ? fCur : fPrev, fb = float.IsNaN(fCur) ? fPrev : fCur;
                                pieces.Add(new Piece { a = prev, b = cur, fa = Mathf.Min(fa, prev.y), fb = Mathf.Min(fb, cur.y), mat = mi, tile = tile.name });
                                lipM += segM; tileM += segM;
                                hist[g < 1.77f ? 0 : g < 3.2f ? 1 : g < 10f ? 2 : g < 30f ? 3 : 4] += segM;
                                csv.AppendLine($"{tile.name},{F(prev.x)},{F(prev.z)},{F(cur.x)},{F(cur.z)},{F(Mathf.Max(prev.y, cur.y))},{F(Mathf.Min(fa, fb))},{F(g)},{(float.IsNaN(fCur) ? cPrev : cCur)}");
                            }
                            else flushM += segM;
                        }
                        prev = cur; fPrev = fCur; cPrev = cCur; wPrev = wCur;
                    }
                }
                if (tileM > 0f) log.AppendLine($"  {tile.name}: {F(tileM, "0")} m of hovering lip sealed");
            }

            // one mesh, one submesh per material, double-sided quads
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var uvs = new List<Vector2>();
            var subs = new List<List<int>>();
            for (int i = 0; i < mats.Count; i++) subs.Add(new List<int>());
            foreach (Piece p in pieces)
            {
                Vector3 a0 = p.a, b0 = p.b;
                Vector3 a1 = new Vector3(a0.x, p.fa - Bury, a0.z), b1 = new Vector3(b0.x, p.fb - Bury, b0.z);
                Vector3 along = b0 - a0; along.y = 0f;
                Vector3 nrm = along.sqrMagnitude > 1e-8f ? Vector3.Cross(Vector3.up, along.normalized) : Vector3.forward;
                for (int side = 0; side < 2; side++)
                {
                    int bi = verts.Count;
                    Vector3 n = side == 0 ? nrm : -nrm;
                    verts.Add(a0); verts.Add(b0); verts.Add(b1); verts.Add(a1);
                    for (int q = 0; q < 4; q++) { norms.Add(n); uvs.Add(Vector2.zero); }
                    List<int> list = subs[p.mat];
                    // winding so the face's front looks along n
                    Vector3 fn = Vector3.Cross(b0 - a0, a1 - a0);
                    bool flip = Vector3.Dot(fn, n) < 0f;
                    if (!flip) { list.Add(bi); list.Add(bi + 1); list.Add(bi + 2); list.Add(bi); list.Add(bi + 2); list.Add(bi + 3); }
                    else { list.Add(bi); list.Add(bi + 2); list.Add(bi + 1); list.Add(bi); list.Add(bi + 3); list.Add(bi + 2); }
                }
            }
            // drop materials with no pieces
            var usedMats = new List<Material>(); var usedSubs = new List<List<int>>();
            for (int i = 0; i < mats.Count; i++) if (subs[i].Count > 0) { usedMats.Add(mats[i]); usedSubs.Add(subs[i]); }

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
            bool created = mesh == null;
            if (created) mesh = new Mesh();
            mesh.Clear();
            mesh.name = "TownSeal";
            mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetUVs(0, uvs);
            mesh.subMeshCount = usedSubs.Count;
            for (int i = 0; i < usedSubs.Count; i++) mesh.SetTriangles(usedSubs[i], i);
            mesh.RecalculateBounds();
            if (created)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(MeshPath));
                AssetDatabase.CreateAsset(mesh, MeshPath);
            }
            else EditorUtility.SetDirty(mesh);
            AssetDatabase.SaveAssets();

            var root = new GameObject(RootName);
            root.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = root.AddComponent<MeshRenderer>();
            mr.sharedMaterials = usedMats.ToArray();
            var mc = root.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;
            mc.convex = false;
            root.layer = 0;
            GameObjectUtility.SetStaticEditorFlags(root, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                                                         StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            EditorSceneManager.MarkSceneDirty(root.scene);
            Physics.SyncTransforms();

            log.AppendLine($"hovering lip sealed: {F(lipM, "0")} m in {pieces.Count} pieces, {verts.Count} vertices, {usedMats.Count} materials; " +
                           $"lip flush with its floor (< {MinGap} m, nothing to seal) {F(flushM, "0")} m; a seam (another surface continues at the lip's height just outside it) {F(seamM, "0")} m; in a no-go area (the pond) {F(noGoM, "0")} m; over water (the shore barrier's job) {F(waterM, "0")} m; no floor within {MaxGap} m {F(voidM, "0")} m");
            var hs = new StringBuilder("gap under the sealed lip (m of lip): ");
            for (int i = 0; i < hist.Length; i++) hs.Append($"{histName[i]} m: {F(hist[i], "0")}; ");
            log.AppendLine(hs.ToString());
            log.AppendLine($"higher than a 3.2 m edge boulder's top: {F(hist[2] + hist[3] + hist[4], "0")} m of {F(lipM, "0")} m");
            log.AppendLine($"mesh {MeshPath} ({(created ? "created" : "overwritten in place, same GUID")} {AssetDatabase.AssetPathToGUID(MeshPath)}); scene root '{RootName}'. Scene NOT saved. {sw.Elapsed.TotalSeconds:0.0} s");
            string dir = TownCensus.Root + "/" + reportSub;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "build.txt"), log.ToString());
            File.WriteAllText(Path.Combine(dir, "pieces.csv"), csv.ToString());
            Debug.Log("[TownSeal] " + log.ToString().Replace("\n", " | "));
            return log.ToString();
        }

        /// <summary>The floor straight beneath a lip point: the first map surface below it (never scatter, never non-map
        /// colliders). NaN when there is none within <see cref="MaxGap"/>. Water below sets <paramref name="water"/>.</summary>
        private static float Floor(Dictionary<Collider, int> kinds, Collider self, Vector3 p, out string what, out bool water)
        {
            what = ""; water = false;
            float best = float.NaN;
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(p.x, p.y - 0.05f, p.z), Vector3.down, MaxGap, ~0, QueryTriggerInteraction.Ignore))
            {
                if (!kinds.TryGetValue(h.collider, out int k) || k == 2) continue;
                if (float.IsNaN(best) || h.point.y > best)
                {
                    best = h.point.y; what = h.collider.name; water = k == 1;
                }
            }
            return best;
        }

        /// <summary>True when another map surface (not scatter) continues just outside the lip, within half a metre of its
        /// height: the edge is a seam between two pieces (the plain's pieces abut with a crack, so the floor under the crack is
        /// the deep terrain far below), not a lip anyone can walk under.</summary>
        private static bool Seam(Dictionary<Collider, int> kinds, Vector3 p, Vector3 outward)
        {
            Vector3 q = p + outward * 0.25f;
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(q.x, p.y + 0.5f, q.z), Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore))
                if (kinds.TryGetValue(h.collider, out int k) && k == 0) return true;
            return false;
        }

        private static bool InNoGo(TownBoundsConfig cfg, Vector2 p)
        {
            if (cfg.noGoAreas == null) return false;
            foreach (TownBoundsConfig.Area a in cfg.noGoAreas)
                if (a.points != null && a.points.Length >= 3 && Near(a.points, p, Margin)) return true;
            return false;
        }

        private static bool Near(Vector2[] poly, Vector2 p, float margin)
        {
            if (TownBoundsConfig.Contains(poly, p)) return true;
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Length], ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
                if ((a + ab * t - p).sqrMagnitude <= margin * margin) return true;
            }
            return false;
        }

        // ------------------------------------------------------------------------------------------------------------
        // The pub island under the mountains, measured on the baked NavMesh
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>Classifies every triangle of the pub's NavMesh island by what covers it more than <see cref="CoverMin"/>
        /// above (census rule: the top surface that is not scatter or water): a Landscape tile ("mountain"), the plain, or
        /// anything else (a roof, a canopy). Writes measure.txt, under_tris.csv (covered by a Landscape tile), pockets.csv
        /// (connected groups of those, with their entrance length) and entrance_probes.csv.</summary>
        public static string Measure(string outSub)
        {
            GameObject map = GameObject.Find("game map");
            if (map == null) return "missing";
            Physics.SyncTransforms();
            Dictionary<Collider, int> kinds = TownCensus.Kinds(map);
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            Vector3[] v = tri.vertices; int[] idx = tri.indices;
            int[] vid = Weld(v, out int nWeld);
            var parent = new int[nWeld];
            for (int i = 0; i < nWeld; i++) parent[i] = i;
            System.Func<int, int> find = null;
            find = x => { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; };
            for (int t = 0; t < idx.Length; t += 3)
            {
                int a = find(vid[idx[t]]), b = find(vid[idx[t + 1]]);
                parent[b] = a;
                int c = find(vid[idx[t + 2]]);
                parent[c] = find(a);
            }
            Vector3 pub = new Vector3(31f, 0.455f, 19f);
            GameObject sp = GameObject.Find("EventManager/SpawnPoint");
            if (sp != null) pub = sp.transform.position;
            int pubRoot = -1; float best = float.MaxValue;
            for (int i = 0; i < v.Length; i++)
            {
                float d = (v[i] - pub).sqrMagnitude;
                if (d < best) { best = d; pubRoot = find(vid[i]); }
            }
            int nt = idx.Length / 3;
            var cls = new int[nt];   // -1 not the pub's island, 0 open, 1 under a Landscape tile, 2 under the plain, 3 under anything else
            var area = new float[nt];
            var cover = new string[nt];
            double[] sum = new double[4];
            int sampled = 0;
            var under = new StringBuilder("cx,cy,cz,surf_y,under_m,area,col\n");
            var covered = new StringBuilder("class,cx,cy,cz,surf_y,under_m,area,col\n");   // 1 Landscape, 2 plain, 3 other
            for (int t = 0; t < nt; t++)
            {
                if (find(vid[idx[3 * t]]) != pubRoot) { cls[t] = -1; continue; }
                Vector3 A = v[idx[3 * t]], B = v[idx[3 * t + 1]], C = v[idx[3 * t + 2]];
                Vector3 c = (A + B + C) / 3f;
                area[t] = Vector3.Cross(B - A, C - A).magnitude * 0.5f;
                TownCensus.Column(kinds, c.x, c.z, out float sy, out _, out _, out _, out Collider sc);
                // the polygon mesh is flat across a convex slope, so a big triangle's centroid can sit 1-3 m UNDER the real
                // surface (measured at (-631, 34): centroid 8.0, ground 10.1; up to 3.7 m on the mountain slopes). The detail
                // mesh has the true height: use it when it is found right above or below the centroid (within 4 m).
                if (NavMesh.SamplePosition(c, out NavMeshHit dh, 4f, NavMesh.AllAreas) &&
                    new Vector2(dh.position.x - c.x, dh.position.z - c.z).sqrMagnitude < 0.25f)
                { sampled++; c.y = dh.position.y; }
                float u = float.IsNaN(sy) ? 0f : sy - c.y;
                cls[t] = u <= CoverMin || sc == null ? 0 : sc.name.StartsWith("Landscape") ? 1 : IsPlain(sc) ? 2 : 3;
                cover[t] = sc != null ? sc.name : "";
                sum[cls[t]] += area[t];
                if (cls[t] == 1) under.AppendLine($"{F(c.x, "0.0")},{F(c.y, "0.0")},{F(c.z, "0.0")},{F(sy, "0.0")},{F(u, "0.0")},{F(area[t])},{sc.name}");
                if (cls[t] >= 1) covered.AppendLine($"{cls[t]},{F(c.x, "0.0")},{F(c.y, "0.0")},{F(c.z, "0.0")},{F(sy, "0.0")},{F(u, "0.0")},{F(area[t])},{sc.name}");
            }
            // edges → triangles, entrances, pockets of Landscape-covered triangles
            var edgeTri = new Dictionary<long, int>();
            var adj = new List<int>[nt];
            var ent = new StringBuilder("x0,y0,z0,x1,y1,z1\n");
            var probes = new StringBuilder("ox,oy,oz,ux,uy,uz,cover\n");
            var probeCells = new HashSet<Vector2Int>();
            float entM = 0f; int nProbe = 0;
            var entOf = new float[nt];
            for (int t = 0; t < nt; t++)
            {
                if (cls[t] < 0) continue;
                for (int e = 0; e < 3; e++)
                {
                    int p = vid[idx[3 * t + e]], q = vid[idx[3 * t + (e + 1) % 3]];
                    long k = p < q ? ((long)p << 32) | (uint)q : ((long)q << 32) | (uint)p;
                    if (!edgeTri.TryGetValue(k, out int o)) { edgeTri[k] = t; continue; }
                    if (Cov(cls[o]) && Cov(cls[t]))
                    {
                        (adj[t] ?? (adj[t] = new List<int>())).Add(o);
                        (adj[o] ?? (adj[o] = new List<int>())).Add(t);
                    }
                    bool ut = Cov(cls[t]), uo = Cov(cls[o]);
                    if (ut == uo) continue;
                    Vector3 P = v[idx[3 * t + e]], Q = v[idx[3 * t + (e + 1) % 3]];
                    float len = Vector3.Distance(P, Q);
                    entM += len;
                    entOf[ut ? t : o] += len;
                    ent.AppendLine($"{F(P.x)},{F(P.y)},{F(P.z)},{F(Q.x)},{F(Q.y)},{F(Q.z)}");
                    int open = ut ? o : t, cov = ut ? t : o;
                    Vector3 oc = Centroid(v, idx, open), uc = Centroid(v, idx, cov);
                    var cell = new Vector2Int(Mathf.FloorToInt((P.x + Q.x) * 0.5f / 5f), Mathf.FloorToInt((P.z + Q.z) * 0.5f / 5f));
                    if (probeCells.Add(cell))
                    {
                        nProbe++;
                        probes.AppendLine($"{F(oc.x)},{F(oc.y)},{F(oc.z)},{F(uc.x)},{F(uc.y)},{F(uc.z)},{cover[cov]}");
                    }
                }
            }
            var pockets = new StringBuilder("pocket,triangles,area_m2,cx,cz,x_min,x_max,z_min,z_max,entrance_m,cover\n");
            var seen = new bool[nt];
            int np = 0;
            var stack = new Stack<int>();
            var pk = new List<string>();
            for (int t = 0; t < nt; t++)
            {
                if (!Cov(cls[t]) || seen[t]) continue;
                np++;
                float a = 0f, e = 0f; int n = 0; Vector3 cs = Vector3.zero;
                float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
                var covers = new HashSet<string>();
                stack.Push(t); seen[t] = true;
                while (stack.Count > 0)
                {
                    int s = stack.Pop();
                    n++; a += area[s]; e += entOf[s];
                    Vector3 c = Centroid(v, idx, s);
                    cs += c * area[s];
                    x0 = Mathf.Min(x0, c.x); x1 = Mathf.Max(x1, c.x); z0 = Mathf.Min(z0, c.z); z1 = Mathf.Max(z1, c.z);
                    covers.Add(cover[s]);
                    if (adj[s] != null) foreach (int o in adj[s]) if (!seen[o]) { seen[o] = true; stack.Push(o); }
                }
                cs /= Mathf.Max(1e-6f, a);
                pk.Add($"{np},{n},{F(a, "0.0")},{F(cs.x, "0.0")},{F(cs.z, "0.0")},{F(x0, "0")},{F(x1, "0")},{F(z0, "0")},{F(z1, "0")},{F(e, "0.0")},{string.Join(" ", covers)}");
            }
            pk.Sort((p, q) => -float.Parse(p.Split(',')[2], Inv).CompareTo(float.Parse(q.Split(',')[2], Inv)));
            foreach (string s in pk) pockets.AppendLine(s);
            var log = new StringBuilder($"Under the terrain (the mountains' Landscape tiles and the plain), on the baked NavMesh {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}: {v.Length} vertices, {nt} triangles; the pub's island: {F((float)(sum[0] + sum[1] + sum[2] + sum[3]), "0")} m²\n");
            log.AppendLine($"  heights: {sampled} of the island's triangles measured at the detail mesh's height under their centroid (the rest at the polygon's)");
            log.AppendLine($"  open sky (nothing more than {CoverMin} m above): {F((float)sum[0], "0")} m²");
            log.AppendLine($"  UNDER THE TERRAIN: {F((float)(sum[1] + sum[2]), "0")} m² in {np} pockets (under a Landscape tile {F((float)sum[1], "0")} m², under the plain {F((float)sum[2], "0")} m²); entrance edges (open | under the terrain) {F(entM, "0")} m; {nProbe} probe cells (5 m)");
            log.AppendLine($"  under anything else (roofs, canopies; expected): {F((float)sum[3], "0")} m²");
            int show = 0;
            foreach (string s in pk) { if (show++ >= 25) break; log.AppendLine("    pocket " + s); }
            string dir = TownCensus.Root + "/" + outSub;
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "measure.txt"), log.ToString());
            File.WriteAllText(Path.Combine(dir, "under_tris_1m.csv"), under.ToString());
            File.WriteAllText(Path.Combine(dir, "covered_tris.csv"), covered.ToString());
            File.WriteAllText(Path.Combine(dir, "entrance_edges.csv"), ent.ToString());
            File.WriteAllText(Path.Combine(dir, "entrance_probes.csv"), probes.ToString());
            File.WriteAllText(Path.Combine(dir, "pockets.csv"), pockets.ToString());
            Debug.Log("[TownSeal] " + log.ToString().Split('\n')[2].Trim());
            return log.ToString();
        }

        private static bool Cov(int c) => c == 1 || c == 2;

        private static Vector3 Centroid(Vector3[] v, int[] idx, int t) => (v[idx[3 * t]] + v[idx[3 * t + 1]] + v[idx[3 * t + 2]]) / 3f;

        /// <summary>Welds the NavMesh's vertices by position (5 cm), as MeasureContact does.</summary>
        private static int[] Weld(Vector3[] v, out int count)
        {
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
            count = weld.Count;
            return vid;
        }

        // ------------------------------------------------------------------------------------------------------------
        // The controller's own body at every entrance
        // ------------------------------------------------------------------------------------------------------------

        /// <summary>For every probe in <paramref name="probesCsv"/> (from a Measure run BEFORE the seal): a temporary
        /// CharacterController with the player's settings stands on the open side and is driven by Move toward the covered
        /// side and 6 m past it, with gravity, in 0.1 m steps. It ENTERED when it ends with a terrain tile (Landscape or the plain) more than
        /// <see cref="CoverMin"/> above its feet. Restores nothing it did not create; the temporary object is destroyed.</summary>
        public static string ProbeEntrances(string probesCsv, string outSub)
        {
            GameObject map = GameObject.Find("game map");
            GameObject player = GameObject.Find("main characters");
            if (map == null || !File.Exists(probesCsv)) return "missing";
            Physics.SyncTransforms();
            Dictionary<Collider, int> kinds = TownCensus.Kinds(map);
            CharacterController pc = player != null ? player.GetComponent<CharacterController>() : null;
            var go = new GameObject("TownSeal probe (temp)") { hideFlags = HideFlags.HideAndDontSave };
            var log = new StringBuilder("ox,oz,ux,uz,start_ok,end_x,end_y,end_z,cover_above_m,cover,result,last_hit\n");
            int entered = 0, stopped = 0, skipped = 0, total = 0;
            try
            {
                var cc = go.AddComponent<CharacterController>();
                cc.radius = pc != null ? pc.radius : 0.3f;
                cc.height = pc != null ? pc.height : 1.77f;
                cc.center = pc != null ? pc.center : new Vector3(0f, 0.885f, 0f);
                cc.stepOffset = 0.35f;
                cc.slopeLimit = pc != null ? pc.slopeLimit : 45f;
                cc.skinWidth = pc != null ? pc.skinWidth : 0.08f;
                cc.minMoveDistance = 0f;
                string[] lines = File.ReadAllLines(probesCsv);
                for (int li = 1; li < lines.Length; li++)
                {
                    string[] f = lines[li].Split(',');
                    if (f.Length < 6) continue;
                    total++;
                    Vector3 o = new Vector3(float.Parse(f[0], Inv), float.Parse(f[1], Inv), float.Parse(f[2], Inv));
                    Vector3 u = new Vector3(float.Parse(f[3], Inv), float.Parse(f[4], Inv), float.Parse(f[5], Inv));
                    Vector3 dir = u - o; dir.y = 0f;
                    float dist = dir.magnitude + 6f;
                    if (dir.sqrMagnitude < 1e-4f) { skipped++; continue; }
                    dir.Normalize();
                    // stand 1 m back on the open side, on its NavMesh
                    Vector3 start = o - dir * 1f;
                    bool ok = NavMesh.SamplePosition(start, out NavMeshHit nh, 1.5f, NavMesh.AllAreas);
                    if (ok) start = nh.position;
                    cc.enabled = false;
                    go.transform.position = start + Vector3.up * 0.05f;
                    cc.enabled = true;
                    Physics.SyncTransforms();
                    string lastHit = "";
                    int steps = Mathf.CeilToInt((dist + 1f) / 0.1f);
                    for (int s = 0; s < steps; s++)
                    {
                        CollisionFlags fl = cc.Move(dir * 0.1f + Vector3.down * 0.15f);
                        if ((fl & CollisionFlags.Sides) != 0)
                        {
                            if (Physics.CapsuleCast(go.transform.position + Vector3.up * 0.35f, go.transform.position + Vector3.up * 1.47f, 0.28f, dir, out RaycastHit hh, 0.3f, ~0, QueryTriggerInteraction.Ignore))
                                lastHit = hh.collider.name;
                        }
                    }
                    Vector3 end = go.transform.position;
                    TownCensus.Column(kinds, end.x, end.z, out float sy, out _, out _, out _, out Collider sc);
                    float above = float.IsNaN(sy) ? 0f : sy - end.y;
                    bool inside = above > CoverMin && IsTerrain(sc);
                    if (inside) entered++; else stopped++;
                    log.AppendLine($"{F(o.x)},{F(o.z)},{F(u.x)},{F(u.z)},{(ok ? 1 : 0)},{F(end.x)},{F(end.y)},{F(end.z)},{F(above)},{(sc != null ? sc.name : "")},{(inside ? "ENTERED" : "stopped")},{lastHit}");
                }
            }
            finally { Object.DestroyImmediate(go); }
            string dir2 = TownCensus.Root + "/" + outSub;
            Directory.CreateDirectory(dir2);
            File.WriteAllText(Path.Combine(dir2, "probes.csv"), log.ToString());
            string summary = $"capsule probes ({probesCsv}): {total}; ENTERED under the terrain {entered}; stopped outside {stopped}; skipped {skipped}";
            File.WriteAllText(Path.Combine(dir2, "probes.txt"), summary + "\n");
            Debug.Log("[TownSeal] " + summary);
            return summary;
        }
    }
}
