using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using CameraGame.Events;
using CameraGame.PhotoMode;
using CameraGame.Recording;

namespace CameraGame.Bench.RecordingBench
{
    /// <summary>
    /// Story 1.15, Task 11: the recorder's frame-rate COST in a Windows release build (AC3, NFR1).
    ///
    /// ⚠️ FENCED. This assembly compiles only in the editor or when a build passes CAMERA_RECORDING_BENCH through
    /// <c>BuildPlayerOptions.extraScriptingDefines</c> (Tools/Recording/Build Recording Bench); no game assembly
    /// references it, and RecordingBenchFenceTests guard both. In the editor and in a bench build launched
    /// WITHOUT <c>-recording-bench</c> it does nothing at all.
    ///
    /// What it does: loads with SampleScene, puts the player at the rig's vantage on the drunk's route (ADR-5
    /// scale), raises the camera through <c>SetPhotoMode(true)</c>, and runs the same segment of the drunk's
    /// lifecycle (24 s from each spawn) once RECORDING, through the real <c>ToggleRecording()</c>, and once not,
    /// for N rounds with the order rotated. A per-frame CSV (<c>i,t,dt_ms,phase,round,segment,serial</c>) goes
    /// to <c>-bench-out</c>; tools/verification/recording_bench_did.py computes the difference-in-differences.
    /// Then it quits.
    /// </summary>
    public class RecordingBench : MonoBehaviour
    {
        private const float SegmentSeconds = 24f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-recording-bench") < 0) return;
            var go = new GameObject("RecordingBench");
            DontDestroyOnLoad(go);
            var b = go.AddComponent<RecordingBench>();
            b._out = Arg(args, "-bench-out", Path.Combine(Application.persistentDataPath, "recording-bench"));
            b._rounds = int.TryParse(Arg(args, "-bench-rounds", "3"), out int r) ? Mathf.Clamp(r, 1, 10) : 3;
        }

        private static string Arg(string[] a, string name, string fallback)
        {
            int i = Array.IndexOf(a, name);
            return i >= 0 && i + 1 < a.Length ? a[i + 1] : fallback;
        }

        private string _out;
        private int _rounds = 3;
        private PhotoModeController _photo;
        private ClipRecorder _recorder;
        private EventManager _manager;
        private readonly StringBuilder _csv = new StringBuilder(1 << 20);
        private readonly StringBuilder _summary = new StringBuilder();
        private int _frame;

        private IEnumerator Start()
        {
            Directory.CreateDirectory(_out);
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 0;   // measure the frame cost, not the display's refresh
            yield return null;

            _photo = FindAnyObjectByType<PhotoModeController>();
            _recorder = FindAnyObjectByType<ClipRecorder>();
            _manager = FindAnyObjectByType<EventManager>();
            var player = FindAnyObjectByType<ThirdPersonController>();
            if (_photo == null || _recorder == null || _manager == null || player == null)
            {
                File.WriteAllText(Path.Combine(_out, "bench-error.txt"), "scene wiring missing");
                Application.Quit(2);
                yield break;
            }

            // Stray mouse or keyboard input must not move the camera mid-measurement.
            var pi = player.GetComponent<PlayerInput>();
            if (pi != null) pi.DeactivateInput();

            var cc = player.GetComponent<CharacterController>();
            float y = Physics.Raycast(new Vector3(32f, 60f, 8f), Vector3.down, out RaycastHit h, 120f) ? h.point.y : 0.5f;
            cc.enabled = false;
            player.transform.SetPositionAndRotation(new Vector3(32f, y + 0.05f, 8f),
                Quaternion.Euler(0f, Mathf.Atan2(26f - 32f, 19f - 8f) * Mathf.Rad2Deg, 0f));
            cc.enabled = true;
            _photo.SetPhotoMode(true);

            _summary.AppendLine($"Recording bench, {DateTime.Now:u}; {Screen.width}x{Screen.height}, vSync {QualitySettings.vSyncCount}, " +
                                $"{SystemInfo.graphicsDeviceName}, {SystemInfo.processorType}, rounds {_rounds}, segment {SegmentSeconds} s from each spawn.");
            _csv.AppendLine("i,t,dt_ms,phase,round,segment,serial,recording");

            // Warm-up: one whole lifecycle unmeasured (shader warm-up, first-spawn costs).
            yield return WaitForSpawn();
            yield return WaitForSpawn();

            int segment = 0;
            for (int round = 0; round < _rounds; round++)
            {
                // Order rotated each round: rec-base, base-rec, rec-base…, so neither mode always runs first.
                bool recFirst = round % 2 == 0;
                for (int k = 0; k < 2; k++)
                {
                    bool rec = recFirst ? k == 0 : k == 1;
                    yield return Segment(rec, round, segment++);
                }
            }

            File.WriteAllText(Path.Combine(_out, "bench-frames.csv"), _csv.ToString());
            File.WriteAllText(Path.Combine(_out, "bench-summary.txt"), _summary.ToString());
            Application.Quit(0);
        }

        private int Serial()
        {
            IReadOnlyList<EventActor> a = _manager.ActiveActors;
            return a.Count > 0 && a[0] != null ? a[0].LifecycleSerial : 0;
        }

        private IEnumerator WaitForSpawn()
        {
            int s0 = Serial();
            float waited = 0f;
            while ((Serial() == s0 || Serial() == 0) && waited < 90f) { waited += Time.unscaledDeltaTime; yield return null; }
        }

        private IEnumerator Segment(bool rec, int round, int segment)
        {
            yield return WaitForSpawn();
            int serial = Serial();
            long alloc0 = UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong();
            long res0 = UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong();
            int gc0 = GC.CollectionCount(0);
            if (rec) _photo.ToggleRecording();
            float t = 0f;
            string phase = rec ? "rec" : "base";
            while (t < SegmentSeconds)
            {
                yield return null;
                float dt = Time.unscaledDeltaTime;
                t += dt;
                _csv.Append(_frame++).Append(',').Append(t.ToString("0.0000", CultureInfo.InvariantCulture)).Append(',')
                    .Append((dt * 1000f).ToString("0.000", CultureInfo.InvariantCulture)).Append(',').Append(phase).Append(',')
                    .Append(round).Append(',').Append(segment).Append(',').Append(serial).Append(',')
                    .Append(_recorder.IsRecording ? 1 : 0).Append('\n');
            }
            string clip = "-";
            if (rec)
            {
                _photo.ToggleRecording();
                RecordedClip c = _recorder.LastClip;
                if (c != null) clip = c.ToString();
            }
            _summary.AppendLine(string.Format(CultureInfo.InvariantCulture,
                "segment {0} round {1} {2} lifecycle {3}: allocated {4:+0.0;-0.0} MB, reserved {5:+0.0;-0.0} MB, gen0 GCs {6}, clip {7}",
                segment, round, phase, serial, (UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() - alloc0) / 1048576.0,
                (UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() - res0) / 1048576.0, GC.CollectionCount(0) - gc0, clip));
        }
    }
}
