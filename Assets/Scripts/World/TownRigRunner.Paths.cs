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
    ///   paths — from the pub (the drunk's spawn point) to every part in census/parts.json (PathComplete AND the last
    ///           corner within 0.5 m of the target), a 25 m coverage grid over the playable area (each failure probed for
    ///           why), and three REAL NavMeshAgents (agent type 0, 3.5 m/s) walked to arrival on the three longest paths,
    ///           arrival judged by the agent's own test (remaining ≤ stopping distance; lesson 68).
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

        private IEnumerator Paths()
        {
            if (_manager != null) { foreach (EventActor a in _manager.ActiveActors) a.gameObject.SetActive(false); _manager.enabled = false; }
            TownBoundsConfig cfg = _fall != null && _fall.Config != null ? _fall.Config : null;
            Part[] parts = JsonUtility.FromJson<PartList>(ReadData("census/parts.json")).parts;
            GameObject spawn = GameObject.Find("SpawnPoint");
            Vector3 pubRaw = spawn != null ? spawn.transform.position : new Vector3(31.8f, 0.55f, 19f);
            if (!NavMesh.SamplePosition(pubRaw, out NavMeshHit pubHit, 1f, NavMesh.AllAreas)) { _log.AppendLine("RIG BROKEN: the pub is not on the NavMesh"); yield break; }
            Vector3 pub = pubHit.position;
            _log.AppendLine($"pub (the drunk's spawn) {V(pubRaw)} → on mesh at {V(pub)}");
            var path = new NavMeshPath();
            var csv = new StringBuilder("part,target_x,target_y,target_z,sampled,status,corners,length_m,end_to_target_m,pass\n");
            var corners = new StringBuilder("path,i,x,y,z\n");
            var lengths = new List<KeyValuePair<float, Vector3>>();
            int pass = 0;
            foreach (Part p in parts)
            {
                Vector3 t = new Vector3(p.x, p.y, p.z);
                bool sampled = NavMesh.SamplePosition(t, out NavMeshHit th, 2f, NavMesh.AllAreas);
                Vector3 target = sampled ? th.position : t;
                bool ok = NavMesh.CalculatePath(pub, target, NavMesh.AllAreas, path);
                float len = 0f; Vector3[] c = path.corners;
                for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]);
                float end = c.Length > 0 ? Vector3.Distance(c[c.Length - 1], target) : float.PositiveInfinity;
                bool good = sampled && ok && path.status == NavMeshPathStatus.PathComplete && end <= 0.5f;
                if (good) { pass++; lengths.Add(new KeyValuePair<float, Vector3>(len, target)); }
                for (int i = 0; i < c.Length; i++) corners.AppendLine($"{p.name},{i},{F(c[i].x)},{F(c[i].y)},{F(c[i].z)}");
                csv.AppendLine($"{p.name},{F(target.x)},{F(target.y)},{F(target.z)},{(sampled ? 1 : 0)},{path.status},{c.Length},{F(len)},{F(end)},{(good ? 1 : 0)}");
            }
            _log.AppendLine($"PARTS: {pass}/{parts.Length} paths from the pub complete with the end within 0.5 m");
            File.WriteAllText(Path.Combine(outputDir, "paths.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(outputDir, "paths_corners.csv"), corners.ToString());

            // the 25 m coverage grid
            var grid = new StringBuilder("x,z,inside,sampled,status,length_m,reason\n");
            int inside = 0, gpass = 0;
            Vector4 b = cfg != null ? cfg.PlayableBounds2D() : new Vector4(-680f, -400f, 260f, 269.5f);
            for (float z = b.y + 12.5f; z < b.w; z += 25f)
            {
                for (float x = b.x + 12.5f; x < b.z; x += 25f)
                {
                    var xz = new Vector2(x, z);
                    bool ins = cfg != null ? cfg.IsAllowed(xz) : true;
                    if (!ins) continue;
                    inside++;
                    float gy = GroundY(new Vector3(x, 0f, z));
                    Vector3 q = new Vector3(x, float.IsNaN(gy) ? 0f : gy, z);
                    bool s = NavMesh.SamplePosition(q, out NavMeshHit qh, 2f, NavMesh.AllAreas);
                    string reason = "";
                    NavMeshPathStatus st = NavMeshPathStatus.PathInvalid; float len = 0f;
                    if (s)
                    {
                        NavMesh.CalculatePath(pub, qh.position, NavMesh.AllAreas, path);
                        st = path.status;
                        Vector3[] c = path.corners;
                        for (int i = 1; i < c.Length; i++) len += Vector3.Distance(c[i - 1], c[i]);
                        float end = c.Length > 0 ? Vector3.Distance(c[c.Length - 1], qh.position) : 99f;
                        if (st == NavMeshPathStatus.PathComplete && end <= 0.5f) gpass++;
                        else reason = $"path {st}, end {F(end)} m short: an island";
                    }
                    else reason = Why(q);
                    grid.AppendLine($"{F(x)},{F(z)},1,{(s ? 1 : 0)},{st},{F(len)},{reason.Replace(",", ";")}");
                }
                yield return null;
            }
            _log.AppendLine($"GRID 25 m: {gpass}/{inside} points inside the playable area path from the pub ({F(100f * gpass / Mathf.Max(1, inside), "0.0")} %)");
            File.WriteAllText(Path.Combine(outputDir, "grid.csv"), grid.ToString());

            // three real agents on the three longest paths
            lengths.Sort((p1, p2) => p2.Key.CompareTo(p1.Key));
            var agents = new StringBuilder("agent,target_x,target_z,path_m,arrived,seconds,remaining_at_end,on_mesh_every_frame\n");
            for (int k = 0; k < Mathf.Min(3, lengths.Count); k++)
            {
                var go = new GameObject($"PathAgent {k} (rig)");
                go.transform.position = pub;
                NavMeshAgent ag = go.AddComponent<NavMeshAgent>();
                ag.agentTypeID = 0; ag.radius = 0.3f; ag.height = 1.77f; ag.speed = 3.5f; ag.acceleration = 20f; ag.angularSpeed = 720f;
                yield return null;
                ag.SetDestination(lengths[k].Value);
                float t = 0f, limit = lengths[k].Key / 3.5f * 2f + 30f; bool arrived = false, onMesh = true;
                while (t < limit)
                {
                    yield return null; t += Time.deltaTime;
                    if (!ag.isOnNavMesh) onMesh = false;
                    if (!ag.pathPending && ag.hasPath && ag.remainingDistance <= ag.stoppingDistance + 0.05f) { arrived = true; break; }
                    if (!ag.pathPending && !ag.hasPath && t > 2f) break;
                }
                agents.AppendLine($"{k},{F(lengths[k].Value.x)},{F(lengths[k].Value.z)},{F(lengths[k].Key)},{(arrived ? 1 : 0)},{F(t)},{F(ag.hasPath ? ag.remainingDistance : -1f)},{(onMesh ? 1 : 0)}");
                _log.AppendLine($"AGENT {k}: {F(lengths[k].Key)} m path → arrived {arrived} in {F(t)} s (agent's own test), on mesh every frame {onMesh}");
                Destroy(go);
            }
            File.WriteAllText(Path.Combine(outputDir, "agents.csv"), agents.ToString());
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
