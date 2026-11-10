// THROWAWAY SPIKE CODE (Story 1.14). Never referenced by game code. See README.md in this folder.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using CameraGame.Core;
using CameraGame.Events;
using CameraGame.PhotoMode;
using Unity.Collections;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.Video;

namespace CameraGame.Spikes.RecordingSpike
{
    /// <summary>
    /// Self-bootstrapping measurement harness (Task 2.2). Arms ONLY when -recordingSpike is on the command
    /// line (build) or the editor rig set the SessionState flag; creates every helper object at runtime and
    /// edits nothing in the scene. Runs the same scripted scenario every time (Task 1c): camera raised
    /// through the real PhotoModeController.SetPhotoMode, fixed vantage, aimed at EventActor.Bounds.center,
    /// ~10 s baseline, ~10 s recording across the drunk's peak, ~10 s baseline, then ~10 s of the
    /// candidate's own in-game playback. Quits on its own.
    /// </summary>
    [DefaultExecutionOrder(10000)]
    public sealed class SpikeHarness : MonoBehaviour
    {
        // ---------- boot ----------
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Boot()
        {
            var a = SpikeArgs.Read();
            if (!a.Armed) return;
            var go = new GameObject("RecordingSpike_Harness");
            var h = go.AddComponent<SpikeHarness>(); h._a = a; h._bootTime = Time.realtimeSinceStartup;
        }

        private SpikeArgs _a;
        private int _prevTargetFps = -1;
        private float _bootTime;
        private PhotoModeController _photo;
        private EventManager _mgr;
        private ThirdPersonCamera _tpc;
        private ThirdPersonController _ctrl;
        private Camera _cam;
        private FrameTap _tap;
        private AudioTap _audio;
        private FfmpegSink _sink;
        private RawRing _ring;
        private ReplayTrack _track;
        private ReplayPlayer _replay;
        private bool _tpcWasEnabled;
        private string _out;
        private int ClipFrames => _a.Fps * 10;
        private int FrameBytes => _a.Width * _a.Height * 4;

        // ---------- per-frame log (preallocated, Task 2.4) ----------
        private const int MaxFrames = 40000;
        private readonly float[] _t = new float[MaxFrames], _dt = new float[MaxFrames], _peak = new float[MaxFrames], _dist = new float[MaxFrames], _mt = new float[MaxFrames];
        private readonly byte[] _ph = new byte[MaxFrames], _cap = new byte[MaxFrames], _rep = new byte[MaxFrames];
        private int _n;
        private static readonly string[] PhaseNames = { "setup", "pre", "rec", "post", "play", "wait", "rec2", "play2", "export", "rec3", "play3" };
        private byte _phase;
        private ProfilerRecorder _mainThread;
        private readonly StringBuilder _sec = new StringBuilder("t,phase,private_mb,working_set_mb,candidate_buffer_mb,candidate_used_mb,bytes_written_mb,dropped,queue,audio_callbacks\n");
        private float _nextSec;
        private System.Diagnostics.Process _self;
        private readonly StringBuilder _events = new StringBuilder();
        private readonly Dictionary<string, string> _sum = new Dictionary<string, string>();

        // ---------- recording state ----------
        private bool _recording;
        private float _recStart;
        private int _nextVf;          // next output (video) frame index
        private int _carry;           // repeats owed by dropped frames
        private int _captures, _dupFrames, _dropped, _readbackErrors, _outOfOrder, _lastSubmittedVf = -1;
        private int _cycle;
        private int _peakVf = -1;     // first output frame inside the peak window
        private readonly int[] _refVf = { 60, 165, 255 };
        private readonly NativeArray<byte>[] _refBuf = new NativeArray<byte>[3];
        private readonly bool[] _refOk = new bool[3];
        private readonly Vector3[][] _refBonesLive = new Vector3[3][];
        private int _markerA = 15, _markerB = 285;
        private readonly List<string> _markerLog = new List<string>();
        private GameObject _flash;
        private int _flashOffFrame = -1;
        private AudioSource _tone;
        private string _clipPath = "";
        private Task _finalize;
        private double _stopToReadyMs = -1;
        private float[] _pcm = new float[0];
        private EventActor _actor;
        private double _finishMs; private int _muxExit;
        private float _postStart;

        // ---------- playback ----------
        private Canvas _playCanvas;
        private RawImage _playImage;
        private AudioSource _playAudio;

        private void Log(string msg)
        {
            string line = $"[{Time.realtimeSinceStartup:0.000}] {msg}";
            _events.AppendLine(line);
            GameLog.Info("RecordingSpike", msg);
        }

