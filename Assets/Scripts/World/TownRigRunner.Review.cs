#if UNITY_EDITOR
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

namespace CameraGame.World
{
    /// <summary>
    /// Story 2.2 CODE REVIEW scenarios for FallRecovery (rig code, not shipped). Mode "review":
    ///   staging "outside" — TownRig.Stage gives FallRecovery a VALID polygon that does not contain the spawn (the default
    ///                       shifted 2,000 m east). Measures what the game does with the player standing at the spawn.
    ///   staging "ring"    — runtime swaps of a CLONE of the shipped config (never the asset): the shipped values, then
    ///                       minRestoreAge 5 s, then safeSampleInterval 0.01 s, each walked into the real hole 3 cycles;
    ///                       then maxAirborneSeconds 0.5 s against three ordinary jumps on open ground.
    /// Every '[World]' warning in these runs is PROVOKED ON PURPOSE; the log says how many each case produced.
    /// </summary>
    public partial class TownRigRunner
    {
        partial void ExtraModes6(ref IEnumerator routine)
        {
            if (mode == "review") routine = staging == "outside" ? ReviewOutside() : ReviewRing();
        }

        private static readonly FieldInfo CfgField = typeof(FallRecovery).GetField("config", BindingFlags.NonPublic | BindingFlags.Instance);
        private static readonly FieldInfo FallbackField = typeof(FallRecovery).GetField("_fallback", BindingFlags.NonPublic | BindingFlags.Instance);

        private IEnumerator ReviewOutside()
        {
            _log.Insert(0, "PROVOKED ON PURPOSE (review): FallRecovery runs on a valid playable-area polygon that does NOT contain the spawn. " +
                           "Every '[World]' warning below is provoked; the question is how many and what the player can do.\n");
            if (_fall == null) { _log.AppendLine("RIG BROKEN: no FallRecovery"); yield break; }
            if (_manager != null) { foreach (Events.EventActor ea in _manager.ActiveActors) ea.gameObject.SetActive(false); _manager.enabled = false; }
            Application.logMessageReceived += CountLogs;
            var cfg = (TownBoundsConfig)CfgField.GetValue(_fall);
            Vector3 s = _player.transform.position;
            _log.AppendLine($"config at run time: {(cfg != null ? cfg.name : "NONE")}; polygon[0] {(cfg != null ? cfg.SafePlayableArea[0].ToString() : "-")}; " +
                            $"spawn allowed? {(cfg != null ? cfg.IsAllowed(new Vector2(s.x, s.z)).ToString() : "-")}; recoveries since boot {_fall.Recoveries}");
            int w0 = _warnWorld, r0 = _fall.Recoveries, f0 = Time.frameCount;
            Vector3 a = _player.transform.position;
            yield return Settle(3f);
            int frames = Time.frameCount - f0;
            Vector3 b = _player.transform.position;
            _log.AppendLine($"IDLE 3 s ({frames} frames): recoveries {_fall.Recoveries - r0}, [World] warnings {_warnWorld - w0}, grounded {_cc.isGrounded}, moved {F(Hz(a, b))} m, last reason '{_fall.LastReason}'");
            yield return new WaitForEndOfFrame();
            CaptureScreen(Path.Combine(outputDir, "outside_idle.jpg"));
            int w1 = _warnWorld, r1 = _fall.Recoveries; f0 = Time.frameCount;
            Keys(Key.W);
            yield return Settle(3f);
            Keys();
            frames = Time.frameCount - f0;
            Vector3 c = _player.transform.position;
            _log.AppendLine($"W HELD 3 s ({frames} frames): recoveries {_fall.Recoveries - r1}, [World] warnings {_warnWorld - w1}, moved {F(Hz(b, c))} m from {V(b)} to {V(c)}");
            // and the hole still recovers (the below-the-world reasons must survive whatever the area check does)
            Place(new Vector3(-379f, 0f, -244f), 0f);
            yield return Settle(0.8f);
            int r2 = _fall.Recoveries;
            Keys(Key.W);
            float t = 0f;
            while (t < 20f && _fall.Recoveries == r2) { _player.transform.rotation = Quaternion.Euler(0f, 0f, 0f); yield return null; t += Time.deltaTime; }
            Keys();
            yield return Settle(0.6f);
            _log.AppendLine($"HOLE: {(_fall.Recoveries > r2 ? $"put back ({_fall.Recoveries - r2} recoveries) at {V(_fall.LastTo)}, reason '{_fall.LastReason}', now at {V(_player.transform.position)}" : "NOT PUT BACK")}");
            Application.logMessageReceived -= CountLogs;
            _log.AppendLine($"REVIEW outside: total [World] warnings {_warnWorld}, other warnings {_warnOther}, errors {_errors}\n{_msgs}");
            File.WriteAllText(Path.Combine(outputDir, "review.txt"), _log.ToString());
        }

