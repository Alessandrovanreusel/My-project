#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.Rendering;
using CameraGame.Core;
using CameraGame.Events;
using CameraGame.PhotoMode;
using CameraGame.UI;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace CameraGame.Recording
{
    /// <summary>
    /// Story 1.15's verification rig (Task 10), driven by <c>Tools/Recording/Record Clip Shoot (Play)</c>. It runs
    /// in the REAL SampleScene with its real wiring and drives the real paths: a virtual gamepad through
    /// PlayerInput for Record / raise / look / zoom, and the controller's own methods for the routes a gamepad
    /// cannot reach (the gallery's suppression). Everything it measures is written to the evidence folder;
    /// nothing is asserted. Lives in the runtime assembly behind UNITY_EDITOR (an Editor-assembly MonoBehaviour
    /// resolves as "missing script" on a scene object in play mode).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class RecordClipShootRunner : MonoBehaviour
    {
        // Wired by the editor rig from the shipped scene.
        public PhotoModeController photo;
        public ClipRecorder recorder;
        public RecordingHud hud;
        public EventManager manager;
        public Camera cam;
        public ThirdPersonController player;
        public ClipRecordedChannel channel;
        public RecordingConfig shippedConfig;
        public EventActor actorPrefab;
        public string outputDir;
        public float eyeHeight = 1.65f;
        public string phases = "0ABCDEF";

        private readonly StringBuilder _log = new StringBuilder();
        private readonly StringBuilder _csv = new StringBuilder();
        private Gamepad _pad;
        private GamepadState _padState;
        private InputSettings.BackgroundBehavior _prevBg;
        private InputSettings.EditorInputBehaviorInPlayMode _prevEditorInput;
        private float _prevMaxClip;
        private bool _configTouched;
        private int _captureActions;
        private int _raises;
        private readonly List<RecordedClip> _raised = new List<RecordedClip>();
        private RecordedClip _lastRaised;
        private int _started, _stopped;
        private long _startedTs, _stoppedTs;
        private StopReason _lastReason;
        private bool _claimNext;
        private readonly List<string> _logLines = new List<string>();
        private bool _collectLogs;
        private Text _label;
        private GameObject _labelCanvas;
        private RecordClipShootBeepProbe _beepProbe;
        private AudioSource _clickSource;
        private AudioClip _click;
        private RecordClipShootBoneLog _boneLog;
        private Vector3 _vantage;
        private float _vantageYaw;

        // Render-time log (beginCameraRendering on the main camera), preallocated: AC6 measures this rig's frames too.
        private const int LogCap = 40000;
        private readonly int[] _rFrame = new int[LogCap];
        private readonly Vector3[] _rCamPos = new Vector3[LogCap];
        private readonly Quaternion[] _rCamRot = new Quaternion[LogCap];
        private readonly float[] _rFov = new float[LogCap];
        private readonly Vector3[] _rActorPos = new Vector3[LogCap];
        private readonly bool[] _rActorLive = new bool[LogCap];
        private readonly double[] _rRealtime = new double[LogCap];
        private readonly double[] _rDsp = new double[LogCap];
        private int _rCount;

        // A/V markers: a click scheduled at a DSP time, and a zoom toggle on the first frame whose dspTime reaches it.
        private readonly List<double> _markerDsp = new List<double>();
        private int _markerNext;
        private readonly List<int> _markerFrame = new List<int>();
        private double _beepControlAt = -1;

        private static long Ts() => Stopwatch.GetTimestamp();
        private static double Ms(long from, long to) => (to - from) * 1000.0 / Stopwatch.Frequency;
        private static string F(double v, string fmt = "0.000") => v.ToString(fmt, CultureInfo.InvariantCulture);

        private void Line(string s)
        {
            _log.AppendLine(s);
            Debug.Log("[RecordClipShoot] " + s);
        }

        private void Start()
        {
            Directory.CreateDirectory(outputDir);
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            Line("Record Clip Shoot — Story 1.15. SampleScene, real wiring. " + DateTime.Now.ToString("u", CultureInfo.InvariantCulture));
            Line("⚠ ERRORS AND WARNINGS THIS RIG PROVOKES ON PURPOSE (Phase F, between its markers): " +
                 "[Recording] ClipRecordedChannel unassigned (error), RecordingConfig unassigned (error), No ListenerTap assigned (warning), " +
                 "No EventManager assigned (warning), RecordingConfig: maxClipSeconds ... (warnings, 3), " +
                 "The audio device or its configuration changed mid-clip (warning). Anything else is a finding.");
            Application.logMessageReceived += OnLog;
            recorder.Started += OnStarted;
            recorder.Stopped += OnStopped;
            channel.Raised += OnRaised;
            RenderPipelineManager.beginCameraRendering += OnBeginCamera;
            BuildLabel();
            SetupAudioProbes();
            SetupInput();
            _boneLog = gameObject.AddComponent<RecordClipShootBoneLog>();

            bool ok = true;
            try { _prevMaxClip = shippedConfig.maxClipSeconds; }
            catch (Exception e) { Line("config unreadable: " + e.Message); ok = false; }

            yield return null;
            Line($"Screen {Screen.width}x{Screen.height}; HUD canvas scale factor {F(hud.GetComponent<Canvas>().scaleFactor)}; " +
                 $"audio {AudioSettings.outputSampleRate} Hz, {AudioSettings.speakerMode}, DSP buffer {AudioSettings.GetConfiguration().dspBufferSize}; " +
                 $"recorder capacity {recorder.CapacityBytesPerClip / 1048576.0:0.0} MB per clip x {recorder.StoragesCreated} blocks");
            if (Screen.width != 1920 || Screen.height != 1080)
                Line("⚠ NOT 1920x1080: legibility read from these captures does not carry to 1080p.");

            if (ok)
            {
                PlaceAtVantage();
                for (int i = 0; i < 30; i++) yield return null;
                if (phases.Contains("0")) yield return Phase0();
                if (phases.Contains("A")) yield return PhaseA();
                if (phases.Contains("B")) yield return PhaseB();
                if (phases.Contains("E")) yield return PhaseE();
                if (phases.Contains("C")) yield return PhaseC();
                if (phases.Contains("D")) yield return PhaseD();
                if (phases.Contains("F")) yield return PhaseF();
            }

            Teardown();
            File.WriteAllText(Path.Combine(outputDir, "report.txt"), _log.ToString());
            Line("DONE — report at " + outputDir + "/report.txt. Leaving play mode mid-clip (F7: the exit path).");

            // F7: leave play mode WITH a clip running, so the teardown path is exercised on the real recorder.
            photo.SetPhotoMode(true);
            photo.ToggleRecording();
            Debug.Log("[RecordClipShoot] F7 marker: exiting play mode while recording=" + recorder.IsRecording);
            UnityEditor.EditorApplication.isPlaying = false;
        }

        // =====================================================================================================
        // Setup
        // =====================================================================================================

        private void SetupInput()
        {
            // The defaults drop a non-background device's input while the editor is unfocused (an unattended run),
            // so the rig lets input through for its own duration and puts the settings back in Teardown.
            _prevBg = InputSystem.settings.backgroundBehavior;
            _prevEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _pad = InputSystem.AddDevice<Gamepad>("RecordClipShootPad");
            _padState = new GamepadState();
            InputSystem.QueueStateEvent(_pad, _padState);

            var pi = player.GetComponent<PlayerInput>();
            InputAction capture = pi.actions.FindAction(GameConstants.InputActions.Capture);
            capture.performed += _ => _captureActions++;
            Line($"Input: virtual gamepad added (id {_pad.deviceId}); background {_prevBg} -> IgnoreFocus for the run.");
        }

        private void SetupAudioProbes()
        {
            AudioSource beep = hud.GetComponent<AudioSource>();
            _beepProbe = beep.gameObject.AddComponent<RecordClipShootBeepProbe>();

            _clickSource = gameObject.AddComponent<AudioSource>();
            _clickSource.playOnAwake = false;
            _clickSource.spatialBlend = 0f;
            _clickSource.bypassListenerEffects = false;   // the marker MUST reach the listener tap
            SetupClick();
        }

        private void SetupClick()
        {
            int rate = AudioSettings.outputSampleRate;
            var data = new float[rate / 100];           // 10 ms, 1 kHz square at 0.9
            for (int i = 0; i < data.Length; i++) data[i] = ((i * 1000 * 2 / rate) % 2 == 0) ? 0.9f : -0.9f;
            _click = AudioClip.Create("RigClick", data.Length, 1, rate, false);
            _click.SetData(data, 0);
        }

        private void BuildLabel()
        {
            _labelCanvas = new GameObject("RigLabelCanvas");
            var c = _labelCanvas.AddComponent<Canvas>();
            c.renderMode = RenderMode.ScreenSpaceOverlay;
            c.sortingOrder = 900;
            var go = new GameObject("RigLabel", typeof(RectTransform));
            go.transform.SetParent(_labelCanvas.transform, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(12, 12);
            rt.sizeDelta = new Vector2(1300, 120);
            _label = go.AddComponent<Text>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.fontSize = 20;
            _label.color = Color.yellow;
            _label.alignment = TextAnchor.LowerLeft;
            go.AddComponent<Outline>().effectColor = Color.black;
        }

        private void SetLabel(string caption)
        {
            _label.text = $"RIG SNAPSHOT — {caption}\nrecorder: IsRecording {recorder.IsRecording} · ElapsedSeconds {F(recorder.ElapsedSeconds)} " +
                          $"· HUD timecode '{hud.TimecodeText}' · REC {hud.IsShowingRec} · STBY {hud.IsShowingStandby} · frame {Time.frameCount}" +
                          $"\nclips raised {_raises} · last stop {(_stopped > 0 ? _lastReason.ToString() : "-")} · mode {photo.Mode}";
        }

        private void PlaceAtVantage()
        {
            // Surveyed 2026-11-15: from (32, 8) the eye has a clear line to every waypoint of the drunk's route
            // (x 31.8 -> 20.4 at z 19), 11-16 m away. Ground height read here, eye height from PlayerConfig (ADR-5).
            Vector3 top = new Vector3(32f, 60f, 8f);
            float y = Physics.Raycast(top, Vector3.down, out RaycastHit h, 120f) ? h.point.y : 0.5f;
            _vantage = new Vector3(32f, y + 0.05f, 8f);
            _vantageYaw = Mathf.Atan2(26f - 32f, 19f - 8f) * Mathf.Rad2Deg;
            ResetVantage();
            Line($"Vantage ({F(_vantage.x, "0.0")}, {F(_vantage.y, "0.00")}, {F(_vantage.z, "0.0")}) yaw {F(_vantageYaw, "0.0")}°, eye {F(eyeHeight, "0.00")} m");
        }

        private void ResetVantage()
        {
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.SetPositionAndRotation(_vantage, Quaternion.Euler(0f, _vantageYaw, 0f));
            cc.enabled = true;
        }

        private void Teardown()
        {
            if (recorder.IsRecording) recorder.Stop(StopReason.PlayerPressed);
            RestoreConfig();
            if (_pad != null) { InputSystem.RemoveDevice(_pad); _pad = null; }
            InputSystem.settings.backgroundBehavior = _prevBg;
            InputSystem.settings.editorInputBehaviorInPlayMode = _prevEditorInput;
            var pi = player.GetComponent<PlayerInput>();
            Line($"Teardown: pad removed; PlayerInput scheme now '{pi.currentControlScheme}', devices {pi.devices.Count}; " +
                 $"input settings back to {InputSystem.settings.backgroundBehavior}; config maxClipSeconds {F(shippedConfig.maxClipSeconds)}; " +
                 $"timeScale {Time.timeScale}");
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
            channel.Raised -= OnRaised;
            recorder.Started -= OnStarted;
            recorder.Stopped -= OnStopped;
            Application.logMessageReceived -= OnLog;
            if (_labelCanvas != null) Destroy(_labelCanvas);
        }

        private void RestoreConfig()
        {
            if (!_configTouched) return;
            shippedConfig.maxClipSeconds = _prevMaxClip;
            UnityEditor.EditorUtility.SetDirty(shippedConfig);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(shippedConfig);
            _configTouched = false;
        }

        private void OnDestroy()
        {
            // A run that ends early (an exception, a manual stop) still removes its device and puts the input back.
            if (_pad != null) { InputSystem.RemoveDevice(_pad); _pad = null; InputSystem.settings.backgroundBehavior = _prevBg; InputSystem.settings.editorInputBehaviorInPlayMode = _prevEditorInput; }
            RestoreConfig();
            RenderPipelineManager.beginCameraRendering -= OnBeginCamera;
        }

        // =====================================================================================================
        // Listeners
        // =====================================================================================================

        private void OnStarted() { _started++; _startedTs = Ts(); }
        private void OnStopped(StopReason r) { _stopped++; _stoppedTs = Ts(); _lastReason = r; }

        private void OnRaised(RecordedClip clip)
        {
            _raises++;
            _lastRaised = clip;
            _raised.Add(clip);
            if (_claimNext) { _claimNext = false; clip.Claim(this); }
        }

        private void OnLog(string condition, string stack, LogType type)
        {
            if (_collectLogs && !condition.StartsWith("[RecordClipShoot]", StringComparison.Ordinal))
                _logLines.Add(type + ": " + condition);
        }

        private void OnBeginCamera(ScriptableRenderContext ctx, Camera c)
        {
            if (c != cam || _rCount >= LogCap) return;
            int i = _rCount++;
            _rFrame[i] = Time.frameCount;
            c.transform.GetPositionAndRotation(out _rCamPos[i], out _rCamRot[i]);
            _rFov[i] = c.fieldOfView;
            _rRealtime[i] = Time.realtimeSinceStartupAsDouble;
            _rDsp[i] = AudioSettings.dspTime;
            IReadOnlyList<EventActor> a = manager.ActiveActors;
            bool live = a.Count > 0 && a[0] != null && a[0].isActiveAndEnabled;
            _rActorLive[i] = live;
            _rActorPos[i] = live ? a[0].transform.position : default;
        }

        // Marker drive: the first frame whose main-thread dspTime reaches a scheduled click toggles the zoom, so the
        // picture changes (the FOV starts to move, recorded in the track) on the frame nearest the click.
        private void Update()
        {
            if (_markerNext < _markerDsp.Count && AudioSettings.dspTime >= _markerDsp[_markerNext])
            {
                _markerFrame.Add(Time.frameCount);
                photo.SetZoom(_markerNext % 2 == 0 ? 1f : 0f);
                _markerNext++;
            }
        }

        // =====================================================================================================
        // Input helpers (virtual gamepad)
        // =====================================================================================================

        private void Pad(float? lt = null, float? rt = null, Vector2? rstick = null, bool? dpadUp = null)
        {
            if (lt.HasValue) _padState.leftTrigger = lt.Value;
            if (rt.HasValue) _padState.rightTrigger = rt.Value;
            if (rstick.HasValue) _padState.rightStick = rstick.Value;
            if (dpadUp.HasValue) _padState = _padState.WithButton(GamepadButton.DpadUp, dpadUp.Value);
            InputSystem.QueueStateEvent(_pad, _padState);
        }

        /// <summary>One Record press: RT to 1.0 (past the 0.5 press point), then back to 0 two frames later.</summary>
        private IEnumerator PressRecord()
        {
            Pad(rt: 1f);
            yield return null;
            yield return null;
            Pad(rt: 0f);
            yield return null;
        }

        private IEnumerator Raise(bool up)
        {
            Pad(lt: up ? 1f : 0f);
            for (int i = 0; i < 4; i++) yield return null;
        }

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        // =====================================================================================================
        // Captures
        // =====================================================================================================

        private Texture2D _last;

        /// <summary>The screen as the player saw it (Overlay UI included), grabbed at the end of the frame with the
        /// camera rendering to the screen (CLAUDE.md: Overlay canvases are invisible to cam.Render()).</summary>
        private IEnumerator Grab(string name, string caption)
        {
            SetLabel(caption);
            yield return new WaitForEndOfFrame();
            if (_last != null) Destroy(_last);
            _last = ScreenCapture.CaptureScreenshotAsTexture();
            if (name != null) File.WriteAllBytes(Path.Combine(outputDir, name + ".png"), _last.EncodeToPNG());
        }

        private static float Fraction(Texture2D t, RectInt r, Func<Color32, bool> test)
        {
            Color32[] px = t.GetPixels32();
            int n = 0, all = 0;
            for (int y = Mathf.Max(0, r.yMin); y < Mathf.Min(t.height, r.yMax); y++)
                for (int x = Mathf.Max(0, r.xMin); x < Mathf.Min(t.width, r.xMax); x++)
                {
                    all++;
                    if (test(px[y * t.width + x])) n++;
                }
            return all == 0 ? 0f : n / (float)all;
        }

        private static bool IsRecRed(Color32 c) => c.r > 170 && c.g < 110 && c.b < 110 && c.r - c.g > 90;

        private RectInt RecRegion()
        {
            var corners = new Vector3[4];
            hud.RecRect.GetWorldCorners(corners);   // Overlay canvas: world corners are screen pixels
            return new RectInt((int)corners[0].x - 8, (int)corners[0].y - 8,
                               (int)(corners[2].x - corners[0].x) + 16, (int)(corners[2].y - corners[0].y) + 16);
        }

        // =====================================================================================================
        // Phase 0 — controls
        // =====================================================================================================

        private IEnumerator Phase0()
        {
            Line("\n== Phase 0 — controls ==");
            yield return Grab("p0_scene", "Phase 0: known 3D content (camera lowered)");
            Color32[] px = _last.GetPixels32();
            double sum = 0, sq = 0; int white = 0;
            foreach (Color32 c in px)
            {
                double l = 0.299 * c.r + 0.587 * c.g + 0.114 * c.b;
                sum += l; sq += l * l;
                if (l > 245) white++;
            }
            double mean = sum / px.Length, sd = Math.Sqrt(Math.Max(0, sq / px.Length - mean * mean));
            Line($"3D frame: luma mean {F(mean, "0.0")}, sd {F(sd, "0.0")}, near-white {F(100.0 * white / px.Length, "0.00")} % (must not be uniform or near-white)");

            var mGo = new GameObject("RigMarkerCanvas");
            var mc = mGo.AddComponent<Canvas>();
            mc.renderMode = RenderMode.ScreenSpaceOverlay;
            mc.sortingOrder = 1000;
            var img = new GameObject("Marker", typeof(RectTransform)).AddComponent<Image>();
            img.transform.SetParent(mGo.transform, false);
            var rt = (RectTransform)img.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            img.color = new Color(1f, 0f, 1f, 1f);
            yield return Grab(null, "marker");
            var full = new RectInt(0, 0, _last.width, _last.height);
            float on = Fraction(_last, full, c => c.r > 240 && c.g < 15 && c.b > 240);
            mGo.SetActive(false);
            yield return null;
            yield return Grab(null, "marker off");
            float off = Fraction(_last, full, c => c.r > 240 && c.g < 15 && c.b > 240);
            Destroy(mGo);
            Line($"Overlay marker: {F(on * 100, "0.00")} % on, {F(off * 100, "0.00")} % off (Story 1.12 measured 100.00 / 0.00). " +
                 "This rig never freezes time, so the timeScale control does not apply.");
        }

        // =====================================================================================================
        // Phase A — through the real input (AC1, AC4)
        // =====================================================================================================

        private IEnumerator PhaseA()
        {
            Line("\n== Phase A — the real input path (virtual gamepad, PlayerInput Send Messages) ==");

            // A1: Record in Walk is a no-op. The press must be proven to have arrived first.
            int actions0 = _captureActions, started0 = _started, raises0 = _raises;
            _beepProbe.Arm();
            yield return PressRecord();
            yield return Wait(0.4f);
            bool beeped = _beepProbe.FirstTs != 0;
            _beepProbe.Disarm();
            yield return Grab("a1_walk_record_press", "A1: Record pressed in Walk (camera lowered)");
            float recWalk = Fraction(_last, RecRegion(), IsRecRed);
            Line($"A1 Walk: Capture action performed +{_captureActions - actions0} (the press ARRIVED), recorder started +{_started - started0}, " +
                 $"clips raised +{_raises - raises0}, beep heard {beeped}, REC red pixels {F(recWalk * 100, "0.00")} %, recording {recorder.IsRecording}");

            // A2: raise and look at STBY.
            yield return Raise(true);
            yield return Wait(0.6f);
            yield return Grab("a2_raised_standby", "A2: camera raised, not recording (STBY expected)");
            Line($"A2 raised: mode {photo.Mode}, STBY {hud.IsShowingStandby}, REC {hud.IsShowingRec}");

            // A3: press Record, measure the parts.
            actions0 = _captureActions; started0 = _started;
            _beepProbe.Arm();
            yield return new WaitForEndOfFrame();
            long tQueue = Ts();
            Pad(rt: 1f);
            long tFrame = 0; int framesToRec = -1; float recFrac = 0f;
            for (int f = 0; f < 12; f++)
            {
                yield return new WaitForEndOfFrame();
                if (_started == started0) continue;
                long tGrab = Ts();
                if (_last != null) Destroy(_last);
                _last = ScreenCapture.CaptureScreenshotAsTexture();
                recFrac = Fraction(_last, RecRegion(), IsRecRed);
                if (recFrac > 0.01f) { tFrame = tGrab; framesToRec = f; break; }
            }
            Pad(rt: 0f);
            File.WriteAllBytes(Path.Combine(outputDir, "a3_first_rec_frame.png"), _last.EncodeToPNG());
            yield return Wait(0.5f);
            long tBeep = _beepProbe.FirstTs;
            AudioSettings.GetDSPBufferSize(out int bufLen, out int bufCount);
            double outMs = 1000.0 * bufLen * bufCount / AudioSettings.outputSampleRate;
            Line($"A3 press: action +{_captureActions - actions0}, started +{_started - started0}. Parts, from the queued press: " +
                 $"input -> handler {F(Ms(tQueue, _startedTs), "0.0")} ms; handler -> first frame with REC {(tFrame != 0 ? F(Ms(_startedTs, tFrame), "0.0") : "NEVER")} ms " +
                 $"(end of frame {framesToRec} after the queue, REC red {F(recFrac * 100, "0.00")} % of its region); handler -> beep's first non-zero sample in the mix " +
                 $"{(tBeep != 0 ? F(Ms(_startedTs, tBeep), "0.0") : "NEVER")} ms, plus the output buffers {bufCount} x {bufLen} = {F(outMs, "0.0")} ms to the speaker at worst.");
            Line($"   so press -> REC on screen {(tFrame != 0 ? F(Ms(tQueue, tFrame), "0.0") : "-")} ms; press -> beep at the speaker <= {(tBeep != 0 ? F(Ms(tQueue, tBeep) + outMs, "0.0") : "-")} ms (NFR2 budget 200 ms; reported, not asserted)");
            _beepProbe.SaveWav(Path.Combine(outputDir, "a3_start_beep_at_source.wav"));
            _beepProbe.Disarm();

            yield return Wait(1.7f);
            yield return Grab("a4_recording_0002", "A4: recording, ~2 s in");
            Line($"A4: timecode on screen '{hud.TimecodeText}' against ElapsedSeconds {F(recorder.ElapsedSeconds)}");

            // A5: stop with the stop beep.
            int raisesBefore = _raises;
            _beepProbe.Arm();
            yield return PressRecord();
            yield return Wait(0.5f);
            _beepProbe.SaveWav(Path.Combine(outputDir, "a5_stop_beep_at_source.wav"));
            int bursts = _beepProbe.CountBursts();
            _beepProbe.Disarm();
            yield return Grab("a5_stopped", "A5: Record pressed again (stopped)");
            Line($"A5 stop: raised +{_raises - raisesBefore} ({_lastRaised}), reason {_lastReason}, REC {hud.IsShowingRec}, STBY {hud.IsShowingStandby}; " +
                 $"stop beep bursts at its source: {bursts} (start beep: {RecordClipShootBeepProbe.CountBurstsIn(Path.Combine(outputDir, "a3_start_beep_at_source.wav"))})");
            yield return Raise(false);
            Line($"A6 lowered: mode {photo.Mode}; control scheme '{player.GetComponent<PlayerInput>().currentControlScheme}'");
        }

        // =====================================================================================================
        // Phase B — every way a recording ends (AC2, AC6), and Phase E's memory series over it
        // =====================================================================================================

        private IEnumerator PhaseB()
        {
            Line("\n== Phase B — every way a recording ends (and Phase E: memory over the clips) ==");
            _csv.AppendLine("case,clip,reason,raises,duration_s,wallclock_s,diff_ms,footage_alive,prev_disposed,storages_created,storages_free,alloc_MB,reserved_MB");

            yield return Raise(true);
            yield return Wait(0.3f);
            yield return OneClip("B1 press/press", 2f, () => PressRecord());
            yield return OneClip("B2 press, release LT", 2f, () => Raise(false), lowered: true);
            yield return Raise(true);
            yield return OneClip("B3 press, gallery suppression", 1.5f, () => { photo.SetRaiseSuppressed(true); return null; }, lowered: true);
            photo.SetRaiseSuppressed(false);
            yield return Raise(false);   // LT is still held from before; a Value action only re-raises on a change
            yield return Raise(true);
            Line($"   after suppression off and LT re-pressed: mode {photo.Mode}");

            // B4: the length limit, on the shipped asset (restored and read back right after).
            shippedConfig.maxClipSeconds = 3f;
            _configTouched = true;
            yield return OneClip("B4 length limit (3 s)", 4.5f, null);
            RestoreConfig();
            var reread = UnityEditor.AssetDatabase.LoadAssetAtPath<RecordingConfig>(UnityEditor.AssetDatabase.GetAssetPath(shippedConfig));
            Line($"   config restored: maxClipSeconds {F(reread.maxClipSeconds)} (was {F(_prevMaxClip)})");

            // B5: rapid press, press, press.
            int r0 = _raises, s0 = _started;
            for (int i = 0; i < 3; i++) { Pad(rt: 1f); yield return null; Pad(rt: 0f); yield return null; }
            bool recAfter3 = recorder.IsRecording;
            yield return Wait(1f);
            yield return PressRecord();
            Line($"B5 rapid x3: started +{_started - s0}, raised +{_raises - r0} after the 4th press, recording after 3 presses {recAfter3}; " +
                 $"durations {Durations(r0)}");

            // B6: ten consecutive clips, the second cycle onward is where bugs hide.
            for (int i = 1; i <= 10; i++)
                yield return OneClip($"B6 cycle {i:00}", 1.2f, () => PressRecord());

            // B7: a listener that claims the clip owns it; the recorder does not hold it.
            RecordedClip heldBefore = recorder.LastClip;
            _claimNext = true;
            yield return OneClip("B7 claimed by a listener", 1f, () => PressRecord());
            RecordedClip claimed = _lastRaised;
            Line($"B7: claimed by the rig {claimed.Owner == (object)this}; recorder still holds {(recorder.LastClip == heldBefore ? "its previous clip" : "something else")}; " +
                 $"storages free {recorder.StoragesFree}; rig disposes it -> ");
            claimed.Footage.Dispose();
            Line($"   footage bytes {claimed.Footage.Bytes}, storages free {recorder.StoragesFree}, created {recorder.StoragesCreated}");

            File.WriteAllText(Path.Combine(outputDir, "phaseB_clips.csv"), _csv.ToString());

            yield return Raise(false);
        }

        private string Durations(int fromRaise)
        {
            var sb = new StringBuilder();
            for (int i = fromRaise; i < _raised.Count; i++) sb.Append(F(_raised[i].DurationSeconds)).Append(" s ");
            return sb.ToString();
        }

        private IEnumerator OneClip(string label, float seconds, Func<IEnumerator> stop, bool lowered = false)
        {
            RecordedClip prev = recorder.LastClip;
            int raises0 = _raises;
            yield return PressRecord();
            long t0 = _startedTs;
            yield return Wait(seconds);
            if (stop != null) { IEnumerator e = stop(); if (e != null) yield return e; }
            yield return null;
            double wall = Ms(t0, _stoppedTs) / 1000.0;
            RecordedClip clip = _lastRaised;
            bool alive = clip != null && clip.Footage != null && !clip.Footage.IsDisposed;
            bool prevDisposed = prev != null && prev.Footage.IsDisposed;
            string line = string.Format(CultureInfo.InvariantCulture,
                "{0},{1},{2},{3},{4:0.000},{5:0.000},{6:0.0},{7},{8},{9},{10},{11:0.0},{12:0.0}",
                label, clip?.Id, clip?.StopReason, _raises - raises0, clip?.DurationSeconds ?? 0, wall,
                ((clip?.DurationSeconds ?? 0) - wall) * 1000.0, alive, prev == null ? "n/a" : prevDisposed.ToString(),
                recorder.StoragesCreated, recorder.StoragesFree,
                UnityEngine.Profiling.Profiler.GetTotalAllocatedMemoryLong() / 1048576.0,
                UnityEngine.Profiling.Profiler.GetTotalReservedMemoryLong() / 1048576.0);
            _csv.AppendLine(line);
            Line(line);
            if (lowered && photo.IsPhotoMode) Line("   ⚠ camera still raised after a lowering route");
        }

        private IEnumerator PhaseE()
        {
            Line("\n== Phase E: managed allocation per frame ==");
            yield return Raise(true);
            yield return Wait(0.3f);

            // GC.GetAllocatedBytesForCurrentThread reads 0 around a new byte[1000] in this Mono (run 2 of this rig showed
            // it; its "0 B" proved nothing), and GC.GetTotalMemory moves in 4 KB blocks. So the paths run thousands of
            // times inside one frame: any managed allocation is >= 16 B, so an allocating path would grow the heap by
            // >= 16 B per call. A positive control proves the measurement sees that.
            int n = 9000;
            var keep = new object[n];
            long ctl = HeapGrowth(() => { for (int i = 0; i < n; i++) keep[i] = new object(); }, out int ctlGc);
            GC.KeepAlive(keep);

            // (a) the FULL sampling path with the live drunk: a rig-owned backend on the real camera and EventManager,
            //     every call taking a tick, so camera, actor and Animator-layer sampling all run.
            var backend = new ReplayBackend(cam, manager, null, RecordingConfig.MaxActorsPerTickOf(shippedConfig),
                                            RecordingConfig.MaxAnimatorLayersOf(shippedConfig));
            backend.Prepare(n / 30f + 1f);
            backend.Begin(AudioSettings.dspTime, n / 30f + 1f);
            backend.Sample(0.001);
            long full = HeapGrowth(() => { for (int i = 1; i < n; i++) { backend.Sample(i / 30.0 + 0.001); backend.TryGetStopRequest(out _); } }, out int fullGc);
            var ff = (ReplayFootage)backend.End();
            int actors = ff.ActorSampleCount, layers = ff.LayerSampleCount, ticks = ff.TickCount;
            ff.Dispose();
            backend.Destroy();

            // (b) the real recorder's own LateUpdate, called through a delegate while it records.
            yield return PressRecord();
            for (int i = 0; i < 5; i++) yield return null;
            var late = (Action)Delegate.CreateDelegate(typeof(Action), recorder, "LateUpdate");
            late();
            long recGrowth = HeapGrowth(() => { for (int i = 0; i < n; i++) late(); }, out int recGc);
            yield return PressRecord();

            Line($"Heap growth over {n} calls (GetTotalMemory, 4 KB blocks; >= {n * 16} B if a call allocated): " +
                 $"positive control (new object() each call) {ctl} B, GCs {ctlGc}; FULL sampling path with the live drunk " +
                 $"({ticks} ticks, {actors} actor samples, {layers} layer samples) {full} B, GCs {fullGc}; the recorder's " +
                 $"LateUpdate while recording {recGrowth} B, GCs {recGc}.");

            Line("Whole-frame view (ProfilerRecorder 'GC Allocated In Frame'); in the editor it includes the editor's own allocations:");
            using (var gc = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame", 1))
            {
                var idle = new long[180];
                var rec = new long[180];
                int collections0 = GC.CollectionCount(0);
                for (int i = 0; i < 20; i++) yield return null;
                for (int i = 0; i < idle.Length; i++) { yield return null; idle[i] = gc.LastValue; }
                yield return PressRecord();
                for (int i = 0; i < 20; i++) yield return null;
                for (int i = 0; i < rec.Length; i++) { yield return null; rec[i] = gc.LastValue; }
                yield return PressRecord();
                Line($"idle (raised, not recording): {Summ(idle)}");
                Line($"recording:                    {Summ(rec)}");
                Line($"Gen-0 collections during the two windows: {GC.CollectionCount(0) - collections0}. " +
                     "The HUD's timecode builds one short string per displayed second (by design, not per frame).");
            }
            yield return Raise(false);
        }

        private static long HeapGrowth(Action body, out int collections)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            int gc0 = GC.CollectionCount(0);
            long before = GC.GetTotalMemory(false);
            body();
            long after = GC.GetTotalMemory(false);
            collections = GC.CollectionCount(0) - gc0;
            return after - before;
        }

        private static string Summ(long[] v)
        {
            long max = 0, sum = 0; int nonzero = 0;
            foreach (long x in v) { sum += x; if (x > max) max = x; if (x > 0) nonzero++; }
            var hist = new SortedDictionary<long, int>();
            foreach (long x in v) { hist.TryGetValue(x, out int c); hist[x] = c + 1; }
            var sb = new StringBuilder();
            foreach (var kv in hist) sb.Append(kv.Key).Append(" B x").Append(kv.Value).Append("; ");
            return $"{v.Length} frames, {nonzero} with any allocation, total {sum} B, max {max} B/frame — values: {sb}";
        }

        // =====================================================================================================
        // Phase C — aim and zoom while recording (AC3)
        // =====================================================================================================

        private IEnumerator PhaseC()
        {
            Line("\n== Phase C — aim and zoom while recording ==");
            ResetVantage();
            yield return Raise(true);
            yield return Wait(0.3f);
            int logFrom = _rCount;
            yield return PressRecord();
            yield return Wait(0.5f);
            yield return Grab("c1_before_look_zoom", "C: recording, before look and zoom");
            float yaw0 = cam.transform.eulerAngles.y, fov0 = cam.fieldOfView;
            Pad(rstick: new Vector2(1.0f, 0.35f));
            yield return Wait(2.0f);
            Pad(rstick: Vector2.zero);
            for (int i = 0; i < 4; i++) { Pad(dpadUp: true); yield return null; yield return null; Pad(dpadUp: false); yield return Wait(0.15f); }
            yield return Wait(1.0f);
            yield return Grab("c2_after_look_zoom", "C: recording, after look (right stick) and zoom (dpad up x4)");
            float yaw1 = cam.transform.eulerAngles.y, fov1 = cam.fieldOfView;
            yield return PressRecord();
            yield return null;
            var f = (ReplayFootage)_lastRaised.Footage;
            Line($"Live camera: yaw {F(yaw0, "0.0")} -> {F(yaw1, "0.0")}°, FOV {F(fov0, "0.0")} -> {F(fov1, "0.0")}");
            TickSample a = f.TickAt(0), b = f.TickAt(f.TickCount - 1);
            Line($"Track:       yaw {F(a.Camera.Rotation.eulerAngles.y, "0.0")} -> {F(b.Camera.Rotation.eulerAngles.y, "0.0")}°, FOV {F(a.Camera.FieldOfView, "0.0")} -> {F(b.Camera.FieldOfView, "0.0")}");
            ComparePoses("C", f, logFrom);
            photo.SetZoom(0f);
            yield return Raise(false);
            ResetVantage();
        }

        /// <summary>Each tick's camera (and actor) against the pose the camera actually RENDERED with on the tick's own
        /// frame, read in beginCameraRendering. 0 means the recorder samples after the camera's final write. The
        /// shifted control compares each tick with the PREVIOUS frame's render, which must be non-zero while moving.</summary>
        private void ComparePoses(string tag, ReplayFootage f, int logFrom)
        {
            double maxPos = 0, maxAng = 0, maxFov = 0, maxActor = 0, ctlPos = 0, ctlAng = 0, ctlFov = 0, ctlActor = 0;
            int matched = 0, actorMatched = 0, prevI = -1;
            for (int k = 0; k < f.TickCount; k++)
            {
                TickSample t = f.TickAt(k);
                int i = FindRender(t.Frame, logFrom);
                if (i < 0) continue;
                matched++;
                maxPos = Math.Max(maxPos, (t.Camera.Position - _rCamPos[i]).magnitude);
                maxAng = Math.Max(maxAng, Quaternion.Angle(t.Camera.Rotation, _rCamRot[i]));
                maxFov = Math.Max(maxFov, Math.Abs(t.Camera.FieldOfView - _rFov[i]));
                if (prevI >= 0)
                {
                    ctlPos = Math.Max(ctlPos, (t.Camera.Position - _rCamPos[prevI]).magnitude);
                    ctlAng = Math.Max(ctlAng, Quaternion.Angle(t.Camera.Rotation, _rCamRot[prevI]));
                    ctlFov = Math.Max(ctlFov, Math.Abs(t.Camera.FieldOfView - _rFov[prevI]));
                }
                if (t.ActorCount > 0 && _rActorLive[i])
                {
                    ActorSample s = f.ActorAt(t.ActorStart);
                    if (s.Live)
                    {
                        actorMatched++;
                        maxActor = Math.Max(maxActor, (s.Position - _rActorPos[i]).magnitude);
                        if (prevI >= 0 && _rActorLive[prevI]) ctlActor = Math.Max(ctlActor, (s.Position - _rActorPos[prevI]).magnitude);
                    }
                }
                prevI = i;
            }
            Line($"{tag} poses vs render-time log: {matched}/{f.TickCount} ticks matched to their frame; camera max |dpos| {F(maxPos * 100, "0.000")} cm, " +
                 $"max angle {F(maxAng, "0.0000")}°, max |dFOV| {F(maxFov, "0.0000")}; actor max |dpos| {F(maxActor * 100, "0.000")} cm over {actorMatched} ticks. " +
                 $"Shifted control (each tick vs the render of the PREVIOUS tick's frame): camera {F(ctlPos * 100, "0.00")} cm, {F(ctlAng, "0.000")}°, FOV {F(ctlFov, "0.000")}; actor {F(ctlActor * 100, "0.00")} cm");
            ClockEnvelope(tag, logFrom);
        }

        private int FindRender(int frame, int from)
        {
            for (int i = from; i < _rCount; i++)
            {
                if (_rFrame[i] == frame) return i;
                if (_rFrame[i] > frame) return -1;
            }
            return -1;
        }

        // =====================================================================================================
        // Phase D — fidelity (AC5): poses, picture, sound, A/V offset, beep absence, the respawn clip
        // =====================================================================================================

        private IEnumerator PhaseD()
        {
            Line("\n== Phase D — fidelity ==");
            ResetVantage();
            yield return Raise(true);

            // D1: wait for a fresh lifecycle, then record ~30 s through his peak, with three A/V markers.
            int serial0 = CurrentSerial();
            float waited = 0f;
            while (CurrentSerial() == serial0 || CurrentSerial() == 0) { yield return null; waited += Time.unscaledDeltaTime; if (waited > 60f) break; }
            Line($"D1: new lifecycle {CurrentSerial()} after {F(waited, "0.0")} s");
            yield return Clip("d1", 30f, new[] { 4.0, 12.0, 21.0 }, heavyStart: false, liveShots: 6);

            // D2: start mid-lifecycle and run through the despawn, the 2 s delay and the next spawn.
            yield return Clip("d2", 45f, new double[0], heavyStart: false, liveShots: 6, untilSecondLifecycle: true);

            // D3: a deliberately heavy press frame (200 ms busy-wait after Begin) must not move the sound.
            _beepControlAt = 3.5;
            yield return Clip("d3", 9f, new[] { 2.0, 5.0 }, heavyStart: true, liveShots: 0);
            _beepControlAt = -1;
            yield return Raise(false);
        }

        private AudioClip StartBeepClip() =>
            UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Audio/SFX/RecStartBeepPlaceholder.wav");

        private int CurrentSerial()
        {
            IReadOnlyList<EventActor> a = manager.ActiveActors;
            return a.Count > 0 && a[0] != null ? a[0].LifecycleSerial : 0;
        }

        private IEnumerator Clip(string tag, float seconds, double[] markersAt, bool heavyStart, int liveShots, bool untilSecondLifecycle = false)
        {
            photo.SetZoom(0f);
            yield return Wait(0.4f);
            int logFrom = _rCount;
            _markerDsp.Clear(); _markerFrame.Clear(); _markerNext = 0;
            var live = new List<(int frame, string file)>();

            _boneLog.Begin(manager);
            _beepProbe.Arm();
            if (heavyStart)
            {
                // The press and the busy-wait happen in the same frame, after Begin: the spike's PCM started at the next
                // audio callback and was 160-290 ms late on such frames.
                photo.ToggleRecording();
                var sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < 200) { }
            }
            else
            {
                yield return PressRecord();
            }
            double startDsp = recorder.StartDspTime;
            foreach (double m in markersAt)
            {
                double at = startDsp + m;
                _markerDsp.Add(at);
                    var src = gameObject.AddComponent<AudioSource>();   // one source per marker, so none cuts another
                src.playOnAwake = false; src.spatialBlend = 0f; src.clip = _click;
                src.PlayScheduled(at);
            }

            if (_beepControlAt > 0)
            {
                // Positive control for the beep's absence: the SAME start-beep clip, through a source that does NOT
                // bypass listener effects, at a known time. The clip must show it there.
                var ctl = gameObject.AddComponent<AudioSource>();
                ctl.playOnAwake = false; ctl.spatialBlend = 0f; ctl.bypassListenerEffects = false;
                ctl.clip = hud.GetComponent<AudioSource>() != null ? StartBeepClip() : null;
                ctl.volume = RecordingConfig.BeepVolumeOf(shippedConfig);
                if (ctl.clip != null) ctl.PlayScheduled(startDsp + _beepControlAt);
            }
            float shotEvery = liveShots > 0 ? seconds / (liveShots + 1) : float.MaxValue;
            float nextShot = shotEvery;
            float t = 0f;
            int firstSerial = CurrentSerial();
            bool sawGap = false;
            float respawnAt = -1f;
            while (true)
            {
                yield return null;
                t += Time.unscaledDeltaTime;
                if (CurrentSerial() == 0) sawGap = true;
                if (sawGap && respawnAt < 0f && CurrentSerial() != 0 && CurrentSerial() != firstSerial) respawnAt = t;
                if (!untilSecondLifecycle && t >= seconds) break;
                if (untilSecondLifecycle && ((respawnAt >= 0f && t >= respawnAt + 8f) || t >= 95f)) break;
                if (t >= nextShot && live.Count < liveShots)
                {
                    nextShot += shotEvery;
                    yield return new WaitForEndOfFrame();
                    string file = $"{tag}_live_f{Time.frameCount:000000}.png";
                    RenderCameraTo(Path.Combine(outputDir, file));
                    live.Add((Time.frameCount, file));
                }
            }
            yield return PressRecord();
            yield return null;
            _boneLog.End();
            _beepProbe.Disarm();
            foreach (AudioSource s in GetComponents<AudioSource>()) if (s != _clickSource) Destroy(s);

            var f = (ReplayFootage)_lastRaised.Footage;
            Line($"\n{tag}: {_lastRaised}; ticks {f.TickCount}, boundaries {f.Boundaries}, PCM {f.PcmFrames} frames " +
                 $"(the tick timeline needs {FootageFormat.PcmFramesFor(f.Boundaries, f.SampleRate)}), {f.SampleRate} Hz x{f.Channels}, sound {f.HasSound}");
            ComparePoses(tag, f, logFrom);
            Lifecycles(tag, f);
            WriteWav(f, Path.Combine(outputDir, tag + "_clip.wav"));
            Loudness(tag, f);
            BeepAbsence(tag, f);
            Offsets(tag, f, markersAt);
            WriteTrack(f, Path.Combine(outputDir, tag + "_track.csv"));
            BoneFidelity(tag, f);
            if (live.Count > 0) yield return Ghosts(tag, f, live);
        }

        /// <summary>The rig's replay ghost: the definition's prefab with every gameplay component stripped, its
        /// Animator frozen (speed 0, as the 1.14 spike's ReplayPlayer) so only the track moves it.</summary>
        private GameObject BuildGhost(out Animator anim)
        {
            var holder = new GameObject("RigGhostHolder");
            holder.SetActive(false);
            GameObject ghost = Instantiate(actorPrefab.gameObject, holder.transform);
            foreach (MonoBehaviour mb in ghost.GetComponentsInChildren<MonoBehaviour>(true)) DestroyImmediate(mb);
            foreach (var c in ghost.GetComponentsInChildren<UnityEngine.AI.NavMeshAgent>(true)) DestroyImmediate(c);
            foreach (var c in ghost.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(c);
            foreach (var c in ghost.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            ghost.transform.SetParent(null, false);
            Destroy(holder);
            anim = ghost.GetComponent<Animator>();
            anim.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            anim.applyRootMotion = false;
            anim.speed = 0f;
            ghost.SetActive(true);
            return ghost;
        }

        /// <summary>Poses the ghost from one tick (ADR-4: state and time per layer, the transition by its recorded
        /// unit). False, ghost hidden, when the tick has no live actor.</summary>
        private static bool PoseGhost(GameObject ghost, Animator anim, ReplayFootage f, TickSample t)
        {
            bool liveActor = t.ActorCount > 0 && f.ActorAt(t.ActorStart).Live;
            if (ghost.activeSelf != liveActor) ghost.SetActive(liveActor);
            if (!liveActor) return false;
            ActorSample a = f.ActorAt(t.ActorStart);
            ghost.transform.SetPositionAndRotation(a.Position, a.Rotation);
            ghost.transform.localScale = a.Scale;
            for (int l = 0; l < a.LayerCount; l++)
            {
                LayerSample s = f.LayerAt(a.LayerStart + l);
                anim.Play(s.StateHash, l, s.StateTime);
                if (s.InTransition)
                {
                    // The ghost's clock is FROZEN (speed 0), so a crossfade's pose depends only on the two states'
                    // normalized times and the transition's progress, never on its duration or unit. The normalized
                    // CrossFade takes the next state's NORMALIZED time, which is what the track holds; the fixed-time
                    // overload wants that offset in seconds (the next state's length is not recorded), and passing 0
                    // put the next state at its start: 6.4 cm mean bone error on transition ticks (run 2). A playback
                    // whose clock RUNS between ticks (Story 1.18) must honour TransitionUnit instead (ADR-4).
                    anim.Update(0f);
                    anim.CrossFade(s.NextStateHash, s.TransitionDuration, l, s.NextStateTime, s.TransitionTime);
                }
            }
            anim.Update(0f);
            return true;
        }

        /// <summary>Every tick with a live actor: the ghost posed from the track against the LIVE bones logged on that
        /// tick's frame (after the recorder sampled), in cm. Control: the same ghost against the previous tick's
        /// live bones, which must be clearly non-zero while he moves.</summary>
        private void BoneFidelity(string tag, ReplayFootage f)
        {
            GameObject ghost = BuildGhost(out Animator anim);
            var smr = ghost.GetComponentInChildren<SkinnedMeshRenderer>(true);
            Transform[] gb = smr != null ? smr.bones : new Transform[0];
            double sumSteady = 0, maxSteady = 0, sumTrans = 0, maxTrans = 0, sumCtl = 0, maxCtl = 0;
            int nSteady = 0, nTrans = 0, nCtl = 0, worst = -1;
            Vector3[] prev = null;
            for (int k = 0; k < f.TickCount; k++)
            {
                TickSample t = f.TickAt(k);
                Vector3[] liveBones = _boneLog.At(t.Frame);
                if (!PoseGhost(ghost, anim, f, t) || liveBones == null || liveBones.Length != gb.Length) { prev = liveBones; continue; }
                double max = 0, ctl = 0;
                for (int b = 0; b < gb.Length; b++)
                {
                    max = Math.Max(max, (gb[b].position - liveBones[b]).magnitude);
                    if (prev != null && prev.Length == gb.Length) ctl = Math.Max(ctl, (gb[b].position - prev[b]).magnitude);
                }
                bool trans = false;
                ActorSample a = f.ActorAt(t.ActorStart);
                for (int l = 0; l < a.LayerCount; l++) trans |= f.LayerAt(a.LayerStart + l).InTransition;
                if (trans) { nTrans++; sumTrans += max; if (max > maxTrans) maxTrans = max; }
                else { nSteady++; sumSteady += max; if (max > maxSteady) { maxSteady = max; worst = k; } }
                if (prev != null) { nCtl++; sumCtl += ctl; maxCtl = Math.Max(maxCtl, ctl); }
                prev = liveBones;
            }
            Destroy(ghost);
            Line($"{tag} bone fidelity (ghost posed from the track vs live bones on the tick's frame, {gb.Length} bones): " +
                 $"steady {nSteady} ticks mean {F(nSteady > 0 ? sumSteady / nSteady * 100 : -1, "0.000")} cm, worst {F(maxSteady * 100, "0.000")} cm (tick {worst}); " +
                 $"in transition {nTrans} ticks mean {F(nTrans > 0 ? sumTrans / nTrans * 100 : -1, "0.000")} cm, worst {F(maxTrans * 100, "0.000")} cm; " +
                 $"control vs the previous tick's live bones: mean {F(nCtl > 0 ? sumCtl / nCtl * 100 : -1, "0.00")} cm, max {F(maxCtl * 100, "0.00")} cm");
        }

        /// <summary>The two clocks the clip is stamped by, compared on every rendered frame of the clip: the audio
        /// clock (dspTime, the PCM's truth) and the realtime clock (the ticks'), both since the press. dspTime only
        /// moves in whole DSP blocks on the main thread, so (dsp - rt) is a sawtooth whose TOP is where the two
        /// clocks really agree; its drift over the clip is the clocks' rate mismatch.</summary>
        private void ClockEnvelope(string tag, int logFrom)
        {
            double t0 = recorder.StartRealtime, d0 = recorder.StartDspTime;
            double hiFirst = double.NaN, hiLast = double.NaN, hi = double.MinValue, lo = double.MaxValue;
            int n = 0;
            for (int i = logFrom; i < _rCount; i++)
            {
                double rt = _rRealtime[i] - t0;
                if (rt < 0) continue;
                double d = (_rDsp[i] - d0) - rt;
                hi = Math.Max(hi, d); lo = Math.Min(lo, d); n++;
                if (rt < 3 && (double.IsNaN(hiFirst) || d > hiFirst)) hiFirst = d;
                if (double.IsNaN(hiLast) || rt > (_rRealtime[_rCount - 1] - t0) - 3) hiLast = double.IsNaN(hiLast) ? d : Math.Max(hiLast, d);
            }
            if (n == 0) return;
            Line($"{tag} clocks over {n} frames: (dspTime - realtime) since the press ranges {F(lo * 1000, "0.0")} .. {F(hi * 1000, "0.0")} ms " +
                 $"(DSP block {F(1000.0 * AudioSettings.GetConfiguration().dspBufferSize / AudioSettings.outputSampleRate, "0.0")} ms); " +
                 $"top of the sawtooth in the first 3 s {F(hiFirst * 1000, "0.0")} ms, last 3 s {F(hiLast * 1000, "0.0")} ms (drift = their difference)");
        }

        private void RenderCameraTo(string path)
        {
            var rt = RenderTexture.GetTemporary(960, 540, 24);
            RenderTexture prev = cam.targetTexture;
            cam.targetTexture = rt;
            cam.Render();
            cam.targetTexture = prev;
            RenderTexture.active = rt;
            var tex = new Texture2D(960, 540, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 960, 540), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Destroy(tex);
        }

        private void Lifecycles(string tag, ReplayFootage f)
        {
            var sb = new StringBuilder();
            int lastSerial = -1, gapTicks = 0;
            for (int k = 0; k < f.TickCount; k++)
            {
                TickSample t = f.TickAt(k);
                int serial = 0; bool liveAny = false; Vector3 pos = default;
                for (int j = 0; j < t.ActorCount; j++)
                {
                    ActorSample a = f.ActorAt(t.ActorStart + j);
                    if (a.Live) { liveAny = true; serial = a.Lifecycle; pos = a.Position; }
                }
                if (!liveAny) { gapTicks++; continue; }
                if (serial != lastSerial)
                {
                    ActorSample a0 = f.ActorAt(t.ActorStart);
                    sb.Append($"[tick {k} at {F(t.Time, "0.00")} s: lifecycle {serial} '{(a0.Definition != null ? a0.Definition.Id : "null")}' " +
                              $"first pose ({F(pos.x, "0.00")}, {F(pos.z, "0.00")}), {gapTicks} actor-less ticks before] ");
                    lastSerial = serial;
                    gapTicks = 0;
                }
            }
            Line($"{tag} lifecycles: {sb}");
        }

        private static void WriteWav(ReplayFootage f, string path)
        {
            if (!f.HasSound) return;
            using (var w = new BinaryWriter(File.Create(path)))
            {
                int ch = f.Channels; long frames = f.PcmFrames;
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write((int)(36 + frames * ch * 2)); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)ch); w.Write(f.SampleRate); w.Write(f.SampleRate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write((int)(frames * ch * 2));
                var buf = new short[4096 * ch];
                for (long s = 0; s < frames;) { int n = f.ReadPcm(s, buf); for (int i = 0; i < n * ch; i++) w.Write(buf[i]); s += n; if (n == 0) break; }
            }
        }

        /// <summary>RMS of the PCM in each tick's window against the camera-to-actor distance on that tick (both read
        /// from the footage itself). Windows near a marker click are left out.</summary>
        private void Loudness(string tag, ReplayFootage f)
        {
            if (!f.HasSound) { Line($"{tag} loudness: no sound"); return; }
            int ch = f.Channels; long slot = 0;
            var xs = new List<double>(); var ys = new List<double>();
            double totalSq = 0; long totalN = 0;
            var buf = new float[1600 * 8 * ch];
            for (int k = 0; k < f.TickCount; k++)
            {
                TickSample t = f.TickAt(k);
                long start = FootageFormat.PcmFramesFor(slot, f.SampleRate);
                long len = FootageFormat.PcmFramesFor(slot + t.Repeats, f.SampleRate) - start;
                slot += t.Repeats;
                if (len <= 0 || len * ch > buf.Length) continue;
                var w = new float[len * ch];
                int n = f.ReadPcm(start, w);
                double sq = 0; float peak = 0;
                for (int i = 0; i < n * ch; i++) { sq += w[i] * w[i]; peak = Mathf.Max(peak, Mathf.Abs(w[i])); }
                totalSq += sq; totalN += n * ch;
                if (peak > 0.5f) continue;   // a marker click
                if (t.ActorCount == 0 || !f.ActorAt(t.ActorStart).Live) continue;
                double rms = Math.Sqrt(sq / Math.Max(1, n * ch));
                xs.Add((f.ActorAt(t.ActorStart).Position - t.Camera.Position).magnitude);
                ys.Add(rms > 0 ? 20 * Math.Log10(rms) : -120);
            }
            double r = Pearson(xs, ys);
            Line($"{tag} loudness: clip RMS {F(20 * Math.Log10(Math.Sqrt(totalSq / Math.Max(1, totalN)) + 1e-12), "0.0")} dBFS; " +
                 $"per-tick dBFS vs camera-to-drunk distance over {xs.Count} ticks: r = {F(r, "0.00")} (1.14 measured -0.58 to -0.73); " +
                 $"distance {F(Min(xs), "0.0")}-{F(Max(xs), "0.0")} m");
        }

        private static double Min(List<double> v) { double m = double.MaxValue; foreach (var x in v) m = Math.Min(m, x); return v.Count == 0 ? 0 : m; }
        private static double Max(List<double> v) { double m = double.MinValue; foreach (var x in v) m = Math.Max(m, x); return v.Count == 0 ? 0 : m; }

        private static double Pearson(List<double> x, List<double> y)
        {
            int n = x.Count; if (n < 3) return double.NaN;
            double mx = 0, my = 0; for (int i = 0; i < n; i++) { mx += x[i]; my += y[i]; }
            mx /= n; my /= n;
            double sxy = 0, sxx = 0, syy = 0;
            for (int i = 0; i < n; i++) { sxy += (x[i] - mx) * (y[i] - my); sxx += (x[i] - mx) * (x[i] - mx); syy += (y[i] - my) * (y[i] - my); }
            return sxy / Math.Sqrt(sxx * syy);
        }

        /// <summary>Energy at the beep's 2.7 kHz in the clip's first 150 ms (when the start beep played) against a
        /// window one second later, and the beep's own level at its source.</summary>
        private void BeepAbsence(string tag, ReplayFootage f)
        {
            if (!f.HasSound) return;
            double g0 = Goertzel(f, 0, 0.15, 2700), g1 = Goertzel(f, 1.0, 0.15, 2700);
            if (_beepControlAt > 0)
                Line($"{tag} POSITIVE CONTROL: the same beep through a non-bypassed source at +{F(_beepControlAt, "0.0")} s reads " +
                     $"{F(10 * Math.Log10(Goertzel(f, _beepControlAt, 0.15, 2700) + 1e-15), "0.0")} dB at 2.7 kHz in the clip");
            Line($"{tag} start beep in the clip's sound: 2.7 kHz power in 0-150 ms {F(10 * Math.Log10(g0 + 1e-15), "0.0")} dB vs 1.0-1.15 s {F(10 * Math.Log10(g1 + 1e-15), "0.0")} dB; " +
                 $"the beep's own source peaked at {F(_beepProbe.Peak, "0.000")} (heard at its source: {_beepProbe.FirstTs != 0})");
        }

        private static double Goertzel(ReplayFootage f, double from, double len, double freq)
        {
            int ch = f.Channels; long s0 = (long)(from * f.SampleRate); int n = (int)(len * f.SampleRate);
            var w = new float[n * ch]; int got = f.ReadPcm(s0, w);
            double k = 2 * Math.Cos(2 * Math.PI * freq / f.SampleRate), s1 = 0, s2 = 0;
            for (int i = 0; i < got; i++) { double x = w[i * ch]; double s = x + k * s1 - s2; s2 = s1; s1 = s; }
            return got == 0 ? 0 : (s1 * s1 + s2 * s2 - k * s1 * s2) / got;
        }

        /// <summary>A/V offset per marker: where the click's onset is in the PCM, against the slot time of the first
        /// tick whose FOV moved after the marker frame. Positive = the picture changes after the sound.</summary>
        private void Offsets(string tag, ReplayFootage f, double[] markersAt)
        {
            if (markersAt.Length == 0) return;
            var sb = new StringBuilder();
            for (int m = 0; m < markersAt.Length; m++)
            {
                double clickAt = FindClick(f, markersAt[m] - 0.25, 0.6);
                long slot = 0; double pictureAt = double.NaN, tickTime = double.NaN; float prevFov = float.NaN; int tickIdx = -1;
                int markerFrame = m < _markerFrame.Count ? _markerFrame[m] : int.MaxValue;
                for (int k = 0; k < f.TickCount; k++)
                {
                    TickSample t = f.TickAt(k);
                    if (t.Frame >= markerFrame && !float.IsNaN(prevFov) && Math.Abs(t.Camera.FieldOfView - prevFov) > 0.01f) { pictureAt = slot / 30.0; tickIdx = k; tickTime = t.Time; break; }
                    prevFov = t.Camera.FieldOfView;
                    slot += t.Repeats;
                }
                sb.Append($"[marker {m} scheduled +{F(markersAt[m], "0.000")} s: click onset in PCM {F(clickAt)} s; the FOV first moves on tick {tickIdx}, " +
                          $"sampled at clip time {F(tickTime)} s, played from slot {F(pictureAt)} s; slot - click {F((pictureAt - clickAt) * 1000, "0.0")} ms, " +
                          $"tick time - click {F((tickTime - clickAt) * 1000, "0.0")} ms] ");
            }
            Line($"{tag} A/V: {sb}");
        }

        private static double FindClick(ReplayFootage f, double from, double len)
        {
            int ch = f.Channels; long s0 = (long)(Math.Max(0, from) * f.SampleRate); int n = (int)(len * f.SampleRate);
            var w = new float[n * ch]; int got = f.ReadPcm(s0, w);
            for (int i = 0; i < got; i++) if (Mathf.Abs(w[i * ch]) > 0.5f) return (s0 + i) / (double)f.SampleRate;
            return double.NaN;
        }

        private static void WriteTrack(ReplayFootage f, string path)
        {
            var sb = new StringBuilder("tick,time,repeats,frame,cam_x,cam_y,cam_z,cam_yaw,cam_pitch,fov,actors,lifecycle,live,actor_x,actor_z,state,state_t,in_transition,unit\n");
            for (int k = 0; k < f.TickCount; k++)
            {
                TickSample t = f.TickAt(k);
                Vector3 e = t.Camera.Rotation.eulerAngles;
                ActorSample a = t.ActorCount > 0 ? f.ActorAt(t.ActorStart) : default;
                LayerSample l = a.LayerCount > 0 ? f.LayerAt(a.LayerStart) : default;
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture, "{0},{1:0.0000},{2},{3},{4:0.000},{5:0.000},{6:0.000},{7:0.00},{8:0.00},{9:0.000},{10},{11},{12},{13:0.000},{14:0.000},{15},{16:0.0000},{17},{18}",
                    k, t.Time, t.Repeats, t.Frame, t.Camera.Position.x, t.Camera.Position.y, t.Camera.Position.z, e.y, e.x, t.Camera.FieldOfView,
                    t.ActorCount, a.Lifecycle, a.Live, a.Position.x, a.Position.z, l.StateHash, l.StateTime, l.InTransition, l.TransitionUnit));
            }
            File.WriteAllText(path, sb.ToString());
        }

        /// <summary>Re-renders chosen ticks through a rig-side GHOST (the definition's prefab, gameplay components
        /// stripped, posed from the track) and saves each next to the live frame grabbed on that tick's frame. A rig
        /// instrument, not Story 1.18's playback.</summary>
        private IEnumerator Ghosts(string tag, ReplayFootage f, List<(int frame, string file)> live)
        {
            GameObject ghost = BuildGhost(out Animator anim);
            var hidden = new List<Renderer>();
            foreach (EventActor a in manager.GetComponentsInChildren<EventActor>(true))
                foreach (Renderer r in a.GetComponentsInChildren<Renderer>(true)) if (!r.forceRenderingOff) { r.forceRenderingOff = true; hidden.Add(r); }

            cam.transform.GetPositionAndRotation(out Vector3 keepPos, out Quaternion keepRot);
            float keepFov = cam.fieldOfView;
            var pairs = new StringBuilder();
            foreach (var shot in live)
            {
                int k = -1;
                for (int i = 0; i < f.TickCount; i++) if (f.TickAt(i).Frame == shot.frame) { k = i; break; }
                if (k < 0) { pairs.Append($"[{shot.file}: no tick on frame {shot.frame}] "); continue; }
                TickSample t = f.TickAt(k);
                bool liveActor = PoseGhost(ghost, anim, f, t);
                cam.transform.SetPositionAndRotation(t.Camera.Position, t.Camera.Rotation);
                cam.fieldOfView = t.Camera.FieldOfView;
                string gfile = shot.file.Replace("_live_", "_ghost_");
                RenderCameraTo(Path.Combine(outputDir, gfile));
                pairs.Append($"[tick {k} frame {shot.frame}: {shot.file} / {gfile}, actor {(liveActor ? "posed" : "absent")}] ");
                yield return null;
                cam.transform.SetPositionAndRotation(keepPos, keepRot);
                cam.fieldOfView = keepFov;
            }
            cam.transform.SetPositionAndRotation(keepPos, keepRot);
            cam.fieldOfView = keepFov;
            foreach (Renderer r in hidden) if (r != null) r.forceRenderingOff = false;
            Destroy(ghost);
            Line($"{tag} ghost re-renders: {pairs}");
        }

        // =====================================================================================================
        // Phase F — fail-soft (AC6). PROVOKED ERRORS: see the header of this report.
        // =====================================================================================================

        private IEnumerator PhaseF()
        {
            Line("\n== Phase F — fail-soft (provoked) ==");
            Debug.Log("[RecordClipShoot] PHASE F BEGIN — the errors and warnings until PHASE F END are provoked on purpose.");

            yield return Throwaway("F1 no channel, no tap, no EventManager", null, null, null, null);
            foreach (float bad in new[] { 0f, float.NaN, 1e9f })
            {
                var cfg = ScriptableObject.CreateInstance<RecordingConfig>();
                cfg.maxClipSeconds = bad;
                yield return Throwaway($"F4 maxClipSeconds {bad}", channel, cfg, manager, cam.GetComponent<ListenerTap>());
                Destroy(cfg);
            }

            // F5: an audio configuration reset mid-clip, on the real recorder.
            _logLines.Clear(); _collectLogs = true;
            AudioConfiguration before = AudioSettings.GetConfiguration();
            yield return Raise(true);
            yield return PressRecord();
            yield return Wait(1.0f);
            bool reset = AudioSettings.Reset(before);
            for (int i = 0; i < 10; i++) yield return null;
            AudioConfiguration after = AudioSettings.GetConfiguration();
            _collectLogs = false;
            Line($"F5 audio reset (returned {reset}): recording {recorder.IsRecording}, last stop {_lastReason}, clip {_lastRaised}; " +
                 $"config after {after.sampleRate} Hz / {after.dspBufferSize} / {after.speakerMode} (before {before.sampleRate} / {before.dspBufferSize} / {before.speakerMode}); " +
                 $"log lines: {Join(_logLines)}");
            yield return Wait(0.5f);
            _markerDsp.Clear(); _markerFrame.Clear(); _markerNext = 0;
            yield return PressRecord();
            SetupClick();   // the reset invalidates procedural clips: the one made at setup is silent now (found 2026-11-15)
            var src = gameObject.AddComponent<AudioSource>(); src.playOnAwake = false; src.spatialBlend = 0f; src.clip = _click; src.Play();
            yield return Wait(1.0f);
            yield return PressRecord();
            yield return null;
            var fAfter = (ReplayFootage)_lastRaised.Footage;
            Line($"   a clip after the reset: sound {fAfter.HasSound}, click found at {F(FindClick(fAfter, 0, 0.9))} s");
            Destroy(src);
            yield return Raise(false);

            // F6: a recorder disabled mid-clip.
            _logLines.Clear(); _collectLogs = true;
            ClipRecorder rec = MakeRecorder(channel, shippedConfig, manager, null);
            yield return null;
            rec.Begin();
            yield return Wait(0.8f);
            rec.enabled = false;
            yield return null;
            _collectLogs = false;
            Line($"F6 disabled mid-clip: recording {rec.IsRecording}, LastClip {rec.LastClip} ({(rec.LastClip != null && !rec.LastClip.Footage.IsDisposed ? "kept" : "LOST")}); log lines: {Join(_logLines)}");
            Destroy(rec.gameObject);
            yield return null;

            Debug.Log("[RecordClipShoot] PHASE F END — anything after this is not provoked.");
        }

        private ClipRecorder MakeRecorder(ClipRecordedChannel ch, RecordingConfig cfg, EventManager em, ListenerTap tap)
        {
            var go = new GameObject("RigThrowawayRecorder");
            go.SetActive(false);
            var r = go.AddComponent<ClipRecorder>();
            Set(r, "clipRecordedChannel", ch);
            Set(r, "config", cfg);
            Set(r, "recordCamera", cam);
            Set(r, "eventManager", em);
            Set(r, "listenerTap", tap);
            go.SetActive(true);
            return r;
        }

        private static void Set(object o, string field, object v) =>
            o.GetType().GetField(field, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic).SetValue(o, v);

        private IEnumerator Throwaway(string label, ClipRecordedChannel ch, RecordingConfig cfg, EventManager em, ListenerTap tap)
        {
            _logLines.Clear(); _collectLogs = true;
            ClipRecorder r = MakeRecorder(ch, cfg, em, tap);
            yield return null;
            bool began = r.Begin();
            yield return Wait(0.6f);
            r.Stop(StopReason.PlayerPressed);
            var f = r.LastClip?.Footage as ReplayFootage;
            _collectLogs = false;
            Line($"{label}: began {began}, LastClip {r.LastClip}, sound {f?.HasSound}, actor samples {f?.ActorSampleCount}, " +
                 $"capacity {F(r.CapacityBytesPerClip / 1048576.0, "0.0")} MB/clip; log lines ({_logLines.Count}): {Join(_logLines)}");
            Destroy(r.gameObject);
            yield return null;
        }

        private static string Join(List<string> lines)
        {
            var sb = new StringBuilder();
            foreach (string l in lines) sb.Append("«").Append(l.Length > 170 ? l.Substring(0, 170) + "…" : l).Append("» ");
            return sb.ToString();
        }
    }

}
#endif
