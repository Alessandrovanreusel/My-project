#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using CameraGame.Events;
using CameraGame.Player;

namespace CameraGame.World
{
    /// <summary>
    /// Story 2.2 FIX phase, cross-review finding 2: AC4's wall evidence rebuilt with an instrument that works on every wall
    /// (rig code, not shipped). Mode "xreview", staging "walls".
    ///
    /// The old marker sat BEHIND the wall's far face and was sized to its collider, so it could not be placed on a narrow
    /// wall or in a hillside, and it showed round a trunk slimmer than its capsule or over a rock lower than the eye. This one
    /// asks the question directly — is the near plane past a visible surface? — per pose:
    ///   1. Visible surfaces near the eye: every collider within 1.2 m of the eye gives its renderer; a temporary MeshCollider
    ///      of that renderer's OWN mesh (the "probe", enabled only for the instant of a ray) is cast at from the body axis at
    ///      eye height in 33 directions. A hit counts only where a real collider lies within 0.15 m of it (a wall the player
    ///      cannot pass; a pine's foliage has none, by the collider rule, so it is not a wall).
    ///   2. Behind each such surface, 2 cm inside it and parallel to it, a double-sided unlit green marker (1 x 1 m).
    ///   3. A = the real camera; B = the same pose with near clip 0.005. Through = green in A and not in B: what the real near
    ///      clip reveals and a camera with (almost) no near clip would not. Leaks round an edge or over a top show in both and
    ///      cancel. The player's own renderers are off for every render (the body is not a wall).
    ///   4. Controls per pose: the near surfaces' renderers off (the marker must be in view), and the camera moved to 3 cm from
    ///      the nearest surface (its near plane is then past it: A−B must show green). A pose counts only if the body touched
    ///      the wall, the wall hides part of the marker, and both controls pass.
    /// Walls: Story 2.2's 14 (walls/walls.json), plus the road slab's edge and two inside corners between building walls
    /// (found by a scan of the street block in edit mode). The bus shelter exists only in the buried set (census), so it is
    /// not on the list.
    /// </summary>
    public partial class TownRigRunner
    {
        partial void ExtraModes8(ref IEnumerator routine)
        {
            if (staging == "walls") routine = XWalls();
        }

        private const float XInset = 0.02f;
        private readonly Dictionary<Renderer, MeshCollider> _xProbes = new Dictionary<Renderer, MeshCollider>();
        private readonly Collider[] _xNear = new Collider[128];
        private Renderer[] _xPlayerRenderers;

        private MeshCollider XProbe(Renderer r)
        {
            if (_xProbes.TryGetValue(r, out MeshCollider mc)) return mc;
            mc = null;
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var go = new GameObject("VisualProbe (rig)");
                go.transform.SetParent(r.transform, false);
                mc = go.AddComponent<MeshCollider>();
                mc.sharedMesh = mf.sharedMesh;
                mc.enabled = false;
            }
            _xProbes[r] = mc;
            return mc;
        }

        private struct XSurface { public Vector3 p, n; public Renderer r; }

