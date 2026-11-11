#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using CameraGame.Events;

namespace CameraGame.World
{
    /// <summary>
    /// Story 2.2 AC2 (Task 9): walk the whole town. The REAL controller, driven by the same input the keys send (W, or
    /// W + Left Shift, through PlayerInput → OnMove / OnSprint), steers along a route through every part in
    /// census/parts.json — visited nearest-first from the spawn, each leg planned on the whole-town NavMesh as a chain of
    /// complete queries (Chain), its corners followed by setting the yaw each frame (the mouse's job). The transform is
    /// never moved to make progress.
    ///   • fall-through check each frame: the feet against the surface straight beneath (a ray from above the head);
    ///     more than 0.3 m below it = fell through. FallRecovery must fire zero times.
    ///   • stick = horizontal speed below 50 % of the commanded speed for more than 0.3 s with W held. Each one is logged
    ///     with what the controller touched, its distance to the NavMesh edge, and an eye photo; then the leg is re-planned
    ///     from where the player stands (up to 3 times). A leg that still cannot be walked is skipped by a LOGGED rig
    ///     placement at its end, so one defect cannot hide the rest of the route.
    /// staging "walk" or "sprint".
    /// </summary>
    public partial class TownRigRunner
    {
        partial void ExtraModes5(ref IEnumerator routine)
        {
            if (mode == "tour") routine = Tour();
            else if (mode == "film") routine = Film();
        }

        /// <summary>
        /// A short real-time film for Alexv's edge check (human check 1): the real controller walks to the shore barrier
        /// and along the water's edge, then up the east hill into the ring's limit. Every frame is captured from the
        /// screen (ScreenCapture after WaitForEndOfFrame) with its time, so tools/film.py can rebuild it at real speed.
        /// </summary>
        private IEnumerator Film()
        {
            if (_manager != null) { foreach (EventActor ea in _manager.ActiveActors) ea.gameObject.SetActive(false); _manager.enabled = false; }
            Directory.CreateDirectory(Path.Combine(outputDir, "frames"));
            var times = new StringBuilder("frame,t,segment,x,y,z\n");
            int f = 0;
            var segs = new[]
            {
                new { name = "shore", at = new Vector3(-100f, 0f, 250f), yaw = 0f, legs = new[] { new Vector2(0f, 5f), new Vector2(90f, 9f) } },
                new { name = "hill", at = new Vector3(205f, 0f, -99f), yaw = 90f, legs = new[] { new Vector2(90f, 14f) } },
            };
            foreach (var sg in segs)
            {
                Place(sg.at, sg.yaw);
                Keys();
                yield return Settle(1f);
                foreach (Vector2 leg in sg.legs)
                {
                    Keys(Key.W);
                    float t = 0f;
                    while (t < leg.y)
                    {
                        _player.transform.rotation = Quaternion.Euler(0f, leg.x, 0f);   // the mouse's job
                        yield return new WaitForEndOfFrame();
                        t += Time.deltaTime;
                        Vector3 p = _player.transform.position;
                        CaptureScreen(Path.Combine(outputDir, "frames", $"f{f:0000}.jpg"));
                        times.AppendLine($"{f},{F(Time.time, "0.0000")},{sg.name},{F(p.x)},{F(p.y)},{F(p.z)}");
                        f++;
                    }
                    Keys();
                }
                _log.AppendLine($"film segment {sg.name}: ends at {V(_player.transform.position)}");
            }
            File.WriteAllText(Path.Combine(outputDir, "frames", "times.csv"), times.ToString());
            _log.AppendLine($"film: {f} frames");
        }

        /// <summary>What the controller touched sideways since <paramref name="since"/> (Time.time): name, kind, height.</summary>
        private string Touched(float since, Vector3 feet)
        {
            var touched = new Dictionary<string, int>();
            foreach (TownHitRecorder.Hit h in _rec.hits)
            {
                if (h.t < since || h.c == null || Vector3.Angle(h.normal, Vector3.up) < 30f) continue;
                string key = $"{h.c.name} ({h.c.GetType().Name}, top {F(h.c.bounds.max.y - feet.y, "0.00")} m over the feet)";
                touched[key] = touched.TryGetValue(key, out int c) ? c + 1 : 1;
            }
            var tb = new StringBuilder();
            foreach (var kv in touched) tb.Append($"{kv.Key} x{kv.Value}; ");
            return tb.Length > 0 ? tb.ToString() : "nothing sideways (slope or step)";
        }

