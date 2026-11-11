#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.AI;
using CameraGame.Events;

namespace CameraGame.World
{
    /// <summary>
    /// Story 2.2 AC5 / AC6 rig modes:
    ///   paths — from the pub (the drunk's spawn point) to every part in census/parts.json. Measured on this mesh (Task 8.2):
    ///           ONE NavMesh.CalculatePath from the pub runs out of search budget ~270–430 m out whatever the target
    ///           (PathPartial), while the same targets complete from nearer starts. So every target is judged two ways:
    ///           the single query from the pub (reported), and a chain of single queries through HUB points (the parts
    ///           themselves — an event's route is a chain of waypoints, ADR-3). A part passes when a chain exists from the
    ///           pub in which EVERY leg is PathComplete with its last corner within 0.5 m. The 25 m grid tries the pub, then
    ///           the four nearest reached hubs; anything left is an island (classified) or has no NavMesh (what is there).
    ///           Real NavMeshAgents (agent type 0, 3.5 m/s) then walk the three longest chains leg by leg (each leg's arrival
    ///           by the agent's own test, lesson 68), with one probe agent given the farthest part as a SINGLE destination.
    ///   warp  — the Warp guard, PROVOKED ON PURPOSE: the spawn point is staged off the NavMesh; the actor must warn
    ///           once, stand down and perform its lifecycles in place, never at its last despawn point.
    /// </summary>
    public partial class TownRigRunner
    {
        [System.Serializable] public class Part { public string name; public string what; public float x, y, z; }
        [System.Serializable] public class PartList { public Part[] parts; }

        partial void ExtraModes3(ref IEnumerator routine)
        {
            if (mode == "paths") routine = Paths();
            else if (mode == "warp") routine = WarpGuard();
            else ExtraModes4(ref routine);
        }

        partial void ExtraModes4(ref IEnumerator routine);