        /// <summary>Visible surfaces round the eye that a real collider backs (see the class summary, step 1).</summary>
        private List<XSurface> XSurfaces(Vector3 eye, List<Renderer> nearRenderers)
        {
            nearRenderers.Clear();
            int n = Physics.OverlapSphereNonAlloc(eye, 1.2f, _xNear, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
            {
                Collider c = _xNear[i];
                if (IsMine(c) || c.name.StartsWith("VisualProbe")) continue;
                Renderer r = c.GetComponent<Renderer>();
                if (r == null) r = c.GetComponentInParent<Renderer>();
                if (r != null && r.enabled && !nearRenderers.Contains(r)) nearRenderers.Add(r);
            }
            var probes = new List<MeshCollider>();
            foreach (Renderer r in nearRenderers) { MeshCollider mc = XProbe(r); if (mc != null) { mc.enabled = true; probes.Add(mc); } }
            Physics.SyncTransforms();
            var dirs = new List<Vector3>();
            for (int k = 0; k < 16; k++) dirs.Add(Quaternion.Euler(0f, k * 22.5f, 0f) * Vector3.forward);
            for (int k = 0; k < 8; k++) dirs.Add(Quaternion.Euler(-30f, k * 45f, 0f) * Vector3.forward);
            for (int k = 0; k < 8; k++) dirs.Add(Quaternion.Euler(-60f, k * 45f, 0f) * Vector3.forward);
            dirs.Add(Vector3.up);
            for (int k = 0; k < 8; k++) dirs.Add(Quaternion.Euler(30f, k * 45f, 0f) * Vector3.forward);    // down: a ledge, a low top
            for (int k = 0; k < 8; k++) dirs.Add(Quaternion.Euler(60f, k * 45f, 0f) * Vector3.forward);
            var hits = new List<XSurface>();
            var renderOf = new Dictionary<MeshCollider, Renderer>();
            foreach (Renderer r in nearRenderers) if (_xProbes.TryGetValue(r, out MeshCollider mc) && mc != null) renderOf[mc] = r;
            bool prevBack = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = false;
            foreach (Vector3 d in dirs)
            {
                float best = 1.2f; XSurface s = default; bool got = false;
                foreach (MeshCollider mc in probes)
                    if (mc.Raycast(new Ray(eye, d), out RaycastHit h, best)) { best = h.distance; s = new XSurface { p = h.point, n = h.normal, r = renderOf[mc] }; got = true; }
                if (got) hits.Add(s);
            }
            Physics.queriesHitBackfaces = prevBack;
            foreach (MeshCollider mc in probes) mc.enabled = false;
            Physics.SyncTransforms();
            var kept = new List<XSurface>();
            foreach (XSurface s in hits)
            {
                bool backed = false;
                int m = Physics.OverlapSphereNonAlloc(s.p, 0.15f, _xNear, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < m; i++) if (!IsMine(_xNear[i]) && !_xNear[i].name.StartsWith("VisualProbe")) { backed = true; break; }
                if (!backed) continue;
                bool dup = false;
                foreach (XSurface k in kept) if (Vector3.Distance(k.p, s.p) < 0.25f && Vector3.Dot(k.n, s.n) > 0.9f) { dup = true; break; }
                if (!dup && kept.Count < 14) kept.Add(s);
            }
            return kept;
        }

        private static Color32[] XRender(Camera c, float near)
        {
            float keep = c.nearClipPlane;
            if (near > 0f) c.nearClipPlane = near;
            RenderTexture rt = RenderTexture.GetTemporary(W, H, 24, RenderTextureFormat.ARGB32);
            RenderTexture prevT = c.targetTexture;
            c.targetTexture = rt;
            c.Render();
            c.targetTexture = prevT;
            c.nearClipPlane = keep;
            RenderTexture prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            Color32[] px = tex.GetPixels32();
            Destroy(tex);
            return px;
        }

        private static bool G(Color32 p) => p.g > 200 && p.r < 50 && p.b < 50;

        private Renderer[] _xAllRenderers;

        /// <summary>
        /// Settles what a "through" pixel is (green in A, not in B). For up to 240 of them, a ray from the camera through the
        /// pixel is cast against the RENDER meshes (probes) of every renderer near the camera, as far as the near plane along
        /// that ray. A visible surface in that span is what the real near clip cut away: if a real collider lies within 0.05 m
        /// of it, the near plane cut into a WALL (a see-through); if none does, it cut collider-less geometry (a pine's
        /// foliage, grass), which the body may stand inside by the collider rule. No surface in that span means A and B
        /// differ for another reason (two surfaces almost coplanar: the marker against a tyre) — an instrument artifact.
        /// </summary>
        private void XClassify(Color32[] a, Color32[] b, out int sampled, out int clippedWall, out int clippedOther, out int unexplained, out string wallNames)
        {
            sampled = clippedWall = clippedOther = unexplained = 0; wallNames = "";
            var idx = new List<int>();
            for (int i = 0; i < a.Length; i++) if (G(a[i]) && !G(b[i])) idx.Add(i);
            if (idx.Count == 0) return;
            if (_xAllRenderers == null) _xAllRenderers = FindObjectsByType<Renderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            Vector3 cp = _cam.transform.position;
            var probes = new List<MeshCollider>();
            foreach (Renderer r in _xAllRenderers)
            {
                if (r == null || !r.enabled || r.name.Contains("(rig)") || r.transform.IsChildOf(_player.transform)) continue;
                if (r.bounds.SqrDistance(cp) > 0.25f * 0.25f) continue;
                MeshCollider mc = XProbe(r);
                if (mc != null) { mc.enabled = true; probes.Add(mc); }
            }
            Physics.SyncTransforms();
            float keepAspect = _cam.aspect;
            _cam.aspect = W / (float)H;                      // the renders were 960 x 540, not the game view's aspect
            float near = _cam.nearClipPlane;
            var names = new HashSet<string>();
            int step = Mathf.Max(1, idx.Count / 240);
            bool prevBack = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;            // a surface the near plane cut may face either way
            for (int k = 0; k < idx.Count; k += step)
            {
                int i = idx[k];
                Ray ray = _cam.ViewportPointToRay(new Vector3((i % W + 0.5f) / W, (i / W + 0.5f) / H, 0f));
                ray.origin = cp;
                float span = near / Mathf.Max(1e-4f, Vector3.Dot(ray.direction, _cam.transform.forward));
                float best = span; Vector3 at = Vector3.zero; bool got = false;
                foreach (MeshCollider mc in probes)
                    if (mc.Raycast(ray, out RaycastHit h, best)) { best = h.distance; at = h.point; got = true; }
                sampled++;
                if (!got) { unexplained++; continue; }
                foreach (MeshCollider mc in probes) mc.enabled = false;
                bool backed = false;
                int m = Physics.OverlapSphereNonAlloc(at, 0.05f, _xNear, ~0, QueryTriggerInteraction.Ignore);
                for (int j = 0; j < m; j++)
                    if (!IsMine(_xNear[j]) && !_xNear[j].name.StartsWith("VisualProbe")) { backed = true; if (names.Count < 4) names.Add(_xNear[j].name.Replace(",", " ")); break; }
                foreach (MeshCollider mc in probes) mc.enabled = true;
                if (backed) clippedWall++; else clippedOther++;
            }
            Physics.queriesHitBackfaces = prevBack;
            _cam.aspect = keepAspect;
            _cam.ResetAspect();
            foreach (MeshCollider mc in probes) mc.enabled = false;
            Physics.SyncTransforms();
            wallNames = string.Join(";", names);
        }

        /// <summary>For a pose the geometric check flags: what the lines from the eye hit, and from which side.</summary>
        private string XGeoDetail()
        {
            float eye = _tpc.Config != null ? _tpc.Config.SafeEyeHeight : 1.65f;
            Vector3 axis = _player.transform.position + Vector3.up * eye;
            var pts = new Vector3[9];
            PlayerConfig.NearPlanePoints(_cam.transform.position, _cam.transform.rotation, _cam.fieldOfView, W / (float)H, _cam.nearClipPlane, pts);
            var sb = new StringBuilder();
            bool prevBack = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            for (int p = 0; p < pts.Length; p++)
            {
                Vector3 d = pts[p] - axis;
                int n = Physics.RaycastNonAlloc(axis, d.normalized, _hitBuf, d.magnitude, ~0, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < n; i++)
                {
                    if (IsMine(_hitBuf[i].collider)) continue;
                    sb.Append($"pt{p}:{_hitBuf[i].collider.name.Replace(",", " ")}@{F(_hitBuf[i].distance, "0.000")}{(Vector3.Dot(_hitBuf[i].normal, d) > 0f ? "(BACK face)" : "(front)")} ");
                    break;
                }
            }
            Physics.queriesHitBackfaces = prevBack;
            int inside = Physics.OverlapSphereNonAlloc(axis, 0.01f, _xNear, ~0, QueryTriggerInteraction.Ignore);
            var ins = new StringBuilder();
            for (int i = 0; i < inside; i++) if (!IsMine(_xNear[i])) ins.Append(_xNear[i].name).Append(' ');
            Vector3 fw = Quaternion.Euler(0f, _player.transform.eulerAngles.y, 0f) * Vector3.forward;
            // is the BODY inside something? the controller's own capsule, shrunk by 1 cm, tested against every collider, and the
            // depth of each overlap (ComputePenetration with a stand-in capsule of the same size)
            Vector3 feet = _player.transform.position, c0 = feet + _cc.center;
            float r = _cc.radius - 0.01f, half = Mathf.Max(0f, _cc.height * 0.5f - _cc.radius);
            int k = Physics.OverlapCapsuleNonAlloc(c0 + Vector3.down * half, c0 + Vector3.up * half, r, _xNear, ~0, QueryTriggerInteraction.Ignore);
            var body = new StringBuilder();
            var probe = new GameObject("BodyProbe (rig)");
            var cap = probe.AddComponent<CapsuleCollider>();
            cap.radius = _cc.radius; cap.height = _cc.height; cap.direction = 1; cap.isTrigger = true;
            probe.transform.position = c0;
            for (int i = 0; i < k; i++)
            {
                Collider o = _xNear[i];
                if (IsMine(o) || o == cap || o.name.StartsWith("VisualProbe")) continue;
                bool pen = Physics.ComputePenetration(cap, c0, Quaternion.identity, o, o.transform.position, o.transform.rotation, out Vector3 dir, out float dist);
                body.Append($"{o.name}{(pen ? $" depth {F(dist, "0.000")} m along {V(dir)}" : " (touching)")}; ");
            }
            DestroyImmediate(probe);
            return $"feet {V(feet)} yaw {F(_player.transform.eulerAngles.y)}; nudge {F(Vector3.Dot(_cam.transform.position - axis, fw), "0.000")} m, camera {F(_cam.transform.position.y - feet.y, "0.000")} m over the feet; eye overlaps [{ins}]; body overlaps [{body}]; {sb}";
        }
        private static int GreenCount(Color32[] px) { int n = 0; foreach (Color32 p in px) if (G(p)) n++; return n; }
        private static int GreenDiff(Color32[] a, Color32[] b) { int n = 0; for (int i = 0; i < a.Length; i++) if (G(a[i]) && !G(b[i])) n++; return n; }

        /// <summary>A with the see-through pixels (green in A, not in B) painted magenta, so a capture shows WHERE.</summary>
        private static void XSave(string path, Color32[] a, Color32[] b)
        {
            var o = new Color32[a.Length];
            for (int i = 0; i < a.Length; i++) o[i] = b != null && G(a[i]) && !G(b[i]) ? new Color32(255, 0, 255, 255) : a[i];
            var tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            tex.SetPixels32(o);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToJPG(85));
            Destroy(tex);
        }

