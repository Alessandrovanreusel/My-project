#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using CameraGame.Core;

namespace CameraGame.Events
{
    /// <summary>
    /// Watches a real event actor walk its real route in the real town, and writes what the
    /// NavMeshAgent actually did.
    ///
    /// ⚠️ WHY THIS EXISTS SEPARATELY FROM THE PHOTO-SHOOT RIG. The photo-shoot rig answers
    /// "does the grader see him"; it builds a private world with NO EventRoute, so its actor
    /// performs the whole lifecycle in place — correctly, by design. The placement study runs
    /// in the town but finishes in ~2.7s, entirely inside the 3s Spawn phase, which also stops
    /// in place by design. Neither ever reaches BUILD, the only phase whose PhaseConfig sets
    /// advanceAlongRoute = 1. So neither can answer "does he actually walk his route", and a
    /// video of either shows a stationary drunk that looks exactly like a stalled agent.
    ///
    /// This runs long enough to cover spawn(3s) + build(10s) + peak(1.5s) + windDown(6s) and
    /// logs the agent every frame, so BUILD can be judged on its own.
    ///
    /// Runtime assembly behind #if UNITY_EDITOR, never an editor assembly: Unity cannot resolve
    /// an editor-assembly MonoBehaviour on a scene object when entering play mode, and the rig
    /// then silently does nothing (CLAUDE.md §Traps).
    /// </summary>
    public class RouteTraceRunner : MonoBehaviour
    {
        [HideInInspector] public EventManager manager;
        [HideInInspector] public string outputDir = "_bmad-output/verification/route-trace";
        [HideInInspector] public float seconds = 30f;

        private readonly StringBuilder _csv = new StringBuilder();
        private bool _prevRunInBackground;

        private IEnumerator Start()
        {
            _prevRunInBackground = Application.runInBackground;
            Application.runInBackground = true;

            try
            {
                yield return Trace();
            }
            finally
            {
                Application.runInBackground = _prevRunInBackground;
                Write();
                if (EditorApplication.isPlaying) EditorApplication.ExitPlaymode();
            }
        }

        private IEnumerator Trace()
        {
            Directory.CreateDirectory(outputDir);
            _csv.AppendLine("time,count,phase,x,y,z,step_m,agent_speed,desired_speed,is_stopped," +
                            "has_path,remaining,on_navmesh,agent_enabled,at_peak,time_to_peak");

            float t0 = Time.time;
            Vector3 prev = Vector3.zero;
            bool first = true;

            while (Time.time - t0 < seconds)
            {
                int count = manager != null ? manager.ActiveActors.Count : -1;
                EventActor a = count > 0 ? manager.ActiveActors[0] : null;

                if (a == null || !a.isActiveAndEnabled)
                {
                    _csv.AppendLine($"{Time.time - t0:0.###},{count},none,,,,,,,,,,,,,");
                }
                else
                {
                    Vector3 p = a.transform.position;
                    var ag = a.GetComponent<NavMeshAgent>();
                    float step = first ? 0f : Vector3.Distance(p, prev);

                    // The actor's phase is not public, so derive it from the peak clock: the
                    // definition's own durations are the authority on which phase we are in.
                    string phase = a.IsAtPeak ? "Peak" : (a.TimeToPeak > 0f ? "PrePeak" : "PostPeak");

                    float rem = ag != null && ag.hasPath ? ag.remainingDistance : -1f;
                    if (float.IsInfinity(rem)) rem = -2f;

                    _csv.AppendLine(
                        $"{Time.time - t0:0.###},{count},{phase}," +
                        $"{p.x:0.###},{p.y:0.###},{p.z:0.###},{step:0.#####}," +
                        $"{(ag != null ? ag.velocity.magnitude : -1f):0.###}," +
                        $"{(ag != null ? ag.desiredVelocity.magnitude : -1f):0.###}," +
                        $"{(ag != null && ag.isStopped ? 1 : 0)}," +
                        $"{(ag != null && ag.hasPath ? 1 : 0)},{rem:0.###}," +
                        $"{(ag != null && ag.isOnNavMesh ? 1 : 0)}," +
                        $"{(ag != null && ag.enabled ? 1 : 0)}," +
                        $"{(a.IsAtPeak ? 1 : 0)},{a.TimeToPeak:0.###}");

                    prev = p;
                    first = false;
                }

                yield return null;
            }
        }

        private void Write()
        {
            try
            {
                File.WriteAllText(Path.Combine(outputDir, "route-trace.csv"), _csv.ToString());
                Debug.Log($"[RouteTrace] wrote {outputDir}/route-trace.csv");
            }
            catch (IOException e)
            {
                Debug.LogError($"[RouteTrace] could not write: {e.Message}");
            }
        }
    }
}
#endif