        private IEnumerator Start()
        {
            _out = _a.OutDir;
            Directory.CreateDirectory(_out);
            _self = System.Diagnostics.Process.GetCurrentProcess();
            _mainThread = ProfilerRecorder.StartNew(ProfilerCategory.Internal, "Main Thread", 1);
            Log($"armed mode={_a.Mode} tag={_a.Tag} out={_out} args='{string.Join(" ", _a.Raw)}'");
            if (_a.Mode == "failsoft")
                Log("NOTE: this is a FAIL-SOFT run. It provokes encoder/ring failures ON PURPOSE; the warnings it logs are expected, not findings.");

            // ---- the one-off facts the protocol asks for (Task 1c) ----
            _sum["mode"] = Q(_a.Mode); _sum["tag"] = Q(_a.Tag);
            _sum["editor"] = Application.isEditor ? "true" : "false";
            _sum["unity"] = Q(Application.unityVersion);
            _sum["gpu"] = Q(SystemInfo.graphicsDeviceName);
            _sum["gpu_api"] = Q(SystemInfo.graphicsDeviceType.ToString());
            _sum["cpu"] = Q(SystemInfo.processorType);
            _sum["battery"] = Q(SystemInfo.batteryStatus.ToString());
            _sum["battery_level"] = F(SystemInfo.batteryLevel);
            _sum["quality"] = Q(QualitySettings.names[QualitySettings.GetQualityLevel()]);
            _sum["screen"] = Q($"{Screen.width}x{Screen.height} fullscreen={Screen.fullScreenMode} refresh={Screen.currentResolution.refreshRateRatio.value:0.##}");
            _sum["vsync"] = QualitySettings.vSyncCount.ToString();
            _sum["target_fps"] = Application.targetFrameRate.ToString();
            _sum["audio"] = Q($"{AudioSettings.outputSampleRate} Hz {AudioSettings.speakerMode}");
            _sum["profiler_main_thread_valid"] = _mainThread.Valid ? "true" : "false";
            _sum["footage"] = Q($"{_a.Width}x{_a.Height}@{_a.Fps}");
            _sum["encoder_requested"] = Q(_a.Encoder);
            Log($"device gpu='{SystemInfo.graphicsDeviceName}' cpu='{SystemInfo.processorType}' battery={SystemInfo.batteryStatus} screen={Screen.width}x{Screen.height} audio={AudioSettings.outputSampleRate}/{AudioSettings.speakerMode} mainThreadRecorder={_mainThread.Valid}");
            if (SystemInfo.batteryStatus == BatteryStatus.Discharging)
                Log("WARNING: running on battery - this run is VOID for measurement (protocol 1c).");

            _prevTargetFps = Application.targetFrameRate;
            if (_a.FpsCap > 0) { Application.targetFrameRate = _a.FpsCap; Log($"SENSITIVITY RUN: frame rate capped at {_a.FpsCap} (the game ships uncapped)"); }
            _sum["fps_cap"] = _a.FpsCap.ToString();
            yield return null;
            if (!SetupScene()) { Finish("setup failed"); yield break; }

            switch (_a.Mode)
            {
                case "survey": yield return Survey(); Finish("survey done"); yield break;
                case "control": yield return ControlShot(); Finish("control done"); yield break;
                case "failsoft": yield return FailSoft(); Finish("failsoft done"); yield break;
            }

            AllocateCandidate();
            _phase = 0;
            yield return Wait(3f);       // settle: shaders, streaming, the first spawn
            _phase = 1;                  // "pre" baseline starts accumulating from here
            float preStart = Time.unscaledTime;

            for (_cycle = 1; _cycle <= _a.Cycles; _cycle++)
            {
                // Find a peak whose 5-s lead-in starts at least 10 s after the baseline began (cycle 1).
                while (true)
                {
                    var act = Live();
                    if (act != null && act.PeakOffset > 5.05f)
                    {
                        float recAt = Time.unscaledTime + act.PeakOffset - 5f;
                        if (_cycle > 1 || recAt - preStart >= 10f) break;
                    }
                    yield return null;
                }
                while (Live() == null || Live().PeakOffset > 5f) yield return null;
                yield return Record();
                if (_cycle == 1)
                {
                    _phase = 3;
                    while (Time.unscaledTime - _postStart < 10f) yield return null;
                }
                if (_a.Play || _a.Prove) yield return Playback();
                _phase = 5;
            }

            _phase = 8;
            yield return ExportProbe();
            Finish("scenario complete");
        }

        private EventActor Live()
        {
            if (_mgr == null) return null;
            var list = _mgr.ActiveActors;
            for (int i = 0; i < list.Count; i++) if (list[i] != null && list[i].isActiveAndEnabled) return list[i];
            return null;
        }

        private bool SetupScene()
        {
            _photo = FindFirstObjectByType<PhotoModeController>();
            _mgr = FindFirstObjectByType<EventManager>();
            _tpc = FindFirstObjectByType<ThirdPersonCamera>();
            _ctrl = FindFirstObjectByType<ThirdPersonController>();
            _cam = Camera.main;
            Log($"found photo={_photo != null} mgr={_mgr != null} tpc={_tpc != null} ctrl={_ctrl != null} cam={(_cam != null ? _cam.name : "null")}");
            if (_photo == null || _mgr == null || _cam == null) return false;
            if (_tpc != null) { _tpcWasEnabled = _tpc.enabled; _tpc.enabled = false; }
            if (_ctrl != null) _ctrl.SetInputSuppressed(true);
            _photo.SetPhotoMode(true);           // the real raise path
            _photo.SetZoom(_a.Zoom);             // the real zoom path
            if (!float.IsNaN(_a.Vantage.x)) _cam.transform.position = _a.Vantage;

            _audio = _cam.gameObject.AddComponent<AudioTap>();
            _audio.Allocate(14f);
            _tap = new FrameTap(_cam, _a.Width, _a.Height);

            // Clapperboard (Task 2.5): a full-frame white quad in the 3D view, just past the near plane.
            _flash = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _flash.name = "RecordingSpike_Clapper";
            Destroy(_flash.GetComponent<Collider>());
            _flash.transform.SetParent(_cam.transform, false);
            float d = _cam.nearClipPlane + 0.02f;
            _flash.transform.localPosition = new Vector3(0, 0, d);
            float hh = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad) * d * 2.2f;
            _flash.transform.localScale = new Vector3(hh * 2.5f, hh, 1);
            var sh = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
            var r = _flash.GetComponent<Renderer>();
            if (sh != null) r.sharedMaterial = new Material(sh) { color = Color.white };
            r.shadowCastingMode = ShadowCastingMode.Off;
            _sum["clapper_shader"] = Q(sh != null ? sh.name : "none (default material)");
            _flash.SetActive(false);
            _tone = gameObject.AddComponent<AudioSource>();
            _tone.spatialBlend = 0; _tone.playOnAwake = false;
            int rate = AudioSettings.outputSampleRate;
            var clip = AudioClip.Create("spike_tone", rate / 20, 1, rate, false);
            var tone = new float[rate / 20];
            for (int i = 0; i < tone.Length; i++) tone[i] = 0.6f * Mathf.Sin(2 * Mathf.PI * 1000f * i / rate);
            clip.SetData(tone, 0);
            _tone.clip = clip;
            for (int i = 0; i < 3; i++) _refBuf[i] = new NativeArray<byte>(FrameBytes, Allocator.Persistent);
            return true;
        }