        private void XPlayerRenderers(bool on)
        {
            if (_xPlayerRenderers == null) _xPlayerRenderers = _player.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer r in _xPlayerRenderers) if (r != null) r.enabled = on;
        }

        private IEnumerator XWalls()
        {
            _log.Insert(0, "CROSS-REVIEW finding 2 (rig staging): AC4 re-measured with the near-plane instrument (class summary). No warning or error is provoked on purpose.\n");
            Application.logMessageReceived += XCount;
            string json = ReadData("walls/walls.json");
            if (json == null) { _log.AppendLine("RIG BROKEN: no walls/walls.json"); yield break; }
            var walls = new List<WallSpec>(JsonUtility.FromJson<WallList>(json).walls);
            // the slab's collider is its raised TOP (2.10 m, 1.22 m over the grass at x −150; census/road_edges.csv) with no side
            // face, so no horizontal ray finds it: the ledge fallback below walks into its edge
            walls.Add(new WallSpec { name = "road_slab_edge", stand = new[] { -150f, 8.5f }, aim = new[] { -150f, 4.2f, 0.6f } });
            // pressed into a 90° inside corner at ±30/±60 (the contact normals spread 90°: Object014.016 and Object_7.002)
            walls.Add(new WallSpec { name = "inside_corner_building_b", stand = new[] { -233.44f, -27.85f }, aim = new[] { -233.44f, -31.35f, 1.2f } });
            // the overhangs Task 9 walked under (2.07-2.24 m above the feet), walked into toward the boulder's core: a sloping
            // underside can meet the top-front of the capsule (cross-review finding 3)
            walls.Add(new WallSpec { name = "boulder_rock006_overhang", stand = new[] { 75.02f, -337.41f }, aim = new[] { 69.02f, -332.13f, 1.2f } });
            walls.Add(new WallSpec { name = "boulder_rock007_overhang", stand = new[] { 86.71f, -339.04f }, aim = new[] { 82.95f, -332.0f, 1.2f } });
            walls.Add(new WallSpec { name = "boulder_rock023_overhang", stand = new[] { 86.06f, -327.88f }, aim = new[] { 90.46f, -334.6f, 1.2f } });
            walls.Add(new WallSpec { name = "tyre_line_overhang", stand = new[] { -239.8f, 234.0f }, aim = new[] { -240.2f, 226.0f, 1.2f } });
            // CLOSE attempt 4: the walls the close gates added. The TownSeal skirt is ONE double-sided quad with no thickness
            // (the plain's south lip at z −295.78, 0.91 over a ditch floor at −4.26), and the TownEdge rock where ring_b225 stops
            walls.Add(new WallSpec { name = "seal_ditch_north_wall", stand = new[] { 150f, -299.5f }, aim = new[] { 150f, -295.0f, 1.2f } });
            walls.Add(new WallSpec { name = "edge_rock_b225", stand = new[] { -541.5f, -391.8f }, aim = new[] { -547.5f, -398.0f, 1.2f } });
            walls.Add(new WallSpec { name = "edge_rock_b180", stand = new[] { -216.85f, -389.0f }, aim = new[] { -216.85f, -398.0f, 1.2f } });
            // optional subset (SessionState "TownRig.XWallsOnly", comma-separated names): a quick before/after on chosen walls
            string only = UnityEditor.SessionState.GetString("TownRig.XWallsOnly", "");
            if (only.Length > 0)
            {
                var keep = new HashSet<string>(only.Split(','));
                walls.RemoveAll(x => !keep.Contains(x.name));
                _log.AppendLine($"SUBSET RUN: only {only}");
            }
            _log.AppendLine("bus shelter: NOT on the surface. The census lists BusStop_Vetro_0 only in the buried set (y −61…−60, census/census.json), so there is none to press against.");
            if (_manager != null) foreach (EventActor a in _manager.ActiveActors) a.gameObject.SetActive(false);
            if (_manager != null) _manager.enabled = false;
            if (_fall != null) _fall.enabled = false;
            float t0 = 0f;
            while (!_cc.isGrounded && t0 < 8f) { t0 += Time.deltaTime; yield return null; }
            Place(new Vector3(60f, 0f, -2f), 270f);
            yield return Settle(1f);
            yield return DriveControl();
            yield return PitchControl();
            float eye = _tpc.Config != null ? _tpc.Config.SafeEyeHeight : 1.65f;
            // AC4: the fix must leave the open-space camera exactly where it is — read back on the slab, nothing within metres
            foreach (float op in new[] { -80f, 0f, 80f })
            {
                yield return SetPitch(op);
                yield return new WaitForEndOfFrame();
                Vector3 feet = _player.transform.position, camp = _cam.transform.position;
                Vector3 fw = Quaternion.Euler(0f, _player.transform.eulerAngles.y, 0f) * Vector3.forward;
                Vector3 d = camp - feet;
                _log.AppendLine($"OPEN POSE pitch {F(op)} (read {F(_tpc.CameraPitch)}): up {F(d.y, "0.0000")} m, forward {F(Vector3.Dot(new Vector3(d.x, 0f, d.z), fw), "0.0000")} m, " +
                                $"sideways {F(Vector3.Cross(fw, new Vector3(d.x, 0f, d.z)).y, "0.0000")} m, near clip {F(_cam.nearClipPlane, "0.000")}");
            }
            yield return SetPitch(0f);
            Material green = DoubleSidedGreen();
            var csv = new StringBuilder("wall,angle,mode,pitch,fov,contact,corner_spread_deg,surfaces,A_px,B_px,through_px,ctrl_off_px,ctrl_push_through_px,cam_to_surface_m,geo_hits,valid,through,sampled,clipped_wall,clipped_nocollider,unexplained,clipped_wall_colliders\n");
            var perWall = new StringBuilder("wall,collider,poses,contact_poses,valid_poses,through_poses,max_through_px,geo_hit_poses,control_failed_poses,min_cam_to_surface_m,what_stopped_the_body\n");
            string fdir = Path.Combine(outputDir, "frames");
            Directory.CreateDirectory(fdir);
            var nearR = new List<Renderer>();
            int allValid = 0, allThrough = 0, allPoses = 0, allSampled = 0, allWall = 0, allOther = 0, allUnexpl = 0;
            foreach (WallSpec w in walls)
            {
                Vector3 stand = new Vector3(w.stand[0], 0f, w.stand[1]);
                float gy = GroundY(stand);
                float aimH = w.aim.Length > 2 ? w.aim[2] : 1.2f;
                Vector3 aim = new Vector3(w.aim[0], (float.IsNaN(gy) ? 0f : gy) + aimH, w.aim[1]);
                Vector3 from = new Vector3(stand.x, aim.y, stand.z);
                if (!FirstWall(from, aim - from, 30f, out RaycastHit hit))
                {
                    // a LEDGE (a raised top with no side face): the collider straight down at the aim, standing over the stand's ground
                    bool ledge = Physics.Raycast(new Vector3(aim.x, aim.y + 10f, aim.z), Vector3.down, out RaycastHit ledgeTop, 20f, ~0, QueryTriggerInteraction.Ignore)
                                 && !float.IsNaN(gy) && ledgeTop.point.y > gy + 0.3f;
                    if (!ledge) { _log.AppendLine($"{w.name}: NO WALL found from {V(from)} toward {V(aim)}"); continue; }
                    Vector3 toward = aim - from; toward.y = 0f;
                    hit = ledgeTop;
                    hit.normal = -toward.normalized;
                    _log.AppendLine($"{w.name}: no face at body height — a ledge: {ledgeTop.collider.name} top at {F(ledgeTop.point.y - gy, "0.00")} m over the ground; walked into its edge");
                }
                Vector3 n = hit.normal; n.y = 0f;
                if (n.sqrMagnitude < 0.01f) { _log.AppendLine($"{w.name}: hit {hit.collider.name} is not a wall (normal {V(hit.normal)})"); continue; }
                n.Normalize();
                Collider wallCol = hit.collider;
                _log.AppendLine($"{w.name}: wall {wallCol.name} ({wallCol.GetType().Name}) at {V(hit.point)} normal {V(n)}");
                int nPoses = 0, nContact = 0, nValid = 0, nThrough = 0, maxThrough = 0, nGeo = 0, nCtrlFail = 0;
                float minGap = float.PositiveInfinity;
                var stoppers = new Dictionary<string, int>();
                foreach (float ang in Angles)
                {
                    Vector3 heading = Quaternion.Euler(0f, ang, 0f) * -n;
                    Vector3 start = hit.point - heading * 3.5f;
                    start.y = 0f;
                    float yaw = Mathf.Atan2(heading.x, heading.z) * Mathf.Rad2Deg;
                    Place(start, yaw, hit.point.y + 4f);
                    yield return Settle(0.4f);
                    _rec.hits.Clear(); _rec.recording = true;
                    Keys(Key.W);
                    float t = 0f, still = 0f; Vector3 prev = _player.transform.position; bool contact = false;
                    while (t < 6f)
                    {
                        _player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                        yield return null;
                        t += Time.deltaTime;
                        Vector3 q = _player.transform.position;
                        float sp = Hz(q, prev) / Mathf.Max(1e-5f, Time.deltaTime); prev = q;
                        if (!contact) foreach (TownHitRecorder.Hit h in _rec.hits) if (h.c == wallCol) { contact = true; break; }
                        if (contact) { still += Time.deltaTime; if (Mathf.Abs(ang) > 1f ? still > 0.3f : (sp < 0.05f && still > 0.3f)) break; }
                        else if (sp < 0.05f && t > 1f) { still += Time.deltaTime; if (still > 0.6f) break; }   // held by something else
                    }
                    Keys();
                    _rec.recording = false;
                    // what the body touched, and how wide apart the side-on contact normals are (an inside corner holds two)
                    float spread = 0f; var normals = new List<Vector3>();
                    foreach (TownHitRecorder.Hit h in _rec.hits)
                    {
                        if (h.c == null) continue;
                        Vector3 hn = h.normal; hn.y = 0f;
                        if (hn.sqrMagnitude < 0.25f) continue;
                        hn.Normalize();
                        string key = h.c.name;
                        stoppers[key] = stoppers.TryGetValue(key, out int c0) ? c0 + 1 : 1;
                        foreach (Vector3 o in normals) spread = Mathf.Max(spread, Vector3.Angle(o, hn));
                        if (normals.Count < 40) normals.Add(hn);
                    }
                    yield return Settle(0.2f);
                    Vector3 axisEye = _player.transform.position + Vector3.up * eye;
                    List<XSurface> surf = XSurfaces(axisEye, nearR);
                    var markers = new List<GameObject>();
                    foreach (XSurface s in surf)
                    {
                        var mk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        DestroyImmediate(mk.GetComponent<Collider>());
                        mk.name = "SurfaceMarker (rig)";
                        mk.GetComponent<Renderer>().sharedMaterial = green;
                        mk.transform.rotation = Quaternion.LookRotation(-s.n);
                        mk.transform.position = s.p - s.n * (XInset + 0.002f);
                        mk.transform.localScale = new Vector3(1f, 1f, 0.004f);
                        markers.Add(mk);
                    }
                    if (contact) nContact += 15;
                    foreach (string m in new[] { "walk60", "raised60", "zoom18" })
                    {
                        if (m == "raised60") { _photo.SetPhotoMode(true); _photo.SetZoom(0f); yield return Settle(0.6f); }
                        if (m == "zoom18") { _photo.SetZoom(1f); yield return Settle(1.0f); }
                        foreach (float pitch in Pitches)
                        {
                            yield return SetPitch(pitch);
                            yield return new WaitForEndOfFrame();
                            nPoses++;
                            string tag = $"{w.name}_a{ang:+0;-0;0}_{m}_p{pitch:+0;-0;0}";
                            int geo = NearPlaneHits(out string names);
                            XPlayerRenderers(false);
                            Color32[] a = XRender(_cam, -1f);
                            Color32[] b = XRender(_cam, 0.005f);
                            int na = GreenCount(a), nb = GreenCount(b), through = GreenDiff(a, b);
                            XClassify(a, b, out int sampled, out int clipWall, out int clipOther, out int unexpl, out string clipNames);
                            if (m == "walk60" && pitch == 0f) _log.AppendLine($"  {tag}: PRESSED — {XGeoDetail()}");   // every pressed pose: is the body in anything, where is the camera
                            if (geo > 0 || clipWall > 0) _log.AppendLine($"  {tag}: through {through} px (sampled {sampled}: wall {clipWall}, no collider {clipOther}, unexplained {unexpl}) [{clipNames}]; {XGeoDetail()}");
                            foreach (Renderer r in nearR) r.enabled = false;
                            int ctrlOff = GreenCount(XRender(_cam, 0.005f));
                            foreach (Renderer r in nearR) r.enabled = true;
                            // control: the camera 3 cm from the nearest surface, same rotation — its near plane is past it
                            int pushThrough = -1; float gap = float.PositiveInfinity;
                            Vector3 cp = _cam.transform.position;
                            XSurface nearest = default; bool any = false;
                            foreach (XSurface s in surf)
                            {
                                float d = Vector3.Dot(cp - s.p, s.n);
                                if (d > -0.05f && d < gap) { gap = d; nearest = s; any = true; }
                            }
                            Color32[] pa = null, pb = null;
                            if (any)
                            {
                                _cam.transform.position = cp - nearest.n * (gap - 0.03f);
                                pa = XRender(_cam, -1f);
                                pb = XRender(_cam, 0.005f);
                                pushThrough = GreenDiff(pa, pb);
                                _cam.transform.position = cp;
                            }
                            XPlayerRenderers(true);
                            bool valid = contact && surf.Count > 0 && ctrlOff > nb && pushThrough > 0;
                            bool isThrough = valid && clipWall > 0;   // a pixel counts only where the near plane cut into a WALL (XClassify)
                            if (contact && surf.Count > 0 && !(ctrlOff > nb && pushThrough > 0)) nCtrlFail++;
                            if (valid) { nValid++; allValid++; }
                            if (isThrough) { nThrough++; allThrough++; maxThrough = Mathf.Max(maxThrough, through); }
                            if (valid) { allSampled += sampled; allWall += clipWall; allOther += clipOther; allUnexpl += unexpl; }
                            if (geo > 0) nGeo++;
                            if (contact && any) minGap = Mathf.Min(minGap, gap);
                            allPoses++;
                            if (through > 0 || geo > 0 || (m == "walk60" && pitch == 0f)) XSave(Path.Combine(fdir, tag + "_A_through_magenta.jpg"), a, b);   // every pressed pose too, for a human
                            if (ang == 0f && m == "walk60" && (pitch == 0f || pitch == -80f))
                            {
                                XSave(Path.Combine(outputDir, $"ref_{w.name}_p{pitch:+0;-0;0}.jpg"), a, b);
                                if (pa != null) XSave(Path.Combine(outputDir, $"ref_{w.name}_p{pitch:+0;-0;0}_control_push.jpg"), pa, pb);
                            }
                            csv.AppendLine($"{w.name},{F(ang)},{m},{F(pitch)},{F(_cam.fieldOfView, "0.0")},{(contact ? 1 : 0)},{F(spread, "0")},{surf.Count},{na},{nb},{through},{ctrlOff},{pushThrough},{(any ? F(gap, "0.000") : "")},{geo},{(valid ? 1 : 0)},{(isThrough ? 1 : 0)},{sampled},{clipWall},{clipOther},{unexpl},{clipNames}");
                        }
                    }
                    _photo.SetZoom(0f);
                    _photo.SetPhotoMode(false);
                    yield return SetPitch(0f);
                    foreach (GameObject mk in markers) Destroy(mk);
                    yield return Settle(0.4f);
                }
                var top = new List<KeyValuePair<string, int>>(stoppers);
                top.Sort((x, y) => y.Value.CompareTo(x.Value));
                var ts = new StringBuilder();
                for (int i = 0; i < Mathf.Min(3, top.Count); i++) ts.Append(top[i].Key.Replace(",", " ")).Append(" x").Append(top[i].Value).Append("; ");
                perWall.AppendLine($"{w.name},{wallCol.name.Replace(",", " ")},{nPoses},{nContact},{nValid},{nThrough},{maxThrough},{nGeo},{nCtrlFail},{(float.IsInfinity(minGap) ? "" : F(minGap, "0.000"))},{ts}");
            }
            foreach (KeyValuePair<Renderer, MeshCollider> kv in _xProbes) if (kv.Value != null) Destroy(kv.Value.gameObject);
            _xProbes.Clear();
            Application.logMessageReceived -= XCount;
            File.WriteAllText(Path.Combine(outputDir, "per-wall.csv"), perWall.ToString());
            File.WriteAllText(Path.Combine(outputDir, "walls.csv"), csv.ToString());
            _log.Append(perWall);
            _log.AppendLine($"WALLS: {walls.Count} walls, {allPoses} poses; valid (contact + the wall hides the marker + both controls pass) {allValid}; " +
                            $"see-through among them (the near plane cut into a collider-backed surface) {allThrough}. Over the valid poses, of {allSampled} sampled A-not-B pixels: " +
                            $"{allWall} the near plane cut a wall, {allOther} it cut collider-less geometry, {allUnexpl} no surface before the near plane (instrument artifact)");
            _log.AppendLine($"console during the run: warnings {_xWarnEvents + _xWarnOther}, errors {_xErrors}\n{_xMsgs}");
        }
    }
}
#endif
