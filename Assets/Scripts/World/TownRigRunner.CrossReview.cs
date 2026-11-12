#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using CameraGame.Events;

namespace CameraGame.World
{
    /// <summary>
    /// Story 2.2 FIX phase: the cross-review's three findings, run instead of argued (rig code, not shipped). Mode "xreview":
    ///   staging "longroute" — finding 1. The REAL pooled EventActor (the drunk) is handed a staged route whose first waypoint
    ///                         is far from the pub (one NavMesh query from the pub ends short of it: the search budget,
    ///                         Task 8.2). Rig staging only, never the assets: a CLONE of its definition whose Build phase walks
    ///                         for 10 minutes, and its agent at 20 m/s to keep the run short. Logs every waypoint-index change
    ///                         with how far the actor stood from the waypoint it left, and each waypoint's closest approach.
    ///                         Scenarios: far but reachable (the farm), a dead end (corner_nw: a lone agent never got there,
    ///                         Task 8.2), then the farm again on the same pooled instance (the second cycle).
    ///   staging "overhead"  — finding 3. A beam (BoxCollider, Default layer) whose underside is h above the ground is walked
    ///                         into through the real input, then looked at from pitch −80…0. Inside the beam sits a double-
    ///                         sided green marker inset 1 cm, which can only be seen once the near plane is past the beam's
    ///                         surface. Each pose renders A (the real near clip) and B (near clip 0.005 from the same pose);
    ///                         see-through = green in A and not in B. Controls per pose: the beam's renderer off (the marker
    ///                         must show), and the camera put 3 cm under the beam's underside (its near plane is then past it,
    ///                         so A−B must show green).
    /// </summary>
    public partial class TownRigRunner
    {
        partial void ExtraModes7(ref IEnumerator routine)
        {
            if (mode != "xreview") return;
            if (staging == "longroute") routine = XLongRoute();
            else if (staging == "overhead") routine = XOverhead();
            else ExtraModes8(ref routine);
        }

        partial void ExtraModes8(ref IEnumerator routine);