        private IEnumerator ReviewRing()
        {
            _log.Insert(0, "PROVOKED ON PURPOSE (review): every '[World] The player fell out of the world' warning is a provoked fall into the " +
                           "real hole (4 hole cases x 3 cycles = 12) or a jump the backstop misread. Any other warning or error is a finding.\n");
            if (_fall == null) { _log.AppendLine("RIG BROKEN: no FallRecovery"); yield break; }
            if (_manager != null) { foreach (Events.EventActor ea in _manager.ActiveActors) ea.gameObject.SetActive(false); _manager.enabled = false; }
            Application.logMessageReceived += CountLogs;
            var shipped = (TownBoundsConfig)CfgField.GetValue(_fall);
            if (shipped == null) { _log.AppendLine("RIG BROKEN: no shipped config to clone"); yield break; }
            float t0 = 0f;
            while (!_cc.isGrounded && t0 < 8f) { t0 += Time.deltaTime; yield return null; }
            // read AFTER FallRecovery.Start has run (the first review run read it before, and logged [0 0 0])
            Vector3 fallback = (Vector3)FallbackField.GetValue(_fall);
            _log.AppendLine($"shipped config {shipped.name}; FallRecovery's spawn fallback {V(fallback)}");
            var csv = new StringBuilder("case,cycle,settle_s,reason,put_back_at_x,put_back_at_y,put_back_at_z,metres_from_walk_start,metres_from_spawn,grounded_after\n");
            Vector3 start = new Vector3(-379f, 0f, -244f);
            string[] cases = { "shipped", "minRestoreAge_5", "safeSampleInterval_0.01", "shipped_again" };
            var clones = new System.Collections.Generic.List<TownBoundsConfig>();
            foreach (string cs in cases)
            {
                var clone = ScriptableObject.Instantiate(shipped);
                clone.name = "TownBoundsConfig (review clone: " + cs + ")";
                if (cs == "minRestoreAge_5") clone.minRestoreAge = 5f;
                if (cs == "safeSampleInterval_0.01") clone.safeSampleInterval = 0.01f;
                clones.Add(clone);
                CfgField.SetValue(_fall, clone);
                float settle = cs == "minRestoreAge_5" ? 7f : 0.8f;   // a fair chance: the player stood on safe ground longer than the restore age
                for (int cycle = 0; cycle < 3; cycle++)
                {
                    Place(start, 0f);
                    yield return Settle(settle);
                    Vector3 walkStart = _player.transform.position;
                    int r = _fall.Recoveries;
                    Keys(Key.W);
                    float t = 0f;
                    while (t < 20f && _fall.Recoveries == r)
                    {
                        _player.transform.rotation = Quaternion.Euler(0f, 0f, 0f);
                        yield return null; t += Time.deltaTime;
                    }
                    Keys();
                    bool back = _fall.Recoveries > r;
                    yield return Settle(0.6f);
                    Vector3 to = _fall.LastTo;
                    csv.AppendLine($"{cs},{cycle},{F(settle)},{(back ? _fall.LastReason.Replace(',', ';') : "NOT PUT BACK")},{F(to.x)},{F(to.y)},{F(to.z)},{F(Hz(to, walkStart))},{F(Hz(to, fallback))},{_cc.isGrounded}");
                    _log.AppendLine($"{cs} cycle {cycle}: {(back ? $"put back at {V(to)}: {F(Hz(to, walkStart))} m from where the walk began, {F(Hz(to, fallback))} m from the spawn; reason '{_fall.LastReason}'" : "NOT PUT BACK")}");
                    if (cycle == 0) { yield return new WaitForEndOfFrame(); CaptureScreen(Path.Combine(outputDir, $"ring_{cs}_after.jpg")); }
                }
            }
            // the airborne backstop against ordinary jumps on open ground
            var jcsv = new StringBuilder("case,jump,airborne_s,recoveries_during_jump,reason\n");
            foreach (float maxAir in new[] { -1f, 0.5f })
            {
                var clone = ScriptableObject.Instantiate(shipped);
                if (maxAir > 0f) clone.maxAirborneSeconds = maxAir;
                string label = maxAir > 0f ? "maxAirborne_" + F(maxAir) : "maxAirborne_shipped_" + F(shipped.SafeMaxAirborneSeconds);
                clone.name = "TownBoundsConfig (review clone: " + label + ")";
                clones.Add(clone);
                CfgField.SetValue(_fall, clone);
                Place(new Vector3(60f, 0f, 60f), 90f);
                yield return Settle(1f);
                for (int j = 0; j < 3; j++)
                {
                    int r = _fall.Recoveries;
                    float airtime = 0f; bool left = false;
                    Keys(Key.Space); yield return null; yield return null; Keys();
                    float t = 0f;
                    while (t < 2f) { yield return null; t += Time.deltaTime; if (!_cc.isGrounded) { left = true; airtime += Time.deltaTime; } else if (left) break; }
                    yield return Settle(0.8f);
                    jcsv.AppendLine($"{label},{j},{F(airtime, "0.00")},{_fall.Recoveries - r},{(_fall.Recoveries > r ? _fall.LastReason : "")}");
                    _log.AppendLine($"{label} jump {j}: airborne ~{F(airtime, "0.00")} s, recoveries {_fall.Recoveries - r} {(_fall.Recoveries > r ? "('" + _fall.LastReason + "')" : "")}");
                }
            }
            CfgField.SetValue(_fall, shipped);
            foreach (var c in clones) Destroy(c);
            Application.logMessageReceived -= CountLogs;
            _log.AppendLine($"REVIEW ring: [World] warnings {_warnWorld}, other warnings {_warnOther}, errors {_errors}\n{_msgs}");
            File.WriteAllText(Path.Combine(outputDir, "ring.csv"), csv.ToString());
            File.WriteAllText(Path.Combine(outputDir, "jumps.csv"), jcsv.ToString());
            File.WriteAllText(Path.Combine(outputDir, "review.txt"), _log.ToString());
        }
    }
}
#endif