        private void AllocateCandidate()
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            switch (_a.Mode)
            {
                case "A": _sink = new FfmpegSink(FrameBytes, 6); break;
                case "C": _ring = new RawRing(FrameBytes, ClipFrames + 10); break;
                case "B":
                    int layers = 1;
                    var act = Live();
                    var an = act != null ? act.GetComponentInChildren<Animator>() : null;
                    if (an != null) layers = an.layerCount;
                    _track = new ReplayTrack(ClipFrames + 10, layers);
                    break;
            }
            _sum["alloc_ms"] = F((float)sw.Elapsed.TotalMilliseconds);
            Log($"allocated candidate {_a.Mode} in {sw.Elapsed.TotalMilliseconds:0} ms; buffer={(_sink != null ? _sink.BufferedBytes : _ring != null ? _ring.BufferedBytes : 0) / 1048576.0:0} MB");
        }

        private IEnumerator Wait(float s)
        {
            float end = Time.unscaledTime + s;
            while (Time.unscaledTime < end) yield return null;
        }

        // ---------- every frame ----------
        private void Update()
        {
            if (_a == null) return;
            if (Time.realtimeSinceStartup - _bootTime > _a.Timeout) { Log($"HARD TIMEOUT after {_a.Timeout:0} s - quitting regardless"); Finish("timeout"); return; }
            if (_flash != null && _flash.activeSelf && Time.frameCount >= _flashOffFrame) _flash.SetActive(false);

            if (Time.unscaledTime >= _nextSec && _self != null)
            {
                _nextSec = Time.unscaledTime + 1f;
                _self.Refresh();
                long buf = _sink != null ? _sink.BufferedBytes : _ring != null ? _ring.BufferedBytes : _track != null ? (long)_track.Ticks.Length * _track.BytesPerTick + _audio.BufferedFloats * 4L : 0;
                long used = _ring != null ? _ring.UsedBytes : _track != null ? (long)_track.Count * _track.BytesPerTick : 0;
                long written = _sink != null ? _sink.BytesPiped : 0;
                _sec.Append(F(Time.unscaledTime)).Append(',').Append(PhaseNames[_phase]).Append(',')
                    .Append(F(_self.PrivateMemorySize64 / 1048576f)).Append(',').Append(F(_self.WorkingSet64 / 1048576f)).Append(',')
                    .Append(F(buf / 1048576f)).Append(',').Append(F(used / 1048576f)).Append(',').Append(F(written / 1048576f)).Append(',')
                    .Append(_dropped + (_sink != null ? _sink.Dropped : 0)).Append(',').Append(_sink != null ? _sink.QueueDepth : 0).Append(',')
                    .Append(_audio != null ? _audio.Callbacks : 0).Append('\n');
            }
        }

        private void LateUpdate()
        {
            if (_a == null || _cam == null) return;
            var act = Live();
            if (act != null && _a.Mode != "control")
            {
                var b = act.Bounds;
                _cam.transform.rotation = Quaternion.LookRotation(b.center - _cam.transform.position);
            }
            if (_recording) ScheduleCapture(act);

            if (_n < MaxFrames)
            {
                _t[_n] = Time.unscaledTime; _dt[_n] = Time.unscaledDeltaTime * 1000f; _ph[_n] = _phase;
                _peak[_n] = act != null ? act.PeakOffset : float.NaN;
                _dist[_n] = act != null ? Vector3.Distance(act.Bounds.center, _cam.transform.position) : float.NaN;
                _mt[_n] = _mainThread.Valid ? _mainThread.LastValue / 1e6f : float.NaN;
                _n++;
            }
        }

        // ---------- recording (Tasks 3, 4, 5) ----------
        private IEnumerator Record()
        {
            _actor = Live();
            _phase = (byte)(_cycle == 1 ? 2 : _cycle == 2 ? 6 : 9);
            _nextVf = 0; _carry = 0; _captures = 0; _dupFrames = 0; _dropped = 0; _readbackErrors = 0; _outOfOrder = 0; _lastSubmittedVf = -1; _peakVf = -1;
            for (int i = 0; i < 3; i++) _refOk[i] = false;
            _markerLog.Clear(); _markerADone = _markerBDone = false;
            string clipBase = Path.Combine(_out, $"{_a.Tag}-c{_cycle}");
            if (_a.Mode == "A")
            {
                _clipPath = clipBase + ".mp4";
                if (_cycle > 1) { _sink.Dispose(); _sink = new FfmpegSink(FrameBytes, 6); }
                string vf = _a.VFlip ? "-vf vflip " : "";
                string enc = _a.Encoder == "h264_mf" ? "-c:v h264_mf -hw_encoding 1 -rate_control cbr -b:v 8M -g 30 -pix_fmt nv12"
                           : _a.Encoder == "h264_qsv" ? "-c:v h264_qsv -b:v 8M -g 30 -pix_fmt nv12"
                           : "-c:v " + _a.Encoder + " -b:v 8M -g 30 -pix_fmt yuv420p";
                if (_a.Encoder == "h264_qsv") { enc = "-c:v h264_qsv -b:v 8M -g 30"; vf = _a.VFlip ? "-vf vflip,format=nv12 " : "-vf format=nv12 "; }
                // MPEG-TS while recording: readable when truncated (a crash keeps the footage so far), and
                // h264_mf never delivers extradata in time for MP4's header. Remuxed to MP4 at stop.
                string tail = $"{vf}{enc} -f mpegts \"{clipBase}-video.ts\"";
                // Diagnostics that split (A)'s cost: transport only, transport + RGBA->NV12 conversion.
                if (_a.Encoder == "null") tail = "-f null -";
                if (_a.Encoder == "nullconv") tail = $"{vf}-pix_fmt nv12 -f null -";
                string args = $"-y -hide_banner -loglevel verbose -f rawvideo -pix_fmt rgba -s {_a.Width}x{_a.Height} -framerate {_a.Fps} -i {{IN}} {tail}";
                var spawn = System.Diagnostics.Stopwatch.StartNew();
                _sink.Start(_a.FfmpegPath, args, clipBase + "-ffmpeg.log", _a.Transport);
                double spawnMs = spawn.Elapsed.TotalMilliseconds;
                while (!_sink.Ready && !_sink.Failed && spawn.Elapsed.TotalSeconds < 10) yield return null;
                _sum[$"c{_cycle}_ffmpeg_spawn_ms"] = F((float)spawnMs);
                _sum[$"c{_cycle}_ffmpeg_ready_ms"] = F((float)spawn.Elapsed.TotalMilliseconds);
                _sum[$"c{_cycle}_transport"] = Q(_a.Transport);
            }
            else if (_a.Mode == "C") _ring.Reset();
            else if (_a.Mode == "B") _track.Reset();

            _audio.Begin();
            _recStart = Time.unscaledTime;
            _recording = true;
            Log($"REC start cycle={_cycle} peakOffset={(_actor != null ? _actor.PeakOffset : float.NaN):0.000} dist={(_actor != null ? Vector3.Distance(_actor.Bounds.center, _cam.transform.position) : float.NaN):0.00}");
            while (_recording) yield return null;
            _postStart = Time.unscaledTime;
            _audio.End();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            _pcm = _audio.Snapshot();
            int ch = _audio.Channels, rate = _audio.SampleRate;
            Log($"REC stop cycle={_cycle} captures={_captures} outFrames={_nextVf} dups={_dupFrames} dropped={_dropped + (_sink != null ? _sink.Dropped : 0)} readbackErrors={_readbackErrors} peakVf={_peakVf} audioFloats={_pcm.Length} audioPeak={_audio.Peak:0.000} overflow={_audio.Overflowed}");
            string wav = clipBase + "-audio.wav";

            if (_a.Mode == "A")
            {
                var sink = _sink; string video = clipBase + "-video.ts"; string outp = _clipPath; string ff = _a.FfmpegPath; var pcm = _pcm;
                _finalize = Task.Run(() =>
                {
                    double fin = sink.Finish();
                    AudioTap.WriteWav(wav, pcm, ch, rate);
                    var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ff,
                        $"-y -hide_banner -loglevel error -i \"{video}\" -i \"{wav}\" -c:v copy -c:a aac -b:a 160k -shortest -movflags +faststart \"{outp}\"")
                    { UseShellExecute = false, CreateNoWindow = true });
                    p.WaitForExit();
                    _stopToReadyMs = sw.Elapsed.TotalMilliseconds;
                    _finishMs = fin; _muxExit = p.ExitCode;
                });
                // The game keeps running while the file is finalised: the post-baseline starts NOW, in parallel.
                _phase = (byte)(_cycle == 1 ? 3 : 5);
                float waitStart = Time.unscaledTime;
                while (!_finalize.IsCompleted) yield return null;
                _sum[$"c{_cycle}_ffmpeg_finish_ms"] = F((float)_finishMs);
                _sum[$"c{_cycle}_mux_exit"] = _muxExit.ToString();
                _sum[$"c{_cycle}_finalize_wall_s"] = F(Time.unscaledTime - waitStart);
                if (_finalize.IsFaulted) Log("finalize faulted: " + _finalize.Exception?.GetBaseException().Message);
                _sum[$"c{_cycle}_encoder_line"] = Q(sink.EncoderLine);
                _sum[$"c{_cycle}_encoder_failed"] = Q(sink.Failed ? sink.FailReason : "");
                _sum[$"c{_cycle}_ffmpeg_exit"] = sink.ExitCode.ToString();
                _sum[$"c{_cycle}_frames_written"] = sink.FramesWritten.ToString();
                _sum[$"c{_cycle}_sink_dropped"] = sink.Dropped.ToString();
                _sum[$"c{_cycle}_file_bytes"] = File.Exists(outp) ? new FileInfo(outp).Length.ToString() : "-1";
            }
            else
            {
                AsyncGPUReadback.WaitAllRequests();
                _stopToReadyMs = sw.Elapsed.TotalMilliseconds;
                // WAV written after the ready-time is taken: (B) and (C) keep PCM in memory; the file is evidence.
                AudioTap.WriteWav(wav, _pcm, ch, rate);
                if (_a.Mode == "C")
                {
                    int ready = 0; for (int i = 0; i < _ring.Count; i++) if (_ring.Ready[i]) ready++;
                    _sum[$"c{_cycle}_ring_ready"] = ready.ToString();
                    _sum[$"c{_cycle}_ring_used_bytes"] = _ring.UsedBytes.ToString();
                }
                if (_a.Mode == "B")
                {
                    _sum[$"c{_cycle}_ticks"] = _track.Count.ToString();
                    _sum[$"c{_cycle}_pose_bytes"] = ((long)_track.Count * _track.BytesPerTick).ToString();
                    _sum["pose_bytes_per_tick"] = _track.BytesPerTick.ToString();
                }
            }
            _sum[$"c{_cycle}_stop_to_ready_ms"] = F((float)_stopToReadyMs);
            _sum[$"c{_cycle}_captures"] = _captures.ToString();
            _sum[$"c{_cycle}_out_frames"] = _nextVf.ToString();
            _sum[$"c{_cycle}_dup_frames"] = _dupFrames.ToString();
            _sum[$"c{_cycle}_dropped"] = (_dropped + (_sink != null ? _sink.Dropped : 0)).ToString();
            _sum[$"c{_cycle}_readback_errors"] = _readbackErrors.ToString();
            _sum[$"c{_cycle}_out_of_order"] = _outOfOrder.ToString();
            _sum[$"c{_cycle}_peak_vf"] = _peakVf.ToString();
            _sum[$"c{_cycle}_audio_floats"] = _pcm.Length.ToString();
            _sum[$"c{_cycle}_audio_peak"] = F(_audio.Peak);
            _sum[$"c{_cycle}_audio_bytes_per_s_f32"] = F(_audio.SampleRate * _audio.Channels * 4f);
            _sum[$"c{_cycle}_markers"] = Q(string.Join(" | ", _markerLog));
            _sum["backbuffer_skips"] = _tap.BackbufferSkips.ToString();
            _sum["passes_recorded"] = _tap.PassesRecorded.ToString();
            if (_cycle == 1) SaveRefs("ref");
            Log($"stop-to-ready {_stopToReadyMs:0} ms; clip={_clipPath}");
        }

        private int RefIndex(int vfStart, int repeats)
        {
            for (int i = 0; i < _refVf.Length; i++) if (_refVf[i] >= vfStart && _refVf[i] < vfStart + repeats) return i;
            return -1;
        }

        private void ScheduleCapture(EventActor act)
        {
            float t = Time.unscaledTime - _recStart;
            int due = Mathf.FloorToInt(t * _a.Fps);           // the output frame this game frame stands for
            if (due < _nextVf) return;                          // game faster than 30 fps: not this frame
            int fresh = due - _nextVf + 1;                     // output frames this game frame covers
            int repeats = fresh + _carry;                       // plus any owed by a dropped capture
            _dupFrames += fresh - 1;
            int vfStart = _nextVf - _carry;
            _carry = 0;
            if (_peakVf < 0 && act != null && act.IsAtPeak) _peakVf = vfStart;
            int refIdx = _cycle == 1 ? RefIndex(vfStart, repeats) : -1;
            if (refIdx >= 0 && act != null && act == _actor) _refBonesLive[refIdx] = Bones(act.GetComponentInChildren<SkinnedMeshRenderer>());

            // Clapperboard: this game frame IS captured, so the flash is in the footage on exactly this frame.
            if (RefIndexMarker(vfStart, repeats))
            {
                _flash.SetActive(true); _flashOffFrame = Time.frameCount + 1;
                _tone.Play();
                _markerLog.Add($"vf={vfStart} t={t:0.0000} frame={Time.frameCount}");
            }

            _captures++;
            if (_n < MaxFrames) { _cap[_n] = 1; _rep[_n] = (byte)Mathf.Min(255, repeats); }
            int captureVf = vfStart; int rep = repeats; int ri = refIdx;
            switch (_a.Mode)
            {
                case "A":
                    if (_sink.Failed) break;
                    if (_sink.TryAcquire(out int slot))
                    {
                        _tap.CaptureThisFrame(() =>
                        {
                            _tap.ReadInto(ref _sink.Slot(slot), req =>
                            {
                                if (req.hasError) { _sink.Release(slot); _readbackErrors++; return; }
                                if (captureVf < _lastSubmittedVf) _outOfOrder++;
                                _lastSubmittedVf = captureVf;
                                _sink.Submit(slot, rep);
                            });
                            if (ri >= 0) RequestRef(ri);
                        });
                    }
                    else { _dropped++; _carry = repeats; }
                    break;
                case "C":
                    if (_ring.Count >= _ring.Slots.Length)
                    {
                        _ring.Overflow++;
                        if (!_ring.FullLogged) { _ring.FullLogged = true; GameLog.Warn("RecordingSpike", "raw frame ring is full - further frames are dropped (game continues)."); }
                        break;
                    }
                    int s = _ring.Count++;
                    _ring.Repeats[s] = rep; _ring.VideoFrame[s] = captureVf; _ring.Ready[s] = false;
                    _tap.CaptureThisFrame(() =>
                    {
                        _tap.ReadInto(ref _ring.Slots[s], req => { if (req.hasError) _readbackErrors++; else _ring.Ready[s] = true; });
                        if (ri >= 0) RequestRef(ri);
                    });
                    break;
                case "B":
                    _track.Record(_cam, act, act != null ? act.GetComponentInChildren<Animator>() : null, rep);
                    if (ri >= 0) _tap.CaptureThisFrame(() => RequestRef(ri));
                    break;
                default: // baseline: nothing is captured, but the bookkeeping runs identically
                    break;
            }
            _nextVf = due + 1;
            if (_nextVf >= ClipFrames) _recording = false;
        }

        private bool _markerADone, _markerBDone;
        private bool RefIndexMarker(int vfStart, int repeats)
        {
            if (!_markerADone && _markerA >= vfStart && _markerA < vfStart + repeats) return _markerADone = true;
            if (!_markerBDone && _markerB >= vfStart && _markerB < vfStart + repeats) return _markerBDone = true;
            return false;
        }

        private void RequestRef(int i)
        {
            int k = i;
            _tap.ReadInto(ref _refBuf[k], req => _refOk[k] = !req.hasError);
        }

        private static Vector3[] Bones(SkinnedMeshRenderer smr)
        {
            if (smr == null || smr.bones == null) return null;
            var r = new Vector3[smr.bones.Length];
            for (int i = 0; i < r.Length; i++) r[i] = smr.bones[i] != null ? smr.bones[i].position : Vector3.zero;
            return r;
        }

        private void SaveRefs(string prefix)
        {
            AsyncGPUReadback.WaitAllRequests();
            for (int i = 0; i < 3; i++)
            {
                if (!_refOk[i]) { Log($"ref {i} (vf {_refVf[i]}) missing"); continue; }
                SavePng(_refBuf[i], Path.Combine(_out, $"{_a.Tag}-{prefix}-vf{_refVf[i]:000}.png"));
            }
        }

        private void SavePng(NativeArray<byte> data, string path)
        {
            var tex = new Texture2D(_a.Width, _a.Height, TextureFormat.RGBA32, false, false);
            tex.SetPixelData(data, 0);
            tex.Apply(false);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
        }

        // ---------- playback (Tasks 3.3, 4.4, 5.2) ----------
        private void EnsurePlaybackUi()
        {
            if (_playCanvas != null) { _playCanvas.gameObject.SetActive(true); return; }
            var go = new GameObject("RecordingSpike_Playback", typeof(Canvas));
            _playCanvas = go.GetComponent<Canvas>();
            _playCanvas.renderMode = RenderMode.ScreenSpaceOverlay; _playCanvas.sortingOrder = 32000;
            var img = new GameObject("Image", typeof(RectTransform), typeof(RawImage));
            img.transform.SetParent(go.transform, false);
            var rt = img.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            _playImage = img.GetComponent<RawImage>();
            _playAudio = go.AddComponent<AudioSource>();
            _playAudio.spatialBlend = 0; _playAudio.playOnAwake = false;
        }

        private IEnumerator Playback()
        {
            _phase = (byte)(_cycle == 1 ? 4 : _cycle == 2 ? 7 : 10);
            EnsurePlaybackUi();
            _audio.Begin();   // what the listener hears during playback → playback-heard.wav (Task 8.1)
            Log($"PLAY start cycle={_cycle} mode={_a.Mode}");
            float t0 = Time.unscaledTime;
            if (_a.Mode == "A" && File.Exists(_clipPath))
            {
                var vp = gameObject.GetComponent<VideoPlayer>();
                if (vp == null) vp = gameObject.AddComponent<VideoPlayer>();
                var rt = new RenderTexture(_a.Width, _a.Height, 0);
                vp.playOnAwake = false; vp.source = VideoSource.Url; vp.url = _clipPath; vp.isLooping = false;
                vp.renderMode = VideoRenderMode.RenderTexture; vp.targetTexture = rt;
                vp.audioOutputMode = VideoAudioOutputMode.AudioSource;
                vp.controlledAudioTrackCount = 1; vp.EnableAudioTrack(0, true); vp.SetTargetAudioSource(0, _playAudio);
                bool err = false; vp.errorReceived += (p, m) => { err = true; Log("VideoPlayer error: " + m); };
                var sw = System.Diagnostics.Stopwatch.StartNew();
                vp.Prepare();
                while (!vp.isPrepared && !err && sw.Elapsed.TotalSeconds < 10) yield return null;
                double prep = sw.Elapsed.TotalMilliseconds;
                _playImage.texture = rt;
                _playImage.uvRect = new Rect(0, 0, 1, 1);
                vp.Play();
                while (vp.frame < 1 && !err && sw.Elapsed.TotalSeconds < 12) yield return null;
                _sum[$"c{_cycle}_vp_prepare_ms"] = F((float)prep);
                _sum[$"c{_cycle}_vp_first_frame_ms"] = F((float)sw.Elapsed.TotalMilliseconds);
                _sum[$"c{_cycle}_vp_info"] = Q($"{vp.width}x{vp.height} {vp.frameRate:0.##}fps frames={vp.frameCount} audioTracks={vp.audioTrackCount} err={err}");
                Log($"VideoPlayer prepared in {prep:0} ms, first frame at {sw.Elapsed.TotalMilliseconds:0} ms: {_sum[$"c{_cycle}_vp_info"]}");
                while (vp.isPlaying && Time.unscaledTime - t0 < 12f) yield return null;
                vp.Stop(); _playImage.texture = null; rt.Release(); Destroy(rt);
            }
            else if (_a.Mode == "C")
            {
                var tex = new Texture2D(_a.Width, _a.Height, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, UnityEngine.Experimental.Rendering.TextureCreationFlags.None);
                _playImage.texture = tex; _playImage.uvRect = new Rect(0, 0, 1, 1);
                PlayPcm();
                float start = Time.unscaledTime; int shown = -1;
                double upMs = 0; int uploads = 0;
                while (true)
                {
                    int vf = Mathf.FloorToInt((Time.unscaledTime - start) * _a.Fps);
                    if (vf >= _nextVf) break;
                    int s = SlotForVf(vf);
                    if (s >= 0 && s != shown && _ring.Ready[s])
                    {
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        tex.SetPixelData(_ring.Slots[s], 0); tex.Apply(false);
                        upMs += sw.Elapsed.TotalMilliseconds; uploads++; shown = s;
                    }
                    yield return null;
                }
                _sum[$"c{_cycle}_raw_upload_ms_mean"] = F(uploads > 0 ? (float)(upMs / uploads) : -1);
                _playImage.texture = null; Destroy(tex);
            }
            else if (_a.Mode == "B")
            {
                if (_replay == null) _replay = new ReplayPlayer(_cam, _mgr, _a.Width, _a.Height);
                if (_actor != null) _replay.BuildGhost(_actor);
                Log(_replay.BuildNote);
                _replay.PlayCam.enabled = true;
                _playImage.texture = _replay.Target; _playImage.uvRect = new Rect(0, 0, 1, 1);
                PlayPcm();
                float start = Time.unscaledTime;
                int tick = 0, vfAcc = 0; var drift = new List<string>();
                while (true)
                {
                    int vf = Mathf.FloorToInt((Time.unscaledTime - start) * _a.Fps);
                    if (vf >= _nextVf || tick >= _track.Count) break;
                    while (tick + 1 < _track.Count && vfAcc + _track[tick].repeats <= vf) { vfAcc += _track[tick].repeats; tick++; }
                    _replay.Apply(_track[tick]);
                    int ri = _cycle == 1 ? RefIndex(vfAcc, _track[tick].repeats) : -1;
                    if (ri >= 0 && !_rerenderTaken[ri])
                    {
                        _rerenderTaken[ri] = true;
                        int k = ri;
                        var live = _refBonesLive[k]; var ghost = Bones(_replay.GhostSkins.Length > 0 ? _replay.GhostSkins[0] : null);
                        if (live != null && ghost != null && live.Length == ghost.Length)
                        {
                            float mean = 0, max = 0;
                            for (int i = 0; i < live.Length; i++) { float d = Vector3.Distance(live[i], ghost[i]); mean += d; max = Mathf.Max(max, d); }
                            drift.Add($"vf{_refVf[k]}: mean={mean / live.Length * 100f:0.00}cm max={max * 100f:0.00}cm bones={live.Length}");
                        }
                        StartCoroutine(GrabRerender(k));
                    }
                    yield return null;
                }
                _sum["bone_drift"] = Q(string.Join(" | ", drift));
                _replay.PlayCam.enabled = false;
                _playImage.texture = null;
            }
            else
            {
                Log("playback: nothing to play for mode " + _a.Mode);
                yield return Wait(10f);
            }
            _audio.End();
            _playAudio.Stop();
            AudioTap.WriteWav(Path.Combine(_out, $"{_a.Tag}-c{_cycle}-playback-heard.wav"), _audio.Snapshot(), _audio.Channels, _audio.SampleRate);
            _playCanvas.gameObject.SetActive(false);
            Log($"PLAY end cycle={_cycle} after {Time.unscaledTime - t0:0.00} s");
        }

        private readonly bool[] _rerenderTaken = new bool[3];

        private IEnumerator GrabRerender(int k)
        {
            yield return new WaitForEndOfFrame();
            var buf = new NativeArray<byte>(FrameBytes, Allocator.Persistent);
            var req = AsyncGPUReadback.RequestIntoNativeArray(ref buf, _replay.Target, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, null);
            req.WaitForCompletion();
            if (!req.hasError) SavePng(buf, Path.Combine(_out, $"{_a.Tag}-rerender-vf{_refVf[k]:000}.png"));
            buf.Dispose();
        }

        private int SlotForVf(int vf)
        {
            for (int s = 0; s < _ring.Count; s++)
                if (vf >= _ring.VideoFrame[s] && vf < _ring.VideoFrame[s] + _ring.Repeats[s]) return s;
            return -1;
        }

        private void PlayPcm()
        {
            int ch = _audio.Channels, rate = _audio.SampleRate;
            int frames = _pcm.Length / Math.Max(1, ch);
            if (frames == 0) return;
            var clip = AudioClip.Create("spike_playback", frames, ch, rate, false);
            clip.SetData(_pcm, 0);
            _playAudio.clip = clip; _playAudio.Play();
        }

        // ---------- export probes (Tasks 3.5, 5.5) ----------
        private IEnumerator ExportProbe()
        {
            if (_a.Mode != "B" && _a.Mode != "C") yield break;
            string outBase = Path.Combine(_out, $"{_a.Tag}-export");
            string vf = (_a.VFlip ? "-vf vflip " : "");
            var sink = new FfmpegSink(FrameBytes, 4);
            sink.Start(_a.FfmpegPath, $"-y -hide_banner -loglevel verbose -f rawvideo -pix_fmt rgba -s {_a.Width}x{_a.Height} -framerate {_a.Fps} -i {{IN}} {vf}-c:v h264_mf -hw_encoding 1 -rate_control cbr -b:v 8M -g 30 -pix_fmt nv12 -f mpegts \"{outBase}-video.ts\"", outBase + "-ffmpeg.log", _a.Transport);
            while (!sink.Ready && !sink.Failed) yield return null;
            var sw = System.Diagnostics.Stopwatch.StartNew();
            int frames = 0;
            if (_a.Mode == "C")
            {
                for (int s = 0; s < _ring.Count; s++)
                {
                    if (!_ring.Ready[s]) continue;
                    int slot; while (!sink.TryAcquire(out slot)) System.Threading.Thread.Sleep(1);
                    NativeArray<byte>.Copy(_ring.Slots[s], sink.Slot(slot));
                    sink.Submit(slot, _ring.Repeats[s]); frames += _ring.Repeats[s];
                }
            }
            else
            {
                if (_replay == null) _replay = new ReplayPlayer(_cam, _mgr, _a.Width, _a.Height);
                if (_actor != null) _replay.BuildGhost(_actor);
                for (int i = 0; i < _track.Count; i++)
                {
                    _replay.Apply(_track[i]);
                    _replay.PlayCam.Render();
                    int slot; while (!sink.TryAcquire(out slot)) System.Threading.Thread.Sleep(1);
                    var req = AsyncGPUReadback.RequestIntoNativeArray(ref sink.Slot(slot), _replay.Target, 0, UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB, null);
                    req.WaitForCompletion();
                    sink.Submit(slot, _track[i].repeats); frames += _track[i].repeats;
                    if ((i & 15) == 15) yield return null;
                }
            }
            double fin = sink.Finish(60000);
            string wav = Path.Combine(_out, $"{_a.Tag}-c1-audio.wav");
            var p = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_a.FfmpegPath,
                $"-y -hide_banner -loglevel error -i \"{outBase}-video.ts\" -i \"{wav}\" -c:v copy -c:a aac -b:a 160k -shortest -movflags +faststart \"{outBase}.mp4\"")
            { UseShellExecute = false, CreateNoWindow = true });
            p.WaitForExit();
            _sum["export_ms"] = F((float)sw.Elapsed.TotalMilliseconds);
            _sum["export_frames"] = frames.ToString();
            _sum["export_encoder_line"] = Q(sink.EncoderLine);
            _sum["export_finish_ms"] = F((float)fin);
            Log($"EXPORT {_a.Mode}: {frames} frames in {sw.Elapsed.TotalMilliseconds:0} ms -> {outBase}.mp4 (mux exit {p.ExitCode}) encoder='{sink.EncoderLine}'");
            sink.Dispose();
        }

        // ---------- survey: find the peak position and render candidate vantages (Task 2.3) ----------
        // Two cycles proved the peak lands on the same spot (25.94, 0.79, 19.00); later surveys need one.
        private const int SurveyPeaks = 1;
        // The world is ~5x human scale: the player CharacterController is 8.86 m tall, so eye height ~7.5 m.
        private const float EyeHeight = 7.5f;
        private IEnumerator Survey()
        {
            var peaks = new List<Vector3>();
            float lastPeakLog = -100;
            while (peaks.Count < SurveyPeaks)
            {
                var act = Live();
                if (act != null && act.IsAtPeak && Time.unscaledTime - lastPeakLog > 5f)
                {
                    lastPeakLog = Time.unscaledTime;
                    peaks.Add(act.transform.position);
                    Log($"SURVEY peak {peaks.Count} at {V(act.transform.position)} bounds.center={V(act.Bounds.center)} size={V(act.Bounds.size)}");
                }
                yield return null;
            }
            // Wait for the third peak, then render the candidates while he is in it.
            while (Live() == null || !Live().IsAtPeak) yield return null;
            var b = Live().Bounds;
            Vector3 ground = Live().transform.position;
            var buf = new NativeArray<byte>(FrameBytes, Allocator.Persistent);
            int idx = 0;
            foreach (float dist in new[] { 18f, 28f })
                for (int k = 0; k < 8; k++)
                {
                    float ang = k * 45f;
                    Vector3 dir = Quaternion.Euler(0, ang, 0) * Vector3.forward;
                    Vector3 pos = ground + dir * dist + Vector3.up * EyeHeight;
                    _cam.transform.position = pos;
                    _cam.transform.rotation = Quaternion.LookRotation(b.center - pos);
                    bool done = false;
                    _tap.CaptureThisFrame(() => _tap.ReadInto(ref buf, r => done = true));
                    yield return null;
                    while (!done) yield return null;
                    SavePng(buf, Path.Combine(_out, $"survey-{idx:00}-d{dist:0}-a{ang:000}.png"));
                    Log($"SURVEY vantage {idx}: d={dist} a={ang} pos={V(pos)} peakOffset={(Live() != null ? Live().PeakOffset : float.NaN):0.00}");
                    idx++;
                }
            buf.Dispose();
        }

        // ---------- control shot (Task 1a) ----------
        private IEnumerator ControlShot()
        {
            yield return Wait(4f);
            var act = Live();
            if (act != null) _cam.transform.rotation = Quaternion.LookRotation(act.Bounds.center - _cam.transform.position);
            var overlays = new List<Canvas>();
            foreach (var c in FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                if (c.isActiveAndEnabled && c.renderMode == RenderMode.ScreenSpaceOverlay) { overlays.Add(c); c.enabled = false; }
            yield return null; yield return null;
            var buf = new NativeArray<byte>(FrameBytes, Allocator.Persistent);
            bool done = false;
            _tap.CaptureThisFrame(() => _tap.ReadInto(ref buf, r => done = !r.hasError));
            yield return new WaitForEndOfFrame();
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_out, "control-screen-noui.png"), shot.EncodeToPNG());
            Log($"CONTROL screen capture {shot.width}x{shot.height} on frame {Time.frameCount}");
            Destroy(shot);
            AsyncGPUReadback.WaitAllRequests();
            yield return null;
            if (done) SavePng(buf, Path.Combine(_out, "control-tap-noui.png"));
            foreach (var c in overlays) c.enabled = true;
            yield return null; yield return null;
            done = false;
            _tap.CaptureThisFrame(() => _tap.ReadInto(ref buf, r => done = !r.hasError));
            yield return new WaitForEndOfFrame();
            shot = ScreenCapture.CaptureScreenshotAsTexture();
            File.WriteAllBytes(Path.Combine(_out, "control-screen-withui.png"), shot.EncodeToPNG());
            Destroy(shot);
            AsyncGPUReadback.WaitAllRequests();
            yield return null;
            if (done) SavePng(buf, Path.Combine(_out, "control-tap-withui.png"));
            _sum["control_passes"] = _tap.PassesRecorded.ToString();
            _sum["control_backbuffer_skips"] = _tap.BackbufferSkips.ToString();
            buf.Dispose();
        }

        // ---------- fail-soft (AC4, NFR8) ----------
        private IEnumerator FailSoft()
        {
            yield return Wait(3f);
            int framesBefore;
            // 1) wrong ffmpeg path
            _a.Mode = "A"; string realPath = _a.FfmpegPath; _a.FfmpegPath = "C:/does/not/exist/ffmpeg.exe";
            _sink = new FfmpegSink(FrameBytes, 6); _a.Tag = "failsoft-badpath"; _cycle = 1;
            framesBefore = Time.frameCount;
            yield return Record();
            Log($"FAILSOFT badpath: game ran {Time.frameCount - framesBefore} frames through the recording; failed={_sink.Failed} reason='{_sink.FailReason}'");
            _sink.Dispose();
            // 2) ffmpeg killed mid-clip
            _a.FfmpegPath = realPath; _sink = new FfmpegSink(FrameBytes, 6); _a.Tag = "failsoft-kill"; _cycle = 1;
            framesBefore = Time.frameCount;
            StartCoroutine(KillAfter(3f));
            yield return Record();
            Log($"FAILSOFT kill: game ran {Time.frameCount - framesBefore} frames; failed={_sink.Failed} reason='{_sink.FailReason}' written={_sink.FramesWritten}");
            string partial = Path.Combine(_out, "failsoft-kill-c1-video.ts");
            Log($"FAILSOFT kill: partial fragmented file exists={File.Exists(partial)} bytes={(File.Exists(partial) ? new FileInfo(partial).Length : -1)}");
            _sink.Dispose(); _sink = null;
            // 3) full readback ring
            _a.Mode = "C"; _ring = new RawRing(FrameBytes, 30); _a.Tag = "failsoft-ring"; _cycle = 1;
            framesBefore = Time.frameCount;
            yield return Record();
            Log($"FAILSOFT ring: game ran {Time.frameCount - framesBefore} frames; overflow={_ring.Overflow} stored={_ring.Count}");
            _ring.Dispose(); _ring = null;
            _a.Mode = "failsoft";
        }

        private IEnumerator KillAfter(float s)
        {
            yield return Wait(s);
            Log("FAILSOFT: killing ffmpeg now (deliberate)");
            _sink?.KillForTest();
        }

        // ---------- output ----------
        private bool _finished;
        private void Finish(string why)
        {
            if (_finished) return;
            _finished = true;
            Log("FINISH: " + why);
            try { WriteOutputs(); } catch (Exception e) { GameLog.Warn("RecordingSpike", "writing outputs failed: " + e.Message); }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void WriteOutputs()
        {
            var sb = new StringBuilder("i,t,dt_ms,phase,captured,repeats,peak_offset,dist_m,main_thread_ms\n");
            for (int i = 0; i < _n; i++)
                sb.Append(i).Append(',').Append(F(_t[i])).Append(',').Append(F(_dt[i])).Append(',').Append(PhaseNames[_ph[i]]).Append(',')
                  .Append(_cap[i]).Append(',').Append(_rep[i]).Append(',').Append(F(_peak[i])).Append(',').Append(F(_dist[i])).Append(',').Append(F(_mt[i])).Append('\n');
            File.WriteAllText(Path.Combine(_out, $"{_a.Tag}-frames.csv"), sb.ToString());
            File.WriteAllText(Path.Combine(_out, $"{_a.Tag}-seconds.csv"), _sec.ToString());
            File.WriteAllText(Path.Combine(_out, $"{_a.Tag}-events.log"), _events.ToString());
            var js = new StringBuilder("{\n");
            int k = 0;
            foreach (var kv in _sum) js.Append(k++ > 0 ? ",\n" : "").Append("  \"").Append(kv.Key).Append("\": ").Append(kv.Value);
            js.Append("\n}\n");
            File.WriteAllText(Path.Combine(_out, $"{_a.Tag}-summary.json"), js.ToString());
        }

        private void OnDestroy()
        {
            if (_mainThread.Valid) _mainThread.Dispose();
            _tap?.Dispose();
            _sink?.Dispose();
            _ring?.Dispose();
            _replay?.Dispose();
            AsyncGPUReadback.WaitAllRequests();
            for (int i = 0; i < 3; i++) if (_refBuf[i].IsCreated) _refBuf[i].Dispose();
            // Editor: put back everything the harness changed (play-mode exit resets the scene anyway).
            if (_tpc != null) _tpc.enabled = _tpcWasEnabled;
            if (_ctrl != null) _ctrl.SetInputSuppressed(false);
            if (_photo != null) _photo.SetPhotoMode(false);
            if (_audio != null) Destroy(_audio);
            Application.targetFrameRate = _prevTargetFps;
        }

        private static string F(float v) => float.IsNaN(v) ? "" : v.ToString("0.###", CultureInfo.InvariantCulture);
        private static string Q(string s) => "\"" + (s ?? "").Replace("\\", "/").Replace("\"", "'") + "\"";
        private static string V(Vector3 v) => string.Format(CultureInfo.InvariantCulture, "{0:0.00},{1:0.00},{2:0.00}", v.x, v.y, v.z);
    }
}
