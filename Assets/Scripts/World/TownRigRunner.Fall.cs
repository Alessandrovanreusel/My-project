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
    /// Story 2.2 AC3 rig modes (Task 6.2 / 6.3). EVERY FallRecovery warning these modes produce is PROVOKED ON PURPOSE.
    ///   fall     — four ways out of the world, five cycles each, back to back (bugs hide in the second cycle):
    ///                under_plain  the player is placed with the feet 1 m below the plain's surface;
    ///                hole         the player WALKS into the real hole in the plain at (−379, −224);
    ///                buried       the player is placed at y −58 under (−20, −40), where the buried street block was;
    ///                edge         the player WALKS west off the edge with the TownBounds barrier 'Edge 07' disabled.
    ///              Per cycle: from the instant the feet go below the surface (or out of the playable area) to the restore
    ///              frame; then grounded within 0.5 s, on the NavMesh, capsule clear, no falling speed carried over, and the
    ///              screen after the restore (ScreenCapture after WaitForEndOfFrame, which sees overlay UI) against a frame
    ///              before it. One clip of the first hole cycle.
    ///   failsoft — the config is staged missing or invalid (TownRig.Stage). The game must warn ONCE (in Awake, before this
    ///              runs: the rig reads that line from Editor.log afterwards), walk normally without a single recovery,
    ///              and still put the player back from the hole on its safe defaults.
    /// </summary>
    public partial class TownRigRunner
    {
        partial void ExtraModes4(ref IEnumerator routine)
        {
            if (mode == "fall") routine = Fall();
            else if (mode == "failsoft") routine = FailSoft();
            else ExtraModes5(ref routine);
        }

        partial void ExtraModes5(ref IEnumerator routine);

        private static readonly Vector3 HoleCentre = new Vector3(-379f, 0f, -224.5f);

        private int _warnWorld, _warnOther, _errors;
        private readonly StringBuilder _msgs = new StringBuilder();

        private void CountLogs(string m, string st, LogType type)
        {
            if (type == LogType.Warning) { if (m.Contains("[World]")) _warnWorld++; else { _warnOther++; _msgs.AppendLine("  OTHER WARNING " + m); } }
            else if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) { _errors++; _msgs.AppendLine("  ERROR " + m); }
        }

        private IEnumerator Fall()
        {
            _log.Insert(0, "⚠️ PROVOKED ON PURPOSE: every '[World] The player fell out of the world …' warning in this run is a provoked " +
                           "FallRecovery case (4 cases × 5 cycles = 20 expected). Any OTHER warning or error is a finding.\n");
            if (_fall == null) { _log.AppendLine("RIG BROKEN: no FallRecovery on the player"); yield break; }
            if (!_fall.enabled) { _log.AppendLine("RIG BROKEN: FallRecovery is disabled"); yield break; }
            if (_manager != null) { foreach (EventActor ea in _manager.ActiveActors) ea.gameObject.SetActive(false); _manager.enabled = false; }
            Application.logMessageReceived += CountLogs;
            Directory.CreateDirectory(Path.Combine(outputDir, "frames"));
            Directory.CreateDirectory(Path.Combine(outputDir, "clip"));
            float t0 = 0f;
            while (!_cc.isGrounded && t0 < 8f) { t0 += Time.deltaTime; yield return null; }
            yield return DriveControl();

            var csv = new StringBuilder("case,cycle,below_at_s,airborne_at_s,restored_at_s,seconds_below_to_restore,seconds_airborne_to_restore,reason,from_x,from_y,from_z," +
                                        "restore_x,restore_y,restore_z,restore_age_s,grounded_after_s,on_navmesh,capsule_clear,overlaps,vy_after_mps,vy_before_mps,inside_after,frame_std,ui_after\n");
            var trace = new StringBuilder("case,cycle,t,x,y,z,grounded,inside\n");
            TownBoundsConfig cfg = _fall.Config;
            GameObject edge07 = GameObject.Find("TownBounds/Edge 07");
            _log.AppendLine($"TownBounds/Edge 07 found: {edge07 != null}; belowWorldY {(cfg != null ? cfg.SafeBelowWorldY : TownBoundsConfig.DefaultBelowWorldY)}");
            string[] cases = { "under_plain", "hole", "buried", "edge" };
            float worst = 0f; int failures = 0;
            foreach (string cs in cases)
            {
                Vector3 start = cs == "under_plain" ? new Vector3(60f, 0f, 60f)
                              : cs == "hole" ? new Vector3(-379f, 0f, -244f)
                              : cs == "buried" ? new Vector3(-20f, 0f, -55f)
                              : new Vector3(-655f, 0f, -65f);
                float yaw = cs == "hole" ? 0f : cs == "edge" ? 270f : 90f;
                if (cs == "edge" && edge07 != null) edge07.SetActive(false);
                Place(start, yaw);
                Keys();
                yield return Settle(0.8f);
                for (int cycle = 0; cycle < 5; cycle++)
                {
                    bool clip = cs == "hole" && cycle == 0;
                    int before = _fall.Recoveries;
                    var hist = new List<KeyValuePair<float, Vector3>>();
                    // a normal frame before anything happens (for the "nothing shown" comparison)
                    yield return new WaitForEndOfFrame();
                    int uiBefore = ActiveCanvases();
                    if (cycle == 0) CaptureScreen(Path.Combine(outputDir, "frames", $"{cs}_normal.jpg"));
                    float t = 0f, belowAt = -1f, airAt = -1f, lipY = float.NaN, vyBefore = 0f;
                    Vector3 prevP = _player.transform.position; int frameNo = 0;
                    var clipTimes = new StringBuilder("frame,t\n");
                    // walk toward the case's target (re-aimed every cycle: the restore may leave the player elsewhere)
                    float walkFor = cs == "under_plain" || cs == "buried" ? 1.5f : 30f;
                    float aim = cs == "hole" ? YawTo(_player.transform.position, HoleCentre) : cs == "edge" ? 270f : (cycle % 2 == 0 ? 90f : 270f);
                    Keys(Key.W);
                    while (t < walkFor + 6f)
                    {
                        if (_fall.Recoveries == before) _player.transform.rotation = Quaternion.Euler(0f, aim, 0f);   // the mouse's job
                        if (clip) yield return new WaitForEndOfFrame(); else yield return null;
                        t += Time.deltaTime;
                        Vector3 p = _player.transform.position;
                        hist.Add(new KeyValuePair<float, Vector3>(Time.time, p));
                        float vy = (p.y - prevP.y) / Mathf.Max(1e-5f, Time.deltaTime); prevP = p;
                        bool ins = cfg == null || cfg.IsAllowed(new Vector2(p.x, p.z));
                        trace.AppendLine($"{cs},{cycle},{F(Time.time)},{F(p.x)},{F(p.y)},{F(p.z)},{(_cc.isGrounded ? 1 : 0)},{(ins ? 1 : 0)}");
                        if (clip && frameNo < 400) { CaptureScreen(Path.Combine(outputDir, "clip", $"f{frameNo:0000}.jpg")); clipTimes.AppendLine($"{frameNo},{F(Time.time, "0.0000")}"); frameNo++; }
                        if (_fall.Recoveries > before) break;
                        if (_cc.isGrounded) lipY = p.y;
                        else if (airAt < 0f) airAt = Time.time;
                        vyBefore = vy;
                        // the provocation
                        if (t >= walkFor && belowAt < 0f && (cs == "under_plain" || cs == "buried"))
                        {
                            Keys();
                            float gy = GroundY(p);
                            Vector3 to = cs == "under_plain" ? new Vector3(p.x, gy - 1f, p.z) : new Vector3(-20f, -58f, -40f);
                            _cc.enabled = false; _player.transform.position = to; _cc.enabled = true; Physics.SyncTransforms();
                            belowAt = Time.time;   // the feet are below the surface from this frame on
                        }
                        if (belowAt < 0f && cs == "hole" && !float.IsNaN(lipY) && p.y < lipY - 0.3f) { belowAt = Time.time; Keys(); }
                        if (belowAt < 0f && cs == "edge" && !ins) { belowAt = Time.time; Keys(); }
                    }
                    Keys();
                    float restoredAt = _fall.Recoveries > before ? _fall.LastRecoveryTime : -1f;
                    Vector3 rp = _player.transform.position;
                    if (clip) File.WriteAllText(Path.Combine(outputDir, "clip", "times.csv"), clipTimes.ToString());
                    // how old the restore spot is: the last time the player stood within 0.1 m of it before the fall
                    float age = -1f;
                    for (int k = hist.Count - 2; k >= 0; k--)
                        if (Hz(hist[k].Value, _fall.LastTo) < 0.1f && Mathf.Abs(hist[k].Value.y - _fall.LastTo.y) < 0.3f) { age = restoredAt - hist[k].Key; break; }
                    // after the restore: overlaps now, falling speed over the next frames, grounded within 0.5 s, NavMesh
                    int overlaps = Overlaps(out string overlapNames);
                    bool onMesh = NavMesh.SamplePosition(rp, out NavMeshHit _, 0.5f, NavMesh.AllAreas);
                    float tg = 0f, vyAfter = 0f, groundedAfter = -1f; Vector3 q0 = rp;
                    while (tg < 1.0f)
                    {
                        if (clip && frameNo < 400) yield return new WaitForEndOfFrame(); else yield return null;
                        tg += Time.deltaTime;
                        Vector3 q = _player.transform.position;
                        if (tg < 0.12f) vyAfter = Mathf.Min(vyAfter, (q.y - q0.y) / Mathf.Max(1e-5f, Time.deltaTime));
                        q0 = q;
                        if (groundedAfter < 0f && _cc.isGrounded) groundedAfter = tg;
                        if (clip && frameNo < 400) { CaptureScreen(Path.Combine(outputDir, "clip", $"f{frameNo:0000}.jpg")); clipTimes.AppendLine($"{frameNo},{F(Time.time, "0.0000")}"); frameNo++; }
                        trace.AppendLine($"{cs},{cycle},{F(Time.time)},{F(q.x)},{F(q.y)},{F(q.z)},{(_cc.isGrounded ? 1 : 0)},1");
                    }
                    if (clip) File.WriteAllText(Path.Combine(outputDir, "clip", "times.csv"), clipTimes.ToString());
                    // the settled frame ~1 s after the restore, through the screen (sees overlay UI)
                    yield return new WaitForEndOfFrame();
                    int uiAfter = ActiveCanvases();
                    float std = CaptureScreen(Path.Combine(outputDir, "frames", $"{cs}_c{cycle}_after.jpg"));
                    bool insideAfter = cfg == null || cfg.IsAllowed(new Vector2(rp.x, rp.z));
                    float secs = restoredAt >= 0f && belowAt >= 0f ? restoredAt - belowAt : float.NaN;
                    float secsAir = restoredAt >= 0f && airAt >= 0f ? restoredAt - airAt : float.NaN;
                    bool ok = restoredAt >= 0f && secs <= 2f && groundedAfter >= 0f && groundedAfter <= 0.5f && onMesh && overlaps == 0 && insideAfter && uiAfter == uiBefore;
                    if (!ok) failures++;
                    if (!float.IsNaN(secs)) worst = Mathf.Max(worst, secs);
                    csv.AppendLine($"{cs},{cycle},{F(belowAt)},{F(airAt)},{F(restoredAt)},{F(secs, "0.000")},{F(secsAir, "0.000")},{(_fall.LastReason ?? "").Replace(",", ";")}," +
                                   $"{F(_fall.LastFrom.x)},{F(_fall.LastFrom.y)},{F(_fall.LastFrom.z)},{F(rp.x)},{F(rp.y)},{F(rp.z)},{F(age)},{F(groundedAfter)},{(onMesh ? 1 : 0)}," +
                                   $"{(overlaps == 0 ? 1 : 0)},{overlapNames.Replace(",", " ")},{F(vyAfter)},{F(vyBefore)},{(insideAfter ? 1 : 0)},{F(std, "0.0")},{uiAfter - uiBefore}");
                    _log.AppendLine($"{cs} cycle {cycle}: {(restoredAt >= 0f ? $"restored {F(secs, "0.000")} s after going below/out ({F(secsAir, "0.000")} s after leaving the ground), reason '{_fall.LastReason}'" : "NOT RESTORED")}; " +
                                    $"to {V(rp)} (spot {F(age)} s old), grounded after {F(groundedAfter)} s, on mesh {onMesh}, overlaps {overlaps} {overlapNames}, vy after {F(vyAfter)} (before {F(vyBefore)}) m/s, " +
                                    $"new UI canvases {uiAfter - uiBefore}{(ok ? "" : "  ← FAIL")}");
                }
                if (cs == "edge" && edge07 != null) edge07.SetActive(true);
            }
            Application.logMessageReceived -= CountLogs;
            _log.AppendLine($"FALL: worst below→restore {F(worst, "0.000")} s; cycles failing a check {failures}/20; console during the cases: {_warnWorld} provoked [World] warning(s) (20 expected), " +
                            $"{_warnOther} other warning(s), {_errors} error(s)\n{_msgs}");
            File.WriteAllText(Path.Combine(outputDir, "fall.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(outputDir, "trace.csv"), trace.ToString());
        }

        private IEnumerator FailSoft()
        {
            _log.Insert(0, $"⚠️ PROVOKED ON PURPOSE: the TownBoundsConfig is staged '{staging}'. Expected: ONE '[World]' config warning at boot " +
                           "(read from Editor.log by the caller), NO recovery while walking normally, and ONE provoked recovery warning from the hole.\n");
            if (_fall == null) { _log.AppendLine("RIG BROKEN: no FallRecovery on the player"); yield break; }
            if (_manager != null) { foreach (EventActor ea in _manager.ActiveActors) ea.gameObject.SetActive(false); _manager.enabled = false; }
            Application.logMessageReceived += CountLogs;
            _log.AppendLine($"config at run time: {(_fall.Config == null ? "NONE" : _fall.Config.name)}; recoveries since boot {_fall.Recoveries}");
            float t0 = 0f;
            while (!_cc.isGrounded && t0 < 8f) { t0 += Time.deltaTime; yield return null; }
            // walk normally on open ground for 6 s: there must be no recovery
            Place(new Vector3(60f, 0f, 60f), 90f);
            yield return Settle(0.6f);
            int r0 = _fall.Recoveries;
            Keys(Key.W);
            yield return Settle(6f);
            Keys();
            int normal = _fall.Recoveries - r0;
            Vector3 a = _player.transform.position;
            _log.AppendLine($"walked 6 s on open ground to {V(a)}: {normal} recoveries (0 expected)");
            // then the hole, on the defaults
            Place(new Vector3(-379f, 0f, -244f), 0f);
            yield return Settle(0.8f);
            int r1 = _fall.Recoveries;
            Keys(Key.W);
            float t = 0f, airAt = -1f;
            while (t < 20f && _fall.Recoveries == r1)
            {
                _player.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
                yield return null; t += Time.deltaTime;
                if (!_cc.isGrounded && airAt < 0f) airAt = Time.time;
            }
            Keys();
            yield return Settle(0.6f);
            Vector3 b = _player.transform.position;
            bool back = _fall.Recoveries > r1;
            _log.AppendLine($"hole: {(back ? $"put back {F(_fall.LastRecoveryTime - airAt, "0.000")} s after leaving the ground, reason '{_fall.LastReason}', at {V(b)}, grounded {_cc.isGrounded}" : "NOT PUT BACK")}");
            Application.logMessageReceived -= CountLogs;
            _log.AppendLine($"FAILSOFT {staging}: normal-walk recoveries {normal}, hole recovered {back}; console during the run: {_warnWorld} [World] warning(s) (1 expected: the hole), " +
                            $"{_warnOther} other warning(s), {_errors} error(s)\n{_msgs}");
            File.WriteAllText(Path.Combine(outputDir, "failsoft.txt"), $"staging {staging}\nnormal_walk_recoveries {normal}\nhole_recovered {back}\nworld_warnings_during_run {_warnWorld}\nother_warnings {_warnOther}\nerrors {_errors}\n");
        }

        /// <summary>The colliders the player's capsule overlaps right now (excluding the player, actors, triggers and the
        /// WorldBounds barriers' insides do count — standing in a barrier is a defect too).</summary>
        private int Overlaps(out string names)
        {
            float r = _cc.radius, h = Mathf.Max(_cc.height, 2f * r);
            Vector3 c = _player.transform.TransformPoint(_cc.center);
            Vector3 p1 = c + Vector3.up * (h * 0.5f - r), p2 = c - Vector3.up * (h * 0.5f - r);
            Collider[] hits = Physics.OverlapCapsule(p1 + Vector3.up * 0.02f, p2 + Vector3.up * 0.02f, r - 0.02f, ~0, QueryTriggerInteraction.Ignore);
            var sb = new StringBuilder(); int n = 0;
            foreach (Collider col in hits) { if (IsMine(col)) continue; n++; sb.Append(col.name).Append(' '); }
            names = sb.ToString().Trim();
            return n;
        }

        private static int ActiveCanvases()
        {
            int n = 0;
            foreach (Canvas cv in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (cv.isActiveAndEnabled && cv.enabled) n++;
            return n;
        }

        /// <summary>The screen as the player sees it, overlay UI included (call after WaitForEndOfFrame; the camera renders to
        /// the screen). Returns the luma std-dev: a blank or uniform frame means the rig is broken.</summary>
        private static float CaptureScreen(string path)
        {
            Texture2D tex = ScreenCapture.CaptureScreenshotAsTexture();
            float std = LumaStd(tex);
            File.WriteAllBytes(path, tex.EncodeToJPG(85));
            Destroy(tex);
            return std;
        }
    }
}
#endif