        private IEnumerator Tour()
        {
            string gait = staging == "sprint" ? "sprint" : "walk";
            if (_manager != null) { foreach (EventActor ea in _manager.ActiveActors) ea.gameObject.SetActive(false); _manager.enabled = false; }
            Part[] parts = JsonUtility.FromJson<PartList>(ReadData("census/parts.json")).parts;
            Directory.CreateDirectory(Path.Combine(outputDir, "photos"));
            float t0 = 0f;
            while (!_cc.isGrounded && t0 < 8f) { t0 += Time.deltaTime; yield return null; }
            yield return DriveControl();
            Place(new Vector3(0.45f, 0f, 3.6f), 58.8f);   // the spawn
            yield return Settle(0.8f);
            int recoveriesAtStart = _fall != null ? _fall.Recoveries : 0;
            float setSpeed = gait == "walk" ? (_tpc.Config != null ? _tpc.Config.SafeWalkSpeed : 4f) : (_tpc.Config != null ? _tpc.Config.SafeSprintSpeed : 7f);

            // the order: nearest part first, from the spawn
            var left = new List<int>(); for (int i = 0; i < parts.Length; i++) left.Add(i);
            var order = new List<int>();
            Vector3 cur = _player.transform.position;
            while (left.Count > 0)
            {
                int best = left[0];
                foreach (int i in left) if (Hz(cur, new Vector3(parts[i].x, 0f, parts[i].z)) < Hz(cur, new Vector3(parts[best].x, 0f, parts[best].z))) best = i;
                order.Add(best); left.Remove(best); cur = new Vector3(parts[best].x, parts[best].y, parts[best].z);
            }
            var orderNames = new StringBuilder(); foreach (int i in order) orderNames.Append(parts[i].name).Append(' ');
            _log.AppendLine($"TOUR ({gait}, commanded {F(setSpeed)} m/s), order: {orderNames}");

            var trace = new StringBuilder("leg,gait,t,x,y,z,speed,stick,fall,grounded,fell_on\n");
            var sticks = new StringBuilder("leg,gait,t,x,y,z,stick_s,to_navmesh_edge_m,touched,eye_photo,outcome\n");
            var legsCsv = new StringBuilder("leg,from,to,gait,planned_m,walked_m,seconds,replans,sticks,falls,completed,skipped_by_rig\n");
            var path = new NavMeshPath();
            var wp = new List<Vector3>();
            float tTotal = 0f, walkedTotal = 0f; int stickTotal = 0, fallTotal = 0, skipped = 0, completedLegs = 0;
            string from = "spawn";
            var queue = new List<int>(order); var tries = new Dictionary<int, int>(); var visited = new List<Vector3>();
            while (queue.Count > 0)
            {
                int pi = queue[0]; queue.RemoveAt(0);
                Part target = parts[pi];
                if (!NavMesh.SamplePosition(new Vector3(target.x, target.y, target.z), out NavMeshHit th, 2f, NavMesh.AllAreas))
                { _log.AppendLine($"  leg → {target.name}: the part is not on the NavMesh — skipped"); continue; }
                Vector3 goal = th.position;
                int replans = 0, legSticks = 0, legFalls = 0; float legT = 0f, walked = 0f, planned = 0f;
                bool completed = false, skip = false, postponed = false;
                while (!completed && !skip)
                {
                    // plan from where the player stands
                    Vector3 here = _player.transform.position;
                    NavMesh.SamplePosition(here, out NavMeshHit hh, 2f, NavMesh.AllAreas);
                    float plen, pend;
                    if (!Chain(path, hh.position, goal, wp, out plen, out pend))
                    {
                        int n0 = tries.TryGetValue(pi, out int n1) ? n1 : 0;
                        if (n0 < 2 && queue.Count > 0) { tries[pi] = n0 + 1; queue.Add(pi); postponed = true; _log.AppendLine($"  leg → {target.name}: no chain from {V(here)} (closest {F(pend)} m) — postponed"); break; }
                        // last resort: through a part already visited
                        bool via = false;
                        var wp2 = new List<Vector3>();
                        foreach (Vector3 v in visited)
                        {
                            if (!Chain(path, hh.position, v, wp, out float l1, out float _) || !Chain(path, v, goal, wp2, out float l2, out float _)) continue;
                            wp.AddRange(wp2); plen = l1 + l2; via = true;
                            _log.AppendLine($"  leg → {target.name}: routed through the visited part at {V(v)}");
                            break;
                        }
                        if (!via) { _log.AppendLine($"  leg → {target.name}: NO chain from {V(here)} (closest {F(pend)} m), not even through a visited part"); skip = true; break; }
                    }
                    if (replans == 0) planned = plen;
                    var corners = new List<Vector3>();
                    Vector3 prev = hh.position;
                    foreach (Vector3 w in wp) { Leg(path, prev, w, out float _, out float _); corners.AddRange(path.corners); prev = w; }
                    int ci = 1;
                    if (gait == "walk") Keys(Key.W); else Keys(Key.W, Key.LeftShift);
                    float below = 0f, stick = 0f, stickStart = 0f, legTimeout = plen / setSpeed * 2f + 20f, tl = 0f;
                    Vector3 last = _player.transform.position;
                    _rec.hits.Clear(); _rec.recording = true;
                    bool stuck = false;
                    while (tl < legTimeout)
                    {
                        Vector3 p = _player.transform.position;
                        while (ci < corners.Count - 1 && Hz(p, corners[ci]) < (gait == "walk" ? 0.3f : 0.45f)) ci++;
                        if (ci >= corners.Count - 1 && Hz(p, goal) < 0.6f) { completed = true; break; }
                        _player.transform.rotation = Quaternion.Euler(0f, YawTo(p, corners[Mathf.Min(ci, corners.Count - 1)]), 0f);   // the mouse's job
                        yield return null;
                        float dt = Mathf.Max(1e-5f, Time.deltaTime);
                        tl += Time.deltaTime;
                        Vector3 q = _player.transform.position;
                        float sp = Hz(q, last) / dt;
                        walked += Hz(q, last);
                        last = q;
                        // fell through? the surface straight beneath, from above the head
                        // just above the head (the capsule's height + 5 cm): an overhang higher than the head is not the
                        // surface beneath (the first run started at 2.5 m and flagged 27 frames under boulders and tyres)
                        bool fell = false; string fellOn = "";
                        if (Physics.Raycast(q + Vector3.up * (_cc.height + 0.05f), Vector3.down, out RaycastHit gh, 60f, ~0, QueryTriggerInteraction.Ignore) && !IsMine(gh.collider))
                        { fell = q.y < gh.point.y - 0.3f; if (fell) fellOn = gh.collider.name.Replace(",", " "); }
                        if (fell) { legFalls++; fallTotal++; }
                        bool isStick = false;
                        if (tl > 0.5f && sp < 0.5f * setSpeed)
                        {
                            below += Time.deltaTime;
                            if (below > 0.3f) { if (stick == 0f) stickStart = Time.time - 0.3f; stick += Time.deltaTime; isStick = true; }
                        }
                        else
                        {
                            if (stick > 0f)
                            {
                                // a short stick that freed itself: logged and photographed all the same, so none is hidden
                                legSticks++; stickTotal++;
                                Vector3 s0 = _player.transform.position;
                                float e0 = NavMesh.FindClosestEdge(s0, out NavMeshHit eh0, NavMesh.AllAreas) ? eh0.distance : -1f;
                                string ph0 = $"stick_{gait}_{target.name}_{replans}_{legSticks}_freed.jpg";
                                RenderTo(_cam, Path.Combine(outputDir, "photos", ph0));
                                sticks.AppendLine($"{target.name},{gait},{F(tTotal + tl)},{F(s0.x)},{F(s0.y)},{F(s0.z)},{F(stick)},{F(e0)},{Touched(stickStart - 0.5f, s0).Replace(",", " ")},{ph0},freed itself");
                            }
                            stick = 0f; below = 0f;
                        }
                        if (Mathf.FloorToInt(tl * 4f) != Mathf.FloorToInt((tl - Time.deltaTime) * 4f) || isStick || fell)
                            trace.AppendLine($"{target.name},{gait},{F(tTotal + tl)},{F(q.x)},{F(q.y)},{F(q.z)},{F(sp)},{(isStick ? 1 : 0)},{(fell ? 1 : 0)},{(_cc.isGrounded ? 1 : 0)},{fellOn}");
                        if (stick > 1.5f) { stuck = true; break; }
                    }
                    Keys();
                    _rec.recording = false;
                    legT += tl;
                    if (completed) break;
                    // a stick (or a timeout): record it, photograph it from the eye, re-plan
                    Vector3 s = _player.transform.position;
                    float edge = NavMesh.FindClosestEdge(s, out NavMeshHit eh, NavMesh.AllAreas) ? eh.distance : -1f;
                    if (stuck) { legSticks++; stickTotal++; }
                    string tb = Touched(stuck ? stickStart - 0.5f : Time.time - 3f, s);
                    string photo = $"stick_{gait}_{target.name}_{replans}.jpg";
                    yield return new WaitForEndOfFrame();
                    RenderTo(_cam, Path.Combine(outputDir, "photos", photo));
                    replans++;
                    string outcome = stuck ? (replans <= 3 ? "re-planned" : "skipped by rig placement") : (replans <= 3 ? "timeout, re-planned" : "timeout, skipped");
                    sticks.AppendLine($"{target.name},{gait},{F(tTotal + legT)},{F(s.x)},{F(s.y)},{F(s.z)},{F(stick)},{F(edge)},{tb.Replace(",", " ")},{photo},{outcome}");
                    _log.AppendLine($"  STICK on leg → {target.name} at {V(s)}: {F(stick)} s below half speed; NavMesh edge {F(edge)} m; touched {tb}; {outcome}");
                    if (replans > 3)
                    {
                        skip = true; skipped++;
                        Place(goal, 0f);   // LOGGED rig placement so the rest of the route is still walked
                        yield return Settle(0.6f);
                    }
                    else
                    {
                        // step back a little along the way it came, then re-plan (the controller's own walk, S held)
                        Keys(Key.S); yield return Settle(0.4f); Keys(); yield return Settle(0.2f);
                    }
                }
                if (postponed) continue;
                if (completed) { completedLegs++; visited.Add(goal); }
                tTotal += legT; walkedTotal += walked;
                legsCsv.AppendLine($"{completedLegs + skipped},{from},{target.name},{gait},{F(planned)},{F(walked)},{F(legT)},{replans},{legSticks},{legFalls},{(completed ? 1 : 0)},{(skip ? 1 : 0)}");
                _log.AppendLine($"  leg {from} → {target.name}: planned {F(planned, "0")} m, walked {F(walked, "0")} m in {F(legT, "0.0")} s, re-plans {replans}, sticks {legSticks}, falls {legFalls}, completed {completed}");
                from = target.name;
                yield return Settle(0.3f);
            }
            int recoveries = (_fall != null ? _fall.Recoveries : 0) - recoveriesAtStart;
            _log.AppendLine($"TOUR {gait}: {completedLegs}/{order.Count} legs walked to the part, {skipped} skipped by rig placement; {F(walkedTotal, "0")} m in {F(tTotal, "0")} s " +
                            $"(mean {F(walkedTotal / Mathf.Max(1f, tTotal), "0.00")} m/s, commanded {F(setSpeed)}); sticks {stickTotal}; fall-through frames {fallTotal}; FallRecovery fired {recoveries} times");
            File.WriteAllText(Path.Combine(outputDir, "trace.csv"), trace.ToString());
            File.WriteAllText(Path.Combine(outputDir, "sticks.csv"), sticks.ToString());
            File.WriteAllText(Path.Combine(outputDir, "legs.csv"), legsCsv.ToString());
        }
    }
}
#endif