        private static float PathLength(Vector3[] c)
        {
            float len = 0f;
            for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]);
            return len;
        }

        /// <summary>One NavMesh.CalculatePath: complete AND its last corner within 0.5 m of the target (AC5's pass).</summary>
        private static bool Leg(NavMeshPath path, Vector3 a, Vector3 b, out float len, out float end)
        {
            NavMesh.CalculatePath(a, b, NavMesh.AllAreas, path);
            Vector3[] c = path.corners;
            len = PathLength(c);
            end = c.Length > 0 ? Vector3.Distance(c[c.Length - 1], b) : float.PositiveInfinity;
            return path.status == NavMeshPathStatus.PathComplete && end <= 0.5f;
        }

        /// <summary>
        /// A route as a chain of single queries: query from <paramref name="from"/> toward <paramref name="to"/>; while the
        /// answer is partial (the search budget ran out, Task 8.2) continue from where it stopped, as long as each leg gets
        /// closer. Returns the waypoints after <paramref name="from"/> (the last one is <paramref name="to"/> on success).
        /// Then EVERY leg between consecutive waypoints is re-queried on its own and must be PathComplete with its end
        /// within 0.5 m — the chain is only accepted when each leg would complete as a waypoint-to-waypoint route.
        /// </summary>
        private static bool Chain(NavMeshPath path, Vector3 from, Vector3 to, List<Vector3> waypoints, out float length, out float bestEnd)
        {
            waypoints.Clear(); length = 0f; bestEnd = float.PositiveInfinity;
            Vector3 at = from;
            for (int k = 0; k < 10; k++)
            {
                bool ok = Leg(path, at, to, out float len, out float end);
                if (ok) { waypoints.Add(to); break; }
                Vector3[] c = path.corners;
                if (c.Length == 0 || end > bestEnd - 1f) { bestEnd = Mathf.Min(bestEnd, end); return false; }   // no progress: stalled
                bestEnd = end;
                at = c[c.Length - 1];
                waypoints.Add(at);
            }
            if (waypoints.Count == 0 || waypoints[waypoints.Count - 1] != to) return false;
            Vector3 prev = from;
            foreach (Vector3 w in waypoints)
            {
                if (!Leg(path, prev, w, out float len, out float _)) return false;
                length += len; prev = w;
            }
            bestEnd = 0f;
            return true;
        }

        private class Reached { public string name; public Vector3 at; public List<Vector3> route; public float length; }

        /// <summary>Plans every part from the pub: a chain from the pub, else a chain from the nearest part already reached
        /// (repeated until nothing new is reached, so the order of the list does not matter).</summary>
        private static List<Reached> PlanParts(Part[] parts, Vector3 pub, Vector3[] P, bool[] sampled, NavMeshPath path)
        {
            var reached = new List<Reached> { new Reached { name = "PUB", at = pub, route = new List<Vector3>(), length = 0f } };
            var done = new bool[parts.Length];
            var wp = new List<Vector3>();
            for (int pass = 0; pass < 4; pass++)
            {
                bool any = false;
                for (int i = 0; i < parts.Length; i++)
                {
                    if (done[i] || !sampled[i]) continue;
                    var order = new List<Reached>(reached);
                    Vector3 target = P[i];
                    order.Sort((r1, r2) => Hz(r1.at, target).CompareTo(Hz(r2.at, target)));
                    order.Remove(reached[0]); order.Insert(0, reached[0]);   // the pub first, then the nearest parts
                    foreach (Reached r in order)
                    {
                        if (!Chain(path, r.at, P[i], wp, out float len, out float _)) continue;
                        var route = new List<Vector3>(r.route); route.AddRange(wp);
                        reached.Add(new Reached { name = parts[i].name, at = P[i], route = route, length = r.length + len });
                        done[i] = true; any = true;
                        break;
                    }
                }
                if (!any) break;
            }
            return reached;
        }

        private IEnumerator Paths()
        {
            if (_manager != null) { foreach (EventActor a in _manager.ActiveActors) a.gameObject.SetActive(false); _manager.enabled = false; }
            if (_tpc != null) _tpc.enabled = false;
            TownBoundsConfig cfg = _fall != null && _fall.Config != null ? _fall.Config : null;
            Part[] parts = JsonUtility.FromJson<PartList>(ReadData("census/parts.json")).parts;
            GameObject spawn = GameObject.Find("SpawnPoint");
            Vector3 pubRaw = spawn != null ? spawn.transform.position : new Vector3(31.8f, 0.55f, 19f);
            if (!NavMesh.SamplePosition(pubRaw, out NavMeshHit pubHit, 1f, NavMesh.AllAreas)) { _log.AppendLine("RIG BROKEN: the pub is not on the NavMesh"); yield break; }
            Vector3 pub = pubHit.position;
            NavMeshTriangulation tri = NavMesh.CalculateTriangulation();
            _log.AppendLine($"pub (the drunk's spawn) {V(pubRaw)} → on mesh at {V(pub)}; NavMesh {tri.indices.Length / 3} triangles");
            var path = new NavMeshPath();

            var P = new Vector3[parts.Length]; var sampled = new bool[parts.Length];
            for (int i = 0; i < parts.Length; i++)
            {
                Vector3 t = new Vector3(parts[i].x, parts[i].y, parts[i].z);
                sampled[i] = NavMesh.SamplePosition(t, out NavMeshHit th, 2f, NavMesh.AllAreas);
                P[i] = sampled[i] ? th.position : t;
            }
            List<Reached> plan = PlanParts(parts, pub, P, sampled, path);
            yield return null;
            if (staging == "chains") { yield return WalkChains(plan); yield break; }
            var csv = new StringBuilder("part,target_x,target_y,target_z,sampled,single_status,single_length_m,single_end_m,single_pass,chain_pass,chain_legs,chain_length_m\n");
            var corners = new StringBuilder("path,leg,i,x,y,z\n");
            int single = 0, chained = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                bool sp = Leg(path, pub, P[i], out float slen, out float send);
                NavMeshPathStatus sst = path.status;
                if (sp) single++;
                Reached r = plan.Find(x => x.name == parts[i].name);
                if (r != null)
                {
                    chained++;
                    Vector3 prev = pub;
                    for (int k = 0; k < r.route.Count; k++)
                    {
                        Leg(path, prev, r.route[k], out float _, out float _);
                        Vector3[] c = path.corners;
                        for (int q = 0; q < c.Length; q++) corners.AppendLine($"{parts[i].name},{k + 1},{q},{F(c[q].x)},{F(c[q].y)},{F(c[q].z)}");
                        prev = r.route[k];
                    }
                }
                csv.AppendLine($"{parts[i].name},{F(P[i].x)},{F(P[i].y)},{F(P[i].z)},{(sampled[i] ? 1 : 0)},{sst},{F(slen)},{F(send)},{(sp ? 1 : 0)},{(r != null ? 1 : 0)},{(r != null ? r.route.Count : 0)},{(r != null ? F(r.length) : "")}");
                _log.AppendLine($"  {parts[i].name}: single query {(sp ? "COMPLETE" : $"PathPartial, {F(send, "0.0")} m short")}; " +
                                $"chain {(r != null ? $"COMPLETE in {r.route.Count} leg(s), {F(r.length, "0")} m" : "NONE")}");
            }
            _log.AppendLine($"PARTS: one query from the pub {single}/{parts.Length}; a chain of complete legs from the pub {chained}/{parts.Length}");
            File.WriteAllText(Path.Combine(outputDir, "paths.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(outputDir, "paths_corners.csv"), corners.ToString());

            // the 25 m coverage grid: one query from the pub, else a chain from the pub or from the 3 nearest parts reached
            var grid = new StringBuilder("x,z,inside,sampled,result,via,length_m,best_end_m,reason\n");
            int inside = 0, gsingle = 0, gchain = 0, gisland = 0, gnomesh = 0;
            var wp = new List<Vector3>();
            Vector4 b = cfg != null ? cfg.PlayableBounds2D() : new Vector4(-680f, -400f, 260f, 269.5f);
            for (float z = b.y + 12.5f; z < b.w; z += 25f)
            {
                for (float x = b.x + 12.5f; x < b.z; x += 25f)
                {
                    if (cfg != null && !cfg.IsAllowed(new Vector2(x, z))) continue;
                    inside++;
                    float gy = GroundY(new Vector3(x, 0f, z));
                    Vector3 q = new Vector3(x, float.IsNaN(gy) ? 0f : gy, z);
                    if (!NavMesh.SamplePosition(q, out NavMeshHit qh, 2f, NavMesh.AllAreas))
                    {
                        gnomesh++;
                        grid.AppendLine($"{F(x)},{F(z)},1,0,nomesh,,,,{Why(q).Replace(",", ";")}");
                        continue;
                    }
                    Vector3 here = qh.position;
                    string result = "", via = ""; float glen = 0f, bestEnd = float.PositiveInfinity;
                    if (Leg(path, pub, here, out glen, out float e0)) { result = "single"; gsingle++; via = "PUB"; bestEnd = 0f; }
                    else
                    {
                        bestEnd = e0;
                        var order = new List<Reached>(plan);
                        order.Sort((r1, r2) => Hz(r1.at, here).CompareTo(Hz(r2.at, here)));
                        order.Remove(plan[0]); order.Insert(0, plan[0]);   // the pub first, then the 3 nearest parts
                        for (int k = 0; k < Mathf.Min(4, order.Count) && result == ""; k++)
                        {
                            if (Chain(path, order[k].at, here, wp, out float cl, out float ce)) { result = "chain"; gchain++; via = order[k].name; glen = order[k].length + cl; }
                            else bestEnd = Mathf.Min(bestEnd, ce);
                        }
                        if (result == "") { result = "island"; gisland++; }
                    }
                    string reason = result == "island" ? $"no chain from the pub or the 3 nearest parts; closest approach {F(bestEnd, "0.0")} m; {Ground(q).Replace(",", ";")}" : "";
                    grid.AppendLine($"{F(x)},{F(z)},1,1,{result},{via},{F(glen)},{F(bestEnd)},{reason}");
                }
                yield return null;
            }
            int gs = inside - gnomesh;
            _log.AppendLine($"GRID 25 m: {inside} points inside the playable area; {gnomesh} have no NavMesh within 2 m; of the {gs} on the mesh: " +
                            $"{gsingle} one query from the pub ({F(100f * gsingle / Mathf.Max(1, gs), "0.0")} %), {gchain} a chain of complete legs ({F(100f * gchain / Mathf.Max(1, gs), "0.0")} %), " +
                            $"{gisland} unreached ({F(100f * gisland / Mathf.Max(1, gs), "0.0")} %)");
            File.WriteAllText(Path.Combine(outputDir, "grid.csv"), grid.ToString());

            // REAL agents: one per part, from the pub, given the part as its ONLY destination (as an event's SetDestination
            // is), all at once with avoidance off. Arrival = the agent's own test AND within 0.5 m of the part.
            var walkers = new List<NavMeshAgent>(); var arrivedAt = new List<float>(); var offMesh = new List<bool>(); var firstStatus = new List<string>();
            float longest = 0f;
            for (int i = 0; i < parts.Length; i++)
            {
                var go = new GameObject($"PathAgent {parts[i].name} (rig)");
                go.transform.position = pub;
                NavMeshAgent ag = go.AddComponent<NavMeshAgent>();
                ag.agentTypeID = 0; ag.radius = 0.3f; ag.height = 1.77f; ag.speed = 3.5f; ag.acceleration = 20f; ag.angularSpeed = 720f;
                ag.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                walkers.Add(ag); arrivedAt.Add(-1f); offMesh.Add(false); firstStatus.Add("");
                Reached r = plan.Find(x => x.name == parts[i].name);
                longest = Mathf.Max(longest, r != null ? r.length : Hz(pub, P[i]) * 1.5f);
            }
            yield return null;
            for (int i = 0; i < parts.Length; i++) walkers[i].SetDestination(P[i]);
            var atrace = new StringBuilder("agent,t,x,y,z,path_status,remaining\n");
            float tt = 0f, limit = longest / 3.5f * 1.5f + 30f;
            while (tt < limit)
            {
                yield return null; tt += Time.deltaTime;
                bool all = true, tick = Mathf.FloorToInt(tt) != Mathf.FloorToInt(tt - Time.deltaTime);
                for (int i = 0; i < walkers.Count; i++)
                {
                    NavMeshAgent ag = walkers[i];
                    if (arrivedAt[i] >= 0f) continue;
                    all = false;
                    if (!ag.isOnNavMesh) offMesh[i] = true;
                    if (firstStatus[i] == "" && !ag.pathPending && ag.hasPath) firstStatus[i] = ag.pathStatus.ToString();
                    if (tick) atrace.AppendLine($"{parts[i].name},{F(tt)},{F(ag.transform.position.x)},{F(ag.transform.position.y)},{F(ag.transform.position.z)},{ag.pathStatus},{F(ag.remainingDistance)}");
                    // the agent's own test (remaining ≤ stopping distance) at the part; hasPath is NOT required — an agent standing
                    // on its destination can report no path (measured: the hill_northwest agent, 2026-11-11 06:10 run)
                    if (!ag.pathPending && ag.remainingDistance <= ag.stoppingDistance + 0.05f && Hz(ag.transform.position, P[i]) <= 0.5f) arrivedAt[i] = tt;
                }
                if (all) break;
            }
            var agents = new StringBuilder("agent,first_path_status,arrived,seconds,end_x,end_z,end_to_part_m,on_mesh_every_frame\n");
            int arrivedN = 0;
            for (int i = 0; i < walkers.Count; i++)
            {
                NavMeshAgent ag = walkers[i];
                float toGoal = Hz(ag.transform.position, P[i]);
                bool arrived = arrivedAt[i] >= 0f;
                if (arrived) arrivedN++;
                atrace.AppendLine($"{parts[i].name},{F(arrived ? arrivedAt[i] : tt)},{F(ag.transform.position.x)},{F(ag.transform.position.y)},{F(ag.transform.position.z)},{ag.pathStatus},{F(ag.remainingDistance)}");
                agents.AppendLine($"{parts[i].name},{firstStatus[i]},{(arrived ? 1 : 0)},{F(arrived ? arrivedAt[i] : tt)},{F(ag.transform.position.x)},{F(ag.transform.position.z)},{F(toGoal)},{(offMesh[i] ? 0 : 1)}");
                _log.AppendLine($"AGENT → {parts[i].name}: first path {firstStatus[i]}; arrived {arrived} in {F(arrived ? arrivedAt[i] : tt)} s; ends {F(toGoal, "0.0")} m from the part; on mesh every frame {!offMesh[i]}");
                Destroy(ag.gameObject);
            }
            _log.AppendLine($"AGENTS: {arrivedN}/{parts.Length} real agents given ONLY the part as destination arrived (the agent's own test and within 0.5 m)");
            File.WriteAllText(Path.Combine(outputDir, "agents.csv"), agents.ToString());
            File.WriteAllText(Path.Combine(outputDir, "agents_trace.csv"), atrace.ToString());
        }

        /// <summary>Real agents walk the three longest chains WAYPOINT BY WAYPOINT, as an event walks its route (ADR-3):
        /// each leg's arrival by the agent's own test at the waypoint. 7 m/s only to keep the run short.</summary>
        private IEnumerator WalkChains(List<Reached> plan)
        {
            var longest = new List<Reached>(plan);
            longest.RemoveAt(0);   // the pub itself
            longest.Sort((a, b) => b.length.CompareTo(a.length));
            if (longest.Count > 3) longest.RemoveRange(3, longest.Count - 3);
            var walkers = new List<NavMeshAgent>(); var legAt = new List<int>(); var arrivedAt = new List<float>(); var offMesh = new List<bool>();
            foreach (Reached r in longest)
            {
                var go = new GameObject($"ChainAgent {r.name} (rig)");
                go.transform.position = plan[0].at;
                NavMeshAgent ag = go.AddComponent<NavMeshAgent>();
                ag.agentTypeID = 0; ag.radius = 0.3f; ag.height = 1.77f; ag.speed = 7f; ag.acceleration = 40f; ag.angularSpeed = 720f;
                ag.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                walkers.Add(ag); legAt.Add(0); arrivedAt.Add(-1f); offMesh.Add(false);
            }
            yield return null;
            for (int k = 0; k < walkers.Count; k++) walkers[k].SetDestination(longest[k].route[0]);
            var legs = new StringBuilder("chain,leg,of,waypoint_x,waypoint_z,arrived_s,path_status\n");
            var trace = new StringBuilder("agent,t,x,y,z,leg,path_status\n");
            float tt = 0f, limit = (longest.Count > 0 ? longest[0].length : 300f) / 7f * 2f + 30f;
            while (tt < limit)
            {
                yield return null; tt += Time.deltaTime;
                bool all = true, tick = Mathf.FloorToInt(tt) != Mathf.FloorToInt(tt - Time.deltaTime);
                for (int k = 0; k < walkers.Count; k++)
                {
                    if (arrivedAt[k] >= 0f) continue;
                    all = false;
                    NavMeshAgent ag = walkers[k]; Reached r = longest[k];
                    if (!ag.isOnNavMesh) offMesh[k] = true;
                    if (tick) trace.AppendLine($"{r.name},{F(tt)},{F(ag.transform.position.x)},{F(ag.transform.position.y)},{F(ag.transform.position.z)},{legAt[k] + 1},{ag.pathStatus}");
                    Vector3 w = r.route[legAt[k]];
                    if (ag.pathPending || ag.remainingDistance > ag.stoppingDistance + 0.05f || Hz(ag.transform.position, w) > 0.5f) continue;
                    legs.AppendLine($"{r.name},{legAt[k] + 1},{r.route.Count},{F(w.x)},{F(w.z)},{F(tt)},{ag.pathStatus}");
                    if (legAt[k] + 1 < r.route.Count) { legAt[k]++; ag.SetDestination(r.route[legAt[k]]); }
                    else arrivedAt[k] = tt;
                }
                if (all) break;
            }
            for (int k = 0; k < walkers.Count; k++)
            {
                NavMeshAgent ag = walkers[k]; Reached r = longest[k];
                _log.AppendLine($"CHAIN AGENT → {r.name} ({r.route.Count} waypoints, {F(r.length, "0")} m): arrived {arrivedAt[k] >= 0f} in {F(arrivedAt[k] >= 0f ? arrivedAt[k] : tt)} s, " +
                                $"ends {F(Hz(ag.transform.position, r.at), "0.0")} m from the part, on mesh every frame {!offMesh[k]}");
                Destroy(ag.gameObject);
            }
            File.WriteAllText(Path.Combine(outputDir, "chain_legs.csv"), legs.ToString());
            File.WriteAllText(Path.Combine(outputDir, "chain_trace.csv"), trace.ToString());
        }

        /// <summary>What a point stands on: the top collider, its height and slope.</summary>
        private string Ground(Vector3 q)
        {
            int n = Physics.RaycastNonAlloc(new Vector3(q.x, 400f, q.z), Vector3.down, _hitBuf, 900f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity; RaycastHit top = default;
            for (int i = 0; i < n; i++) if (!IsMine(_hitBuf[i].collider) && _hitBuf[i].point.y > best) { best = _hitBuf[i].point.y; top = _hitBuf[i]; }
            if (float.IsNegativeInfinity(best)) return "no ground";
            return $"top is {top.collider.name} at y {F(best)}, slope {F(Vector3.Angle(top.normal, Vector3.up), "0")}°";
        }

        /// <summary>Why a grid point has no NavMesh within 2 m: what is there.</summary>
        private string Why(Vector3 q)
        {
            int n = Physics.RaycastNonAlloc(new Vector3(q.x, 400f, q.z), Vector3.down, _hitBuf, 900f, ~0, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity; RaycastHit top = default;
            for (int i = 0; i < n; i++) if (!IsMine(_hitBuf[i].collider) && _hitBuf[i].point.y > best) { best = _hitBuf[i].point.y; top = _hitBuf[i]; }
            if (float.IsNegativeInfinity(best)) return "no ground at all: a hole";
            float slope = Vector3.Angle(top.normal, Vector3.up);
            return $"no NavMesh within 2 m; top is {top.collider.name} at y {F(best)}, slope {F(slope, "0")}°" + (slope > 45f ? " (too steep)" : "");
        }

        // ---------------------------------------------------------------------------------------------
        // warp — the Warp guard, provoked on purpose
        // ---------------------------------------------------------------------------------------------

        private IEnumerator WarpGuard()
        {
            _log.Insert(0, "⚠️ PROVOKED ON PURPOSE: the drunk's spawn point is staged off the NavMesh (over the hole at x −380, " +
                           "z −229). Expected: ONE '[Events] … off the NavMesh … (said once)' warning, the actor performing every " +
                           "lifecycle in place at the spawn point, never at a previous despawn point. No other warning or error.\n");
            if (_tpc != null) _tpc.enabled = false;
            if (_fall != null) _fall.enabled = false;
            int warnings = 0, errors = 0; var msgs = new StringBuilder();
            Application.LogCallback cb = (m, st, type) =>
            {
                if (type == LogType.Warning) { warnings++; msgs.AppendLine("  WARNING " + m); }
                else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { errors++; msgs.AppendLine("  ERROR " + m); }
            };
            Application.logMessageReceived += cb;
            var csv = new StringBuilder("t,actor,instance,x,y,z,agent_enabled,on_mesh\n");
            float t = 0f;
            var seen = new HashSet<int>();
            while (t < 80f)
            {
                yield return null; t += Time.deltaTime;
                if (_manager == null || _manager.ActiveActors.Count == 0) continue;
                EventActor a = _manager.ActiveActors[0];
                var ag = a.GetComponent<NavMeshAgent>();
                seen.Add(a.GetInstanceID());
                if (Mathf.FloorToInt(t * 2f) != Mathf.FloorToInt((t - Time.deltaTime) * 2f))
                    csv.AppendLine($"{F(t)},{a.name},{a.GetInstanceID()},{F(a.transform.position.x)},{F(a.transform.position.y)},{F(a.transform.position.z)},{(ag != null && ag.enabled ? 1 : 0)},{(ag != null && ag.enabled && ag.isOnNavMesh ? 1 : 0)}");
            }
            Application.logMessageReceived -= cb;
            File.WriteAllText(Path.Combine(outputDir, "warp-trace.csv"), csv.ToString());
            _log.AppendLine($"80 s observed, {seen.Count} pooled instance(s); console during the run: {warnings} warning(s), {errors} error(s)\n{msgs}");
        }
    }
}
#endif