        private static readonly FieldInfo DefField = typeof(EventActor).GetField("definition", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo WpIndexField = typeof(EventActor).GetField("_waypointIndex", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo RouteWpField = typeof(EventRoute).GetField("waypoints", BindingFlags.NonPublic | BindingFlags.Instance);

        private int _xWarnEvents, _xWarnOther, _xErrors;
        private readonly StringBuilder _xMsgs = new StringBuilder();

        private void XCount(string m, string st, LogType type)
        {
            if (type == LogType.Warning)
            {
                if (m.StartsWith("[Events]")) { _xWarnEvents++; _xMsgs.AppendLine("  [Events] WARNING " + m); }
                else { _xWarnOther++; _xMsgs.AppendLine("  OTHER WARNING " + m); }
            }
            else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { _xErrors++; _xMsgs.AppendLine("  ERROR " + m); }
        }

        // =============================================================================================
        // finding 1: far waypoints
        // =============================================================================================

        private IEnumerator XLongRoute()
        {
            _log.Insert(0, "CROSS-REVIEW finding 1 (rig staging, never the assets): the real pooled EventActor walks staged routes with far waypoints. " +
                           "The dead-end scenario is there ON PURPOSE: any '[Events]' warning it causes is provoked, and each is listed at the end.\n");
            if (_manager == null || WpIndexField == null || DefField == null || RouteWpField == null) { _log.AppendLine("RIG BROKEN: no EventManager, or a private field was renamed"); yield break; }
            Application.logMessageReceived += XCount;
            float t0 = 0f;
            while (_manager.ActiveActors.Count == 0 && t0 < 30f) { t0 += Time.deltaTime; yield return null; }
            if (_manager.ActiveActors.Count == 0) { _log.AppendLine("RIG BROKEN: the drunk never spawned"); Application.logMessageReceived -= XCount; yield break; }
            EventActor actor = _manager.ActiveActors[0];
            _manager.enabled = false;                 // no respawn while the staged lifecycles run
            // the player stays idle and ENABLED: a disabled controller never calls Move, so isGrounded goes stale and FallRecovery
            // (correctly) reads 'airborne for over 3 s' — the before-fix run's 28 [World] warnings were that rig artifact
            var def = (EventDefinition)DefField.GetValue(actor);
            var clone = Instantiate(def);
            clone.name = def.name + " (rig: Build walks 10 min)";
            clone.spawn.duration = 0.5f; clone.build.duration = 600f; clone.build.advanceAlongRoute = true;
            DefField.SetValue(actor, clone);
            NavMeshAgent ag = actor.GetComponent<NavMeshAgent>();
            float prevSpeed = ag.speed, prevAcc = ag.acceleration, prevAng = ag.angularSpeed;
            ag.speed = 20f; ag.acceleration = 80f; ag.angularSpeed = 1440f;
            _log.AppendLine($"actor {actor.name}: definition → clone '{clone.name}'; agent speed {F(prevSpeed)} → 20 m/s; stoppingDistance {F(ag.stoppingDistance, "0.0000")}; autoRepath {ag.autoRepath}; autoBraking {ag.autoBraking}");

            Part[] parts = JsonUtility.FromJson<PartList>(ReadData("census/parts.json")).parts;
            Vector3 PartAt(string n) { foreach (Part p in parts) if (p.name == n) return new Vector3(p.x, p.y, p.z); return Vector3.zero; }
            GameObject spawn = GameObject.Find("SpawnPoint");
            Vector3 pub = spawn != null ? spawn.transform.position : new Vector3(31.8f, 0.55f, 19f);
            var routeGo = new GameObject("EventRoute (rig)");
            var route = routeGo.AddComponent<EventRoute>();
            var scenarios = new (string name, string[] wps)[]
            {
                ("reachable_far_farm", new[] { "farm_industrial_yard", "house_row" }),
                ("dead_end_corner_nw", new[] { "corner_nw", "house_row" }),
                ("reachable_far_farm_cycle2", new[] { "farm_industrial_yard", "house_row" }),
                // FIX phase, second session: starts AT the dead end (below), where the agent's own request toward corner_nw never
                // comes back (an isolated probe: still pending after 78 s). Exercises EventActor's stall watchdog every run.
                ("stall_from_dead_end", new[] { "corner_nw", "house_row" }),
            };
            var deadEnd = new Vector3(-546.188f, 0.994f, 254.666f);   // where every corner_nw run ended (trace.csv)
            var csv = new StringBuilder("scenario,t,index,x,y,z,to_wp_m,remaining,status,pending,has_path\n");
            var events = new StringBuilder("scenario,t,from_index,to_index,x,z,dist_to_left_waypoint_m,remaining,status\n");
            var path = new NavMeshPath();
            foreach (var s in scenarios)
            {
                var tr = new Transform[s.wps.Length];
                for (int i = 0; i < s.wps.Length; i++)
                {
                    var w = new GameObject($"WP{i} {s.wps[i]} (rig)");
                    w.transform.SetParent(routeGo.transform);
                    w.transform.position = PartAt(s.wps[i]);
                    tr[i] = w.transform;
                }
                RouteWpField.SetValue(route, tr);
                NavMesh.CalculatePath(pub, tr[0].position, NavMesh.AllAreas, path);
                Vector3[] c = path.corners;
                _log.AppendLine($"{s.name}: route {string.Join(" → ", s.wps)}; ONE query pub → WP0: {path.status}, ends {(c.Length > 0 ? F(Hz(c[c.Length - 1], tr[0].position), "0.0") : "-")} m from WP0");
                int w0 = _xWarnEvents;
                Vector3 start = s.name == "stall_from_dead_end" ? deadEnd : pub;
                // the stall, forced: a pathfinding budget of 1 iteration per frame (Unity's default is 100) until the actor leaves
                // waypoint 0, so the request toward corner_nw cannot come back inside EventActor.PendingTimeout at any frame rate
                int itersBefore = NavMesh.pathfindingIterationsPerFrame;
                bool forcedStall = s.name == "stall_from_dead_end";
                if (forcedStall)
                {
                    NavMesh.pathfindingIterationsPerFrame = 1;
                    _log.AppendLine($"  {s.name}: STAGING — NavMesh.pathfindingIterationsPerFrame {itersBefore} → 1 until waypoint 0 is left (EventActor.PendingTimeout {F(EventActor.PendingTimeout, "0.0")} s)");
                }
                actor.transform.SetPositionAndRotation(start, Quaternion.identity);   // as EventManager.Spawn does
                actor.Begin(route);
                int last = (int)WpIndexField.GetValue(actor);
                var closest = new float[tr.Length];
                for (int i = 0; i < closest.Length; i++) closest[i] = float.PositiveInfinity;
                float t = 0f, sinceTick = 1f, stillFor = 0f;
                Vector3 prevPos = actor.transform.position;
                while (t < 180f)
                {
                    yield return null;
                    t += Time.deltaTime; sinceTick += Time.deltaTime;
                    int idx = (int)WpIndexField.GetValue(actor);
                    Vector3 p = actor.transform.position;
                    for (int i = 0; i < tr.Length; i++) closest[i] = Mathf.Min(closest[i], Hz(p, tr[i].position));
                    if (idx != last)
                    {
                        if (forcedStall && NavMesh.pathfindingIterationsPerFrame != itersBefore)
                        {
                            NavMesh.pathfindingIterationsPerFrame = itersBefore;
                            _log.AppendLine($"  t {F(t, "0.0")} s: pathfindingIterationsPerFrame put back to {itersBefore}; the stalled request had been pending {F(t, "0.0")} s");
                        }
                        float d = Hz(p, tr[last].position);
                        events.AppendLine($"{s.name},{F(t)},{last},{idx},{F(p.x)},{F(p.z)},{F(d, "0.00")},{F(ag.remainingDistance)},{ag.pathStatus}");
                        _log.AppendLine($"  t {F(t, "0.0")} s: waypoint {last} → {idx} with the actor {F(d, "0.0")} m from waypoint {last} ({s.wps[last]}) at {V(p)}; remainingDistance {F(ag.remainingDistance)}, {ag.pathStatus}");
                        last = idx;
                        stillFor = 0f;   // a new waypoint: give the agent time to plan and move off (the first after-fix run stopped watching here)
                    }
                    if (sinceTick >= 1f)
                    {
                        sinceTick = 0f;
                        csv.AppendLine($"{s.name},{F(t)},{idx},{F(p.x)},{F(p.y)},{F(p.z)},{F(Hz(p, tr[idx].position), "0.00")},{F(ag.remainingDistance)},{ag.pathStatus},{ag.pathPending},{ag.hasPath}");
                    }
                    stillFor = Hz(p, prevPos) < 0.01f ? stillFor + Time.deltaTime : 0f;
                    prevPos = p;
                    if (idx == tr.Length - 1 && stillFor > 3f && Hz(p, tr[idx].position) < 2f) break;   // standing AT the last waypoint for 3 s
                    if (stillFor > 25f) break;                           // stuck before the last waypoint
                }
                if (forcedStall) NavMesh.pathfindingIterationsPerFrame = itersBefore;
                Vector3 end = actor.transform.position;
                var cl = new StringBuilder();
                for (int i = 0; i < tr.Length; i++) cl.Append($"WP{i} {s.wps[i]} {F(closest[i], "0.0")} m; ");
                _log.AppendLine($"  {s.name} END after {F(t, "0.0")} s: index {last}, actor at {V(end)}, {F(Hz(end, tr[tr.Length - 1].position), "0.0")} m from the last waypoint. " +
                                $"Closest approach: {cl}[Events] warnings in this scenario: {_xWarnEvents - w0}");
                foreach (Transform w in tr) Destroy(w.gameObject);
                yield return null;
            }
            DefField.SetValue(actor, def);
            ag.speed = prevSpeed; ag.acceleration = prevAcc; ag.angularSpeed = prevAng;
            Destroy(clone);
            Destroy(routeGo);
            Application.logMessageReceived -= XCount;
            _log.AppendLine($"console during the scenarios: [Events] warnings {_xWarnEvents}, other warnings {_xWarnOther}, errors {_xErrors}\n{_xMsgs}");
            File.WriteAllText(Path.Combine(outputDir, "trace.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(outputDir, "index_changes.csv"), events.ToString());
        }

        // =============================================================================================
        // finding 3: a low overhead in front of the eye
        // =============================================================================================

        private Material _xGreen;

        /// <summary>Unlit pure green, rendered from both sides, so a marker can be seen from INSIDE a solid as well.</summary>
        private Material DoubleSidedGreen()
        {
            if (_xGreen != null) return _xGreen;
            _xGreen = new Material(Shader.Find("Universal Render Pipeline/Unlit")) { color = Color.green, name = "Green double-sided (rig)" };
            _xGreen.SetFloat("_Cull", 0f);   // CullMode.Off
            return _xGreen;
        }

        /// <summary>Renders the camera with the given near clip (or its own when <paramref name="near"/> ≤ 0) and returns
        /// the pure-green mask. The camera's own near clip is put back before returning.</summary>
        private static bool[] GreenMask(Camera c, float near, out int count, string savePath = null)
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
            var mask = new bool[px.Length];
            count = 0;
            for (int i = 0; i < px.Length; i++)
                if (px[i].g > 200 && px[i].r < 50 && px[i].b < 50) { mask[i] = true; count++; }
            if (savePath != null) File.WriteAllBytes(savePath, tex.EncodeToJPG(85));
            Destroy(tex);
            return mask;
        }

        private static int Diff(bool[] a, bool[] b)
        {
            int n = 0;
            for (int i = 0; i < a.Length; i++) if (a[i] && !b[i]) n++;
            return n;
        }

        private IEnumerator XOverhead()
        {
            _log.Insert(0, "CROSS-REVIEW finding 3 (rig staging): a beam whose underside is h above the ground, walked into through the real input " +
                           "and looked at from below. No warning or error is provoked on purpose.\n");
            Application.logMessageReceived += XCount;
            if (_manager != null) { foreach (EventActor a in _manager.ActiveActors) a.gameObject.SetActive(false); _manager.enabled = false; }
            float t0 = 0f;
            while (!_cc.isGrounded && t0 < 8f) { t0 += Time.deltaTime; yield return null; }
            Vector3 start = new Vector3(60f, 0f, -2f);
            const float yaw = 270f;
            Place(start, yaw);
            yield return Settle(1f);
            yield return DriveControl();
            yield return PitchControl();
            float eye = _tpc.Config != null ? _tpc.Config.SafeEyeHeight : 1.65f;
            _log.AppendLine($"controller: height {F(_cc.height)} radius {F(_cc.radius)} skin {F(_cc.skinWidth, "0.000")} centre {V(_cc.center)}; eye {F(eye)}; " +
                            $"near clip {F(_cam.nearClipPlane, "0.000")}; FOV {F(_cam.fieldOfView)}; aspect {F(_cam.aspect)}");
            Vector3 fwd = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
            Material green = DoubleSidedGreen();
            var csv = new StringBuilder("h_m,tilt_deg,pitch,state,axis_to_edge_m,contact_fwd_m,contact_up_m,cam_forward_m,cam_up_m,A_px,B_px,through_px,ctrl_beam_off_px,ctrl_under_through_px,geo_hits,geo_names\n");
            Directory.CreateDirectory(Path.Combine(outputDir, "frames"));
            // flat beams (underside h), then slabs whose near edge is 2.2 m up and which DESCEND forward at tilt° — a sloping
            // underside can meet the top-front of the capsule inside the cast's start sphere (worked out in the fix notes)
            var cases = new List<Vector2>();
            foreach (float hf in new[] { 1.60f, 1.65f, 1.70f, 1.72f, 1.74f, 1.76f, 1.78f, 1.80f, 1.85f, 1.95f }) cases.Add(new Vector2(hf, 0f));
            foreach (float tl in new[] { 10f, 15f, 20f, 25f, 30f, 45f }) cases.Add(new Vector2(2.2f, tl));
            float[] pitches = { -80f, -60f, -45f, 0f };
            int throughPoses = 0, geoPoses = 0, ctrlFails = 0;
            foreach (Vector2 cs in cases)
            {
                float h = cs.x, tilt = cs.y;
                Place(start, yaw);
                yield return Settle(0.4f);
                float gy = GroundY(start);
                Vector3 edge = new Vector3(start.x, gy, start.z) + fwd * 1.5f;
                var beam = GameObject.CreatePrimitive(PrimitiveType.Cube);
                beam.name = $"Beam h{F(h)} tilt{F(tilt)} (rig)";
                beam.transform.rotation = Quaternion.LookRotation(fwd) * Quaternion.Euler(tilt, 0f, 0f);
                beam.transform.localScale = new Vector3(3f, 0.3f, tilt > 0f ? 4f : 2f);
                beam.transform.position = edge + Vector3.up * h + beam.transform.rotation * new Vector3(0f, 0.15f, beam.transform.localScale.z * 0.5f);
                var marker = GameObject.CreatePrimitive(PrimitiveType.Cube);
                DestroyImmediate(marker.GetComponent<Collider>());
                marker.name = "OverheadMarker (rig)";
                marker.GetComponent<Renderer>().sharedMaterial = green;
                marker.transform.SetPositionAndRotation(beam.transform.position, beam.transform.rotation);
                marker.transform.localScale = beam.transform.localScale - new Vector3(0.02f, 0.02f, 0.02f);
                Renderer beamR = beam.GetComponent<Renderer>();
                Collider beamC = beam.GetComponent<Collider>();
                Physics.SyncTransforms();
                _rec.hits.Clear(); _rec.recording = true;
                Keys(Key.W);
                float t = 0f, still = 0f;
                Vector3 prev = _player.transform.position;
                bool contact = false;
                string state = "timeout";
                while (t < 5f)
                {
                    _player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
                    yield return null;
                    t += Time.deltaTime;
                    Vector3 q = _player.transform.position;
                    float sp = Hz(q, prev) / Mathf.Max(1e-5f, Time.deltaTime);
                    prev = q;
                    if (!contact) foreach (TownHitRecorder.Hit hh in _rec.hits) if (hh.c == beamC) { contact = true; break; }
                    if (Vector3.Dot(q - edge, fwd) > (tilt > 0f ? 4.5f : 1.0f)) { state = "walked_under"; break; }
                    if (contact && sp < 0.05f) { still += Time.deltaTime; if (still > 0.3f) { state = "held_by_beam"; break; } }
                    else still = 0f;
                }
                Keys();
                _rec.recording = false;
                yield return Settle(0.3f);
                float axisToEdge = Vector3.Dot(edge - _player.transform.position, fwd);
                // where the beam touched the body, relative to the feet: the highest contact point
                Vector3 feet0 = _player.transform.position; float cFwd = float.NaN, cUp = float.NaN;
                foreach (TownHitRecorder.Hit hh in _rec.hits)
                    if (hh.c == beamC && (float.IsNaN(cUp) || hh.point.y - feet0.y > cUp)) { cUp = hh.point.y - feet0.y; cFwd = Vector3.Dot(hh.point - feet0, fwd); }
                _log.AppendLine($"h {F(h)} tilt {F(tilt)}: {state} (contact {contact}); body axis {F(axisToEdge, "0.000")} m before the beam's near edge (negative = under it); " +
                                $"highest contact {F(cFwd, "0.000")} m forward, {F(cUp, "0.000")} m up from the feet");
                foreach (float pitch in pitches)
                {
                    yield return SetPitch(pitch);
                    yield return new WaitForEndOfFrame();
                    Vector3 feet = _player.transform.position;
                    Vector3 cp = _cam.transform.position;
                    float camFwd = Vector3.Dot(cp - feet, fwd), camUp = cp.y - feet.y;
                    string tag = $"h{h:0.00}_t{tilt:0}_p{pitch:+0;-0;0}";
                    bool[] a = GreenMask(_cam, -1f, out int na);
                    bool[] b = GreenMask(_cam, 0.005f, out int nb);
                    int through = Diff(a, b);
                    beamR.enabled = false;
                    GreenMask(_cam, 0.005f, out int nctrl);
                    beamR.enabled = true;
                    int geo = NearPlaneHits(out string names);
                    if (through > 0 || geo > 0 || pitch == -80f) GreenMask(_cam, -1f, out _, Path.Combine(outputDir, "frames", tag + "_A.jpg"));
                    // control: the camera 3 cm under the underside — the near plane is then past the beam. Flat: 0.2 m inside the
                    // edge. Tilted: straight above the real camera, 3 cm under the sloping underside.
                    Vector3 keep = _cam.transform.position;
                    Vector3 under = edge + fwd * 0.2f;
                    under.y = gy + h - 0.03f;
                    if (tilt > 0f)
                    {
                        under = keep;
                        if (beamC.Raycast(new Ray(keep + Vector3.down * 0.5f, Vector3.up), out RaycastHit uh, 5f)) under.y = uh.point.y - 0.03f;
                    }
                    _cam.transform.position = under;
                    bool[] a2 = GreenMask(_cam, -1f, out _, pitch == -80f ? Path.Combine(outputDir, "frames", tag + "_control_under.jpg") : null);
                    bool[] b2 = GreenMask(_cam, 0.005f, out _);
                    int ctrlThrough = Diff(a2, b2);
                    _cam.transform.position = keep;
                    if (through > 0) throughPoses++;
                    if (geo > 0) geoPoses++;
                    if (nctrl == 0 || ctrlThrough == 0) ctrlFails++;
                    csv.AppendLine($"{F(h)},{F(tilt)},{F(pitch)},{state},{F(axisToEdge, "0.000")},{F(cFwd, "0.000")},{F(cUp, "0.000")},{F(camFwd, "0.0000")},{F(camUp, "0.0000")},{na},{nb},{through},{nctrl},{ctrlThrough},{geo},{names}");
                }
                yield return SetPitch(0f);
                Destroy(beam);
                Destroy(marker);
                yield return null;
            }
            Application.logMessageReceived -= XCount;
            _log.AppendLine($"OVERHEAD: {cases.Count} beams x {pitches.Length} pitches — see-through poses (A−B > 0) {throughPoses}; geometric near-plane hits {geoPoses}; " +
                            $"poses whose controls failed (beam off showed no marker, or the camera under the beam showed nothing) {ctrlFails}");
            _log.AppendLine($"console during the run: warnings {_xWarnEvents + _xWarnOther}, errors {_xErrors}\n{_xMsgs}");
            File.WriteAllText(Path.Combine(outputDir, "overhead.csv"), csv.ToString());
        }
    }
}
#endif
