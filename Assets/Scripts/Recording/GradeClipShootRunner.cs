#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using CameraGame.Core;
using CameraGame.Events;
using CameraGame.Gallery;
using CameraGame.Grading;
using CameraGame.PhotoMode;
using CameraGame.UI;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace CameraGame.Recording
{
    /// <summary>
    /// Story 1.16's verification rig (Task 8), driven by <c>Tools/Grading/Grade Clip Shoot (Play)</c>. Runs in the
    /// REAL SampleScene with its real wiring: every clip is recorded by the shipped <see cref="ClipRecorder"/>,
    /// graded by its <see cref="ClipGrader"/> and shown by the shipped <see cref="GradeHud"/>. The rig only aims the
    /// player (yaw on the body, pitch through ThirdPersonController's private field, zoom through the controller's
    /// SetZoom), presses Record through the controller's own ToggleRecording (Phase H: through a virtual gamepad), and
    /// writes what happened. Every scenario is ANCHORED TO THE LIVE LIFECYCLE (the actor's own clock), never to a
    /// wall-clock schedule (loop lesson 42). Nothing is asserted; the report and the sweep are judged afterwards.
    ///
    /// Output: report.txt, ticks.csv (every graded tick, floats round-trip), clips.csv (every clip's live grade and the
    /// words the HUD printed), and PNG frames. Runtime assembly behind UNITY_EDITOR (CLAUDE.md trap).
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public class GradeClipShootRunner : MonoBehaviour
    {
        // Wired by the editor rig from the shipped scene.
        public PhotoModeController photo;
        public ClipRecorder recorder;
        public GradeHud hud;
        public EventManager manager;
        public Camera cam;
        public ThirdPersonController player;
        public ClipRecordedChannel channel;
        public ShotCapturedChannel shotChannel;
        public RecordingConfig recordingConfig;
        public GradingConfig gradingConfig;
        public GradeHudConfig hudConfig;
        public CameraConfig cameraConfig;
        public GalleryView gallery;
        public ListenerTap tap;
        public string outputDir;
        public float eyeHeight = 1.65f;
        public string phases = "0SHEF";

        private enum Aim { Free, Subject, Away }

        private readonly StringBuilder _log = new StringBuilder();
        private readonly StringBuilder _ticks = new StringBuilder();
        private readonly StringBuilder _clips = new StringBuilder();
        private readonly List<string> _logLines = new List<string>();
        private bool _collectLogs;

        private Aim _aim = Aim.Free;
        private float _yawOffset, _pitchOffset;
        private float _heightTarget = -1f;      // > 0: hold the subject at this share of the frame height
        private Vector3 _vantage;
        private float _vantageYaw;
        private FieldInfo _pitchField;

        private string _tag = "-";
        private int _clipNo;
        private int _raises;
        private RecordedClip _lastRaised;
        private bool _claimNext;
        private ShotGrade _lastShot;
        private int _shots;
        private ClipGrader.TickRecord _lastTick;
        private bool _haveTick;
        private int _controlArmed;
        private readonly List<string> _controlRows = new List<string>();

        private Gamepad _pad;
        private GamepadState _padState;
        private InputSettings.BackgroundBehavior _prevBg;
        private InputSettings.EditorInputBehaviorInPlayMode _prevEditorInput;
        private int _captureActions;
        private float _prevMaxClip;
        private bool _configTouched;

        private GameObject _labelCanvas;
        private Text _label;
        private Texture2D _last;
        private Text _rating, _axes, _why;
        private CanvasGroup _hudGroup;

        private static string F(double v, string fmt = "0.000") => v.ToString(fmt, CultureInfo.InvariantCulture);
        private static string R(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        private static long Ts() => Stopwatch.GetTimestamp();
        private static double Ms(long a, long b) => (b - a) * 1000.0 / Stopwatch.Frequency;

        private void Line(string s)
        {
            _log.AppendLine(s);
            Debug.Log("[GradeClipShoot] " + s);
        }

        private void Start()
        {
            Directory.CreateDirectory(outputDir);
            StartCoroutine(Run());
        }

        // =====================================================================================================
        // Run
        // =====================================================================================================

        private IEnumerator Run()
        {
            Line("Grade Clip Shoot — Story 1.16. SampleScene, real wiring. " + DateTime.Now.ToString("u", CultureInfo.InvariantCulture) +
                 " (local time). Phases " + phases + ".");
            Line("⚠ LINES THIS RIG PROVOKES ON PURPOSE (Phase F, between its markers): [Grading] GradingConfig unassigned on the " +
                 "ClipRecorder (error, F1); [Recording] No EventManager assigned (warning, F2); [Recording] The audio device or its " +
                 "configuration changed mid-clip (warning, F6). Phase 0 pulls the still Capture() (its normal [Grading] info lines). " +
                 "Anything else is a finding.");

            _ticks.AppendLine("clip,tick,repeats,samples,lifecycle,subject,counted,miss,composition,actorTime,peakDuration,atPeak," +
                              "peakOffset,heightFraction,framedFraction,visibleFraction");
            _clips.AppendLine("clip,id,stop,footageSeconds,footageTicks,gradedSeconds,gradedTicks,percent,stars,miss,composition,framing," +
                              "coverage,before,after,outOfShot,inShot,hasPeak,subject,rating,axes,why,colour,hudShowing,hudAlpha,lines");

            Application.logMessageReceived += OnLog;
            channel.Raised += OnRaised;
            shotChannel.Raised += OnShot;
            recorder.Grader.TickGraded += OnTick;
            recorder.Grader.TickClosed += OnTickClosed;
            _pitchField = typeof(ThirdPersonController).GetField("_pitch", BindingFlags.Instance | BindingFlags.NonPublic);
            _rating = HudLabel("ratingLabel"); _axes = HudLabel("axesLabel"); _why = HudLabel("whyLabel");
            _hudGroup = hud.GetComponent<CanvasGroup>();
            BuildLabel();
            SetupInput();
            _prevMaxClip = recordingConfig.maxClipSeconds;

            yield return null;
            Line($"Screen {Screen.width}x{Screen.height}; GradeHud canvas scale factor {F(hud.GetComponent<Canvas>().scaleFactor)}; " +
                 $"grading config floor {F(gradingConfig.SafeClipPeakFloor)} absence {F(gradingConfig.SafeClipAbsenceWeight)} " +
                 $"shape {gradingConfig.SafeClipBlend.Shape}; recorder grades: {recorder.Grader.CanGrade}; hud hold+fade {F(hudConfig.SafeVisibleSeconds)} s");
            if (Screen.width != 1920 || Screen.height != 1080) Line("⚠ NOT 1920x1080: legibility read from these captures does not carry to 1080p.");

            PlaceAtVantage();
            photo.SetPhotoMode(true);
            for (int i = 0; i < 30; i++) yield return null;

            if (phases.Contains("0")) yield return Phase0();
            if (phases.Contains("S")) yield return PhaseS();
            if (phases.Contains("H")) yield return PhaseH();
            if (phases.Contains("E")) yield return PhaseE();
            if (phases.Contains("F")) yield return PhaseF();
            if (phases.Contains("R")) yield return PhaseR();
            if (phases.Contains("G")) yield return PhaseG();

            Teardown();
            File.WriteAllText(Path.Combine(outputDir, "ticks.csv"), _ticks.ToString());
            File.WriteAllText(Path.Combine(outputDir, "clips.csv"), _clips.ToString());
            File.WriteAllText(Path.Combine(outputDir, "report.txt"), _log.ToString());
            Debug.Log("[GradeClipShoot] DONE — report at " + outputDir + "/report.txt.");

            if (phases.Contains("F"))
            {
                // F7/F8: a subject DESTROYED mid-clip, then leave play mode WITH the clip running (the teardown path).
                // Last, because the pooled drunk does not come back (EventManager keeps the destroyed slot).
                Debug.Log("[GradeClipShoot] F7/F8 marker: destroying the drunk mid-clip, then exiting play mode while recording.");
                photo.SetPhotoMode(true);
                photo.ToggleRecording();
                yield return Wait(0.6f);
                EventActor a = Live();
                if (a != null) Destroy(a.gameObject);
                yield return Wait(0.6f);
                Debug.Log("[GradeClipShoot] F8 marker: exiting play mode while recording=" + recorder.IsRecording);
            }
            UnityEditor.EditorApplication.isPlaying = false;
        }

        // =====================================================================================================
        // Setup and teardown
        // =====================================================================================================

        private Text HudLabel(string field) =>
            typeof(GradeHud).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(hud) as Text;

        private void SetupInput()
        {
            _prevBg = InputSystem.settings.backgroundBehavior;
            _prevEditorInput = InputSystem.settings.editorInputBehaviorInPlayMode;
            InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
            _pad = InputSystem.AddDevice<Gamepad>("GradeClipShootPad");
            _padState = new GamepadState();
            InputSystem.QueueStateEvent(_pad, _padState);
            var pi = player.GetComponent<PlayerInput>();
            pi.actions.FindAction(GameConstants.InputActions.Capture).performed += _ => _captureActions++;
            Line($"Input: virtual gamepad added (id {_pad.deviceId}); background {_prevBg} -> IgnoreFocus for the run.");
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
            rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(12, -12);
            rt.sizeDelta = new Vector2(1500, 110);
            _label = go.AddComponent<Text>();
            _label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _label.fontSize = 20;
            _label.color = Color.yellow;
            _label.alignment = TextAnchor.UpperLeft;
            go.AddComponent<Outline>().effectColor = Color.black;
        }

        private void SetLabel(string caption)
        {
            EventActor a = Live();
            string tick = _haveTick
                ? $"last tick #{_lastTick.Tick}: {(_lastTick.Grade.Counted ? "COUNTED" : "miss " + _lastTick.Grade.MissReason)} · composition {F(_lastTick.Grade.Composition01, "0.00")} · " +
                  $"height {F(_lastTick.Detail.HeightFraction, "0.00")} · framed {F(_lastTick.Detail.FramedFraction, "0.00")} · actor time {F(_lastTick.Sample.ActorTime, "0.00")} s · at peak {_lastTick.Sample.AtPeak}"
                : "no tick graded yet";
            _label.text = $"RIG SNAPSHOT [{_tag}] — {caption}\n{tick}\nrecording {recorder.IsRecording} · clip {F(recorder.ElapsedSeconds, "0.00")} s · " +
                          $"actor time now {(a != null ? F(Tau(a), "0.00") : "-")} · frame {Time.frameCount}";
        }

        private void PlaceAtVantage()
        {
            // Surveyed 2026-11-15 (vantage-survey/): side-on, 6 m south of the peak spot (25.9, 19). Every occlusion ray
            // to the route is clear for x 22-29 and five green route markers render unobstructed. Story 1.15's (32, 8) is
            // NOT used: a pine trunk's collider 8.6 m out blocks every ray to the drunk's early Build (x >= 29.4), so the
            // grader rightly reads Occluded there (smoke run), and pines hide part of the route in its render.
            Vector3 top = new Vector3(26f, 60f, 13f);
            float y = Physics.Raycast(top, Vector3.down, out RaycastHit h, 120f) ? h.point.y : 0.5f;
            _vantage = new Vector3(26f, y + 0.05f, 13f);
            _vantageYaw = 0f;
            var cc = player.GetComponent<CharacterController>();
            cc.enabled = false;
            player.transform.SetPositionAndRotation(_vantage, Quaternion.Euler(0f, _vantageYaw, 0f));
            cc.enabled = true;
            Line($"Vantage ({F(_vantage.x, "0.0")}, {F(_vantage.y, "0.00")}, {F(_vantage.z, "0.0")}) yaw {F(_vantageYaw, "0.0")}°, eye {F(eyeHeight, "0.00")} m (PlayerConfig).");
        }

        private void Teardown()
        {
            if (recorder.IsRecording) photo.ToggleRecording();
            RestoreConfig();
            if (_pad != null) { InputSystem.RemoveDevice(_pad); _pad = null; }
            InputSystem.settings.backgroundBehavior = _prevBg;
            InputSystem.settings.editorInputBehaviorInPlayMode = _prevEditorInput;
            recorder.Grader.TickGraded -= OnTick;
            recorder.Grader.TickClosed -= OnTickClosed;
            channel.Raised -= OnRaised;
            shotChannel.Raised -= OnShot;
            Application.logMessageReceived -= OnLog;
            if (_labelCanvas != null) Destroy(_labelCanvas);
            if (_reviewTouchedScale) { Time.timeScale = _reviewPrevScale; _reviewTouchedScale = false; }
            if (_reviewPrevConcurrent > 0) { SetConcurrent(_reviewPrevConcurrent); _reviewPrevConcurrent = 0; }
            Line($"Teardown: pad removed; input back to {InputSystem.settings.backgroundBehavior}; maxClipSeconds {F(recordingConfig.maxClipSeconds)}; timeScale {Time.timeScale}.");
        }

        private void RestoreConfig()
        {
            if (!_configTouched) return;
            recordingConfig.maxClipSeconds = _prevMaxClip;
            UnityEditor.EditorUtility.SetDirty(recordingConfig);
            UnityEditor.AssetDatabase.SaveAssetIfDirty(recordingConfig);
            _configTouched = false;
        }

        private void OnDestroy()
        {
            if (_pad != null)
            {
                InputSystem.RemoveDevice(_pad); _pad = null;
                InputSystem.settings.backgroundBehavior = _prevBg;
                InputSystem.settings.editorInputBehaviorInPlayMode = _prevEditorInput;
            }
            RestoreConfig();
        }

        // =====================================================================================================
        // Listeners
        // =====================================================================================================

        private void OnRaised(RecordedClip clip)
        {
            _raises++;
            _lastRaised = clip;
            if (_claimNext) { _claimNext = false; clip.Claim(this); }
        }

        private void OnShot(ShotGrade g) { _lastShot = g; _shots++; }

        private void OnLog(string condition, string stack, LogType type)
        {
            if (_collectLogs && !condition.StartsWith("[GradeClipShoot]", StringComparison.Ordinal))
                _logLines.Add(type + ": " + condition.Split('\n')[0]);
        }

        private void OnTick(ClipGrader.TickRecord t)
        {
            _lastTick = t;
            _haveTick = true;
            ClipSample s = t.Sample;
            GradeDetail d = t.Detail;
            _ticks.Append(_tag).Append(',').Append(t.Tick).Append(',').Append(t.Repeats).Append(",1,")
                  .Append(s.Lifecycle).Append(',').Append(s.SubjectId).Append(',').Append(s.Counted ? 1 : 0).Append(',')
                  .Append(s.Miss).Append(',').Append(R(s.Composition01)).Append(',').Append(R(s.ActorTime)).Append(',')
                  .Append(R(s.PeakDuration)).Append(',').Append(s.AtPeak ? 1 : 0).Append(',').Append(R(t.PeakOffset)).Append(',')
                  .Append(R(d.HeightFraction)).Append(',').Append(R(d.FramedFraction)).Append(',').Append(R(d.VisibleFraction)).AppendLine();

            if (_reviewSets != null) _reviewSets.Add(new KeyValuePair<int, int>(t.Tick, s.Lifecycle));

            if (_controlArmed > 0)
            {
                // THE GRADER CONTROL (AC1): pull the still shutter in the SAME LateUpdate, right after the clip's sample,
                // so both grade one camera pose of one subject.
                _controlArmed--;
                int before = _shots;
                photo.Capture();
                _controlRows.Add(_shots > before
                    ? $"tick {t.Tick}: clip sample {(t.Grade.Counted ? "counted" : t.Grade.MissReason.ToString())} comp {R(t.Grade.Composition01)} | " +
                      $"still {(_lastShot.Counted ? "counted" : _lastShot.MissReason.ToString())} comp {R(_lastShot.Composition01)} | " +
                      $"miss equal {t.Grade.MissReason == _lastShot.MissReason}, composition equal {t.Grade.Composition01 == _lastShot.Composition01}"
                    : $"tick {t.Tick}: Capture() raised nothing (mode {photo.Mode})");
            }
        }

        private void OnTickClosed(int tick, int repeats, int samples)
        {
            // TickClosed reports the count AFTER the tick closed; samples were numbered while it was open (0-based).
            if (samples == 0) _ticks.Append(_tag).Append(',').Append(tick - 1).Append(',').Append(repeats).AppendLine(",0,,,,,,,,,,,,");
        }

        // =====================================================================================================
        // Aim (Update runs at -1000: before ThirdPersonController, ThirdPersonCamera (-100) and the recorder)
        // =====================================================================================================

        private void Update()
        {
            if (_aim == Aim.Free) return;
            Vector3 eye = cam.transform.position;
            float yaw, pitch;
            EventActor a = Live();
            if (_aim == Aim.Subject && a != null)
            {
                Bounds b = a.Bounds;
                Vector3 d = b.center - eye;
                float horiz = new Vector2(d.x, d.z).magnitude;
                yaw = Mathf.Atan2(d.x, d.z) * Mathf.Rad2Deg + _yawOffset;
                pitch = -Mathf.Atan2(d.y, horiz) * Mathf.Rad2Deg + _pitchOffset;
                if (_heightTarget > 0f && cameraConfig != null)
                {
                    float fov = 2f * Mathf.Atan(b.size.y / (2f * Mathf.Max(0.5f, d.magnitude) * _heightTarget)) * Mathf.Rad2Deg;
                    photo.SetZoom(Mathf.InverseLerp(cameraConfig.wideFov, cameraConfig.teleFov, fov));
                }
            }
            else
            {
                yaw = _vantageYaw + 150f;
                pitch = 0f;
            }
            player.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            _pitchField?.SetValue(player, Mathf.Clamp(pitch, -80f, 80f));
        }

        private void AimAt(float heightTarget, float yawOffset = 0f, float zoom = -1f)
        {
            _aim = Aim.Subject;
            _yawOffset = yawOffset;
            _pitchOffset = 0f;
            _heightTarget = heightTarget;
            if (zoom >= 0f) photo.SetZoom(zoom);
        }

        private void AimAway() { _aim = Aim.Away; _heightTarget = -1f; }

        // =====================================================================================================
        // The live lifecycle
        // =====================================================================================================

        private EventActor Live()
        {
            IReadOnlyList<EventActor> a = manager.ActiveActors;
            for (int i = 0; i < a.Count; i++)
                if (a[i] != null && a[i].isActiveAndEnabled) return a[i];
            return null;
        }

        private static float Tau(EventActor a) => -a.TimeToPeak;

        private float PeakLength => Live() != null ? Live().PeakDuration : 1.5f;

        /// <summary>Waits until the live subject's own clock reaches <paramref name="tau"/>. If it is already too close
        /// or past, or <paramref name="lifecycles"/> is above 0, waits for that many NEW lifecycles first.</summary>
        private IEnumerator WaitTau(float tau, int lifecycles = 0, float timeout = 90f)
        {
            float end = Time.realtimeSinceStartup + timeout;
            EventActor a = Live();
            int serial = a != null ? a.LifecycleSerial : 0;
            if (lifecycles == 0 && a != null && Tau(a) > tau - 0.3f) lifecycles = 1;
            while (lifecycles > 0 && Time.realtimeSinceStartup < end)
            {
                yield return null;
                a = Live();
                if (a != null && a.LifecycleSerial != serial) { serial = a.LifecycleSerial; lifecycles--; }
            }
            while (Time.realtimeSinceStartup < end)
            {
                a = Live();
                if (a != null && a.LifecycleSerial == serial && Tau(a) >= tau) yield break;
                if (a != null && a.LifecycleSerial != serial) serial = a.LifecycleSerial;
                yield return null;
            }
            Line($"⚠ TIMEOUT waiting for actor time {F(tau)} ({_tag}).");
        }

        private static IEnumerator Wait(float seconds)
        {
            float end = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        // =====================================================================================================
        // Captures
        // =====================================================================================================

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

        private string Lines()
        {
            string Count(Text t) => t == null ? "-" : t.cachedTextGenerator.lineCount + (t.preferredWidth > ((RectTransform)t.transform).rect.width + 0.5f ? "!" : "");
            return $"{Count(_rating)}/{Count(_axes)}/{Count(_why)}";
        }

        // =====================================================================================================
        // One clip
        // =====================================================================================================

        /// <summary>Starts a clip now (the real controller method the Record input calls), records its tag.</summary>
        private void BeginClip(string tag)
        {
            _tag = tag;
            _haveTick = false;
            if (!photo.IsPhotoMode) photo.SetPhotoMode(true);
            if (!recorder.IsRecording) photo.ToggleRecording();
        }

        /// <summary>Stops the clip with Record, grabs the settled readout (~0.5 s later) and writes the clip's row.</summary>
        private IEnumerator EndClip(Func<IEnumerator> stop = null, float settle = 0.5f)
        {
            int raisesBefore = _raises;
            if (stop != null) yield return stop();
            else if (recorder.IsRecording) photo.ToggleRecording();
            float timeout = Time.realtimeSinceStartup + 5f;
            while (_raises == raisesBefore && Time.realtimeSinceStartup < timeout) yield return null;
            yield return Wait(settle);
            yield return Grab(_tag + "_hud", "the readout, ~0.5 s after the stop");
            WriteClip(_tag, _lastRaised, _raises > raisesBefore);
        }

        private void WriteClip(string tag, RecordedClip clip, bool raised, ClipGrader grader = null)
        {
            if (grader == null) grader = recorder.Grader;
            if (!raised || clip == null) { Line($"{tag}: ⚠ NO CLIP RAISED"); return; }
            ClipGrade g = clip.Grade;
            var f = clip.Footage as ReplayFootage;
            Color c = hudConfig.ColorFor(g.Counted, g.IsMiss, g.Stars, g.Percent01);
            string rating = _rating != null ? _rating.text : "?";
            string axes = _axes != null ? _axes.text : "?";
            string why = _why != null ? _why.text : "?";
            string colour = $"{Mathf.RoundToInt(c.r * 255)} {Mathf.RoundToInt(c.g * 255)} {Mathf.RoundToInt(c.b * 255)}";
            float alpha = _hudGroup != null ? _hudGroup.alpha : -1f;
            _clipNo++;
            _clips.Append(tag).Append(',').Append(clip.Id).Append(',').Append(clip.StopReason).Append(',').Append(R(clip.DurationSeconds)).Append(',')
                  .Append(f != null ? f.TickCount : -1).Append(',').Append(R(g.GradedSeconds)).Append(',').Append(grader.TicksGraded).Append(',')
                  .Append(R(g.Percent01)).Append(',').Append(g.Stars).Append(',').Append(g.MissReason).Append(',').Append(R(g.Composition01)).Append(',')
                  .Append(R(g.Framing01)).Append(',').Append(R(g.PeakCoverage01)).Append(',').Append(R(g.PeakBeforeClip01)).Append(',')
                  .Append(R(g.PeakAfterClip01)).Append(',').Append(R(g.PeakOutOfShot01)).Append(',').Append(R(g.InShot01)).Append(',')
                  .Append(g.HasPeak ? 1 : 0).Append(',').Append(g.SubjectId).Append(',').Append(Csv(rating)).Append(',').Append(Csv(axes)).Append(',')
                  .Append(Csv(why)).Append(',').Append(colour).Append(',').Append(hud.IsShowingClip ? 1 : 0).Append(',').Append(R(alpha)).Append(',')
                  .Append(Lines()).AppendLine();
            Line($"{tag}: {clip.Id} {clip.StopReason}, footage {F(clip.DurationSeconds)} s / {(f != null ? f.TickCount : -1)} ticks, graded {F(g.GradedSeconds)} s / " +
                 $"{grader.TicksGraded} ticks -> {g}\n      HUD [{(hud.IsShowingClip ? "showing clip" : hud.IsShowing ? "showing STILL" : "HIDDEN")} alpha {F(alpha, "0.00")}, lines {Lines()}, rgb {colour}]  " +
                 $"'{rating}' | '{axes}' | '{why}'");
        }

        private static string Csv(string s) => "\"" + (s ?? "").Replace("\"", "'").Replace("\n", " ") + "\"";

        /// <summary>A clip on the live lifecycle: start at <paramref name="startTau"/>, stop at <paramref name="stopTau"/>,
        /// grabbing frames at <paramref name="grabTaus"/>. NaN start means now. <paramref name="awayUntilTau"/>: aim away
        /// first and turn to him at that actor time; <paramref name="stopLifecycles"/>: lifecycles that must begin
        /// after the start before the stop time counts (a clip across a respawn).</summary>
        private IEnumerator Clip(string tag, float heightTarget, float yawOffset, float startTau, float stopTau,
                                 float[] grabTaus = null, float awayUntilTau = float.NaN, int startLifecycles = 0,
                                 int stopLifecycles = 0, float zoom = -1f)
        {
            Line($"\n-- {tag} --");
            if (float.IsNaN(awayUntilTau)) AimAt(heightTarget, yawOffset, zoom); else AimAway();
            if (!float.IsNaN(startTau)) yield return WaitTau(startTau, startLifecycles);
            BeginClip(tag);
            EventActor a0 = Live();
            int serial = a0 != null ? a0.LifecycleSerial : 0;
            int changes = 0;
            int nextGrab = 0;
            float end = Time.realtimeSinceStartup + 80f;
            bool turned = float.IsNaN(awayUntilTau);
            while (Time.realtimeSinceStartup < end)
            {
                EventActor a = Live();
                if (a != null && a.LifecycleSerial != serial) { serial = a.LifecycleSerial; changes++; }
                float tau = a != null ? Tau(a) : float.NaN;
                if (!turned && a != null && changes >= stopLifecycles && tau >= awayUntilTau)
                {
                    turned = true;
                    AimAt(heightTarget, yawOffset, zoom);
                    Line($"   turned to him at clip time {F(recorder.ElapsedSeconds)} s (actor time {F(tau)})");
                }
                if (a != null && changes >= stopLifecycles && tau >= stopTau) break;
                if (grabTaus != null && nextGrab < grabTaus.Length && a != null && changes >= stopLifecycles && tau >= grabTaus[nextGrab])
                {
                    yield return Grab($"{tag}_f{nextGrab}", $"key frame at actor time {F(grabTaus[nextGrab], "0.00")} s");
                    nextGrab++;
                    continue;
                }
                yield return null;
            }
            yield return EndClip();
        }

        // =====================================================================================================
        // Phase 0 — controls
        // =====================================================================================================

        private IEnumerator Phase0()
        {
            Line("\n== Phase 0 — controls ==");
            AimAway();
            photo.SetZoom(0f);
            yield return Wait(0.6f);
            yield return Grab("p0_scene", "Phase 0: known 3D content");
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
            Line($"Overlay marker: {F(on * 100, "0.00")} % on, {F(off * 100, "0.00")} % off (1.12 and 1.15 measured 100.00 / 0.00).");

            // The grader control (AC1): a clip aimed at him, then away; on 3 ticks of each, the still shutter is pulled
            // in the same LateUpdate as the clip's sample.
            yield return WaitTau(-5f);
            AimAt(0.55f);
            yield return Wait(0.8f);
            BeginClip("p0_control");
            yield return Wait(0.5f);
            yield return Grab("p0_aimed", "aimed at him (Phase 0)");
            LineOfSightProbe();
            _controlArmed = 3;
            while (_controlArmed > 0) yield return null;
            AimAway();
            yield return Wait(0.6f);
            _controlArmed = 3;
            while (_controlArmed > 0) yield return null;
            yield return EndClip();
            Line("Grader control — the still Capture() and the clip's tick sample on the same frame:");
            foreach (string r in _controlRows) Line("   " + r);
        }

        /// <summary>What the grader's occlusion rays hit, named: from the camera to the subject's bounds centre and the
        /// eight half-inset corners, against the config's occluder mask (the same rays ShotGrader casts).</summary>
        private void LineOfSightProbe()
        {
            EventActor a = Live();
            if (a == null) { Line("Line-of-sight probe: nobody live."); return; }
            Bounds b = a.Bounds;
            Vector3 eye = cam.transform.position;
            Line($"Line-of-sight probe: eye {eye} -> subject centre {b.center} ({F(Vector3.Distance(eye, b.center), "0.00")} m), mask {gradingConfig.occluderMask.value}");
            for (int i = 0; i < 9; i++)
            {
                Vector3 off = i == 0 ? Vector3.zero : new Vector3((i & 1) != 0 ? 1 : -1, (i & 2) != 0 ? 1 : -1, (i & 4) != 0 ? 1 : -1);
                Vector3 p = b.center + Vector3.Scale(b.extents * 0.5f, off);
                if (Physics.Linecast(eye, p, out RaycastHit h, gradingConfig.occluderMask))
                {
                    Transform t = h.collider.transform;
                    string path = t.name;
                    for (Transform q = t.parent; q != null && path.Length < 120; q = q.parent) path = q.name + "/" + path;
                    Line($"   ray {i} BLOCKED at {F(h.distance, "0.00")} m by '{path}' ({h.collider.GetType().Name}, layer {LayerMask.LayerToName(h.collider.gameObject.layer)}, enabled {h.collider.enabled})");
                }
                else Line($"   ray {i} clear");
            }
        }

        // =====================================================================================================
        // Phase S — the scenario clips
        // =====================================================================================================

        private IEnumerator PhaseS()
        {
            Line("\n== Phase S — the scenario clips (each anchored to the live lifecycle) ==");
            float D = PeakLength;
            const float Prominent = 0.55f;   // the subject's share of the frame height for "centred and prominent"
            var keyFrames = new[] { -1.0f, 0.1f, 0.4f, 0.8f, 1.3f, D + 0.5f };

            // S1: the money clip.
            yield return Clip("S1_whole", Prominent, 0f, -2f, D + 1f, keyFrames);

            // S2 + S3 in one lifecycle: same framing, stopped 1 s before the window opens; started after it closes.
            yield return Clip("S2_missed_early", Prominent, 0f, -6f, -1f, new[] { -4f, -1.5f });
            yield return Clip("S3_missed_late", Prominent, 0f, D + 0.5f, D + 4.5f, new[] { D + 1f, D + 4f });

            // S4b' and S4a in one lifecycle: stopped 0.75 s into the window; then started right away (0.75 s in).
            // S4b' stops 0.75 s into the window (no key frame near the stop: a slow capture frame would move it). S4a
            // starts 0.75 s into the NEXT lifecycle's window (starting it right after S4b' lost ~0.9 s to the readout grab).
            yield return Clip("S4_stop_075", Prominent, 0f, -2f, 0.75f, new[] { -1f, 0.2f });
            yield return Clip("S4_start_075", Prominent, 0f, 0.75f, D + 1f, new[] { 1.0f, D + 0.5f });

            // S4b: only the window's first ~0.5 s (the visible-peak lag), then frames after the stop for comparison.
            yield return Clip("S4b_first_half_second", Prominent, 0f, 0f, 0.5f, new[] { 0.05f, 0.25f, 0.45f });
            AimAt(Prominent);
            yield return WaitTau(0.8f);
            yield return Grab("S4b_after_080", "after the clip: actor time 0.80 s (not recorded)");
            yield return WaitTau(1.2f);
            yield return Grab("S4b_after_120", "after the clip: actor time 1.20 s (not recorded)");

            // S5: the framing ladder over the whole window (centred = S1).
            // Yaw offsets for the ~29° vertical FOV the 0.55 hold gives at ~6 m (horizontal ~49°): -12° puts his centre
            // near 0.73 of the width; -25° puts about half his box past the right edge.
            yield return Clip("S5_off_centre", Prominent, -12f, -2f, D + 1f, new[] { 0.4f });
            yield return Clip("S5_cut_off", Prominent, -25f, -2f, D + 1f, new[] { 0.4f });
            yield return Clip("S5_small", 0.25f, 0f, -2f, D + 1f, new[] { 0.4f });

            // S6: S1 preceded by ~5 s and ~20 s of aiming away.
            yield return Clip("S6_away_5s", Prominent, 0f, -7f, D + 1f, new[] { -4f, 0.4f }, awayUntilTau: -2f);
            float start20 = Away20Start();
            Line($"S6_away_20s starts at actor time {F(start20)} of the lifecycle BEFORE (predicted 20 s to the next one's -2 s).");
            yield return Clip("S6_away_20s", Prominent, 0f, start20, D + 1f, new[] { 0.4f }, awayUntilTau: -2f, stopLifecycles: 1);

            // S8: across a respawn. Framed from the old lifecycle's wind-down to the new one's peak.
            yield return Clip("S8_across_respawn", Prominent, 0f, D + 2f, D + 1f, new[] { D + 4f, 0.4f }, stopLifecycles: 1);

            // S7: misses.
            yield return PhaseS7();

            // S9: every stop route, the readout grabbed after each.
            yield return PhaseS9();

            // S10: ten clips in a row, alternating framed and away, so their grades differ.
            Line("\n-- S10: ten consecutive clips --");
            AimAt(Prominent);
            yield return WaitTau(-7f);
            for (int i = 0; i < 10; i++)
            {
                if (i % 2 == 0) AimAt(Prominent); else AimAway();
                yield return Wait(0.3f);
                BeginClip($"S10_{i}");
                yield return Wait(0.8f + 0.15f * i);
                yield return EndClip(settle: 0.1f);
            }

            // S11: stopped in the frame it started (the start tick only).
            Line("\n-- S11: stopped in the frame it started --");
            AimAt(Prominent);
            yield return Wait(0.5f);
            _tag = "S11_same_frame";
            int raises = _raises;
            photo.ToggleRecording();
            photo.ToggleRecording();
            yield return null;
            yield return Wait(0.5f);
            yield return Grab("S11_same_frame_hud", "start and stop in one frame");
            WriteClip("S11_same_frame", _lastRaised, _raises > raises);
        }

        private float Away20Start()
        {
            EventActor a = Live();
            if (a == null || a.Definition == null) return 4.5f;
            float total = 0f;
            foreach (EventPhase p in new[] { EventPhase.Spawn, EventPhase.Build, EventPhase.Peak, EventPhase.WindDown, EventPhase.Despawn })
                total += a.Definition.GetPhase(p).duration;
            float preRoll = a.Definition.GetPhase(EventPhase.Spawn).duration + a.Definition.GetPhase(EventPhase.Build).duration;
            object rd = typeof(EventManager).GetField("respawnDelay", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(manager);
            float respawn = rd is float r ? r : 2f;
            float tauEnd = total - preRoll;
            // From tau_s to the end, the respawn gap, then the new lifecycle from -preRoll to -2.
            return tauEnd + respawn + (preRoll - 2f) - 20f;
        }

        private IEnumerator PhaseS7()
        {
            Line("\n-- S7: misses --");
            // Nobody there: DEACTIVATE the live actors and put them back (disabling the manager despawns nobody).
            yield return WaitTau(-10f);
            var hidden = new List<GameObject>();
            foreach (EventActor a in manager.ActiveActors) if (a != null && a.gameObject.activeSelf) { a.gameObject.SetActive(false); hidden.Add(a.gameObject); }
            AimAt(0.55f);
            _aim = Aim.Free;
            yield return Wait(0.3f);
            BeginClip("S7_nobody");
            yield return Wait(1.5f);
            yield return Grab("S7_nobody_f0", "nobody live (the actors are deactivated)");
            yield return EndClip();
            foreach (GameObject g in hidden) g.SetActive(true);
            Line($"   put back {hidden.Count} actor(s); live again: {Live() != null}");

            // Too far: 10 m further back on the same line (~16 m), wide zoom, aimed at him. (From the 6-m vantage he
            // fills ~26 % of the frame even at the wide end, which passes the 20 % gate.)
            var cc = player.GetComponent<CharacterController>();
            Vector3 back = _vantage + new Vector3(0f, 0f, -10f);
            back.y = Physics.Raycast(back + Vector3.up * 60f, Vector3.down, out RaycastHit bh, 120f) ? bh.point.y + 0.05f : _vantage.y;
            cc.enabled = false; player.transform.position = back; cc.enabled = true;
            AimAt(-1f, 0f, zoom: 0f);
            yield return Wait(0.8f);
            BeginClip("S7_too_far");
            yield return Wait(1.0f);
            yield return Grab("S7_too_far_f0", "wide zoom, aimed at him, 10 m further back");
            yield return EndClip();
            cc.enabled = false; player.transform.position = _vantage; cc.enabled = true;

            // Out of frame: aimed away.
            AimAway();
            yield return Wait(0.5f);
            BeginClip("S7_out_of_frame");
            yield return Wait(1.0f);
            yield return Grab("S7_out_of_frame_f0", "aimed away from him");
            yield return EndClip();

            // Occluded: a rig WALL on the Default layer (what occluderMask blocks; the town's pines carry no colliders,
            // lessons 67 and 169) between the eye and him, confirmed by rendering.
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "RigOccluderWall";
            wall.layer = 0;
            wall.GetComponent<Renderer>().material.color = new Color(0.55f, 0.45f, 0.35f);
            AimAt(0.55f);
            yield return Wait(0.8f);
            PlaceWall(wall);
            yield return Wait(0.3f);
            BeginClip("S7_occluded");
            yield return Wait(1.0f);
            yield return Grab("S7_occluded_f0", "a rig wall between the eye and him");
            yield return EndClip();

            // Mixed: ~3 s out of frame, then a ~0.3 s glimpse of him behind the wall (MostTime vs DeepestGate, decision 1e).
            AimAway();
            yield return Wait(0.5f);
            BeginClip("S7_mixed_away_then_wall");
            yield return Wait(3.0f);
            PlaceWall(wall);   // where he is NOW (he walks; the wall from the clip before no longer covers him)
            AimAt(0.55f);
            yield return Wait(0.3f);
            yield return Grab("S7_mixed_away_then_wall_f0", "the short glimpse behind the wall");
            yield return EndClip();
            Destroy(wall);
        }

        private void PlaceWall(GameObject wall)
        {
            EventActor who = Live();
            if (who == null) return;
            Vector3 eye = cam.transform.position, to = who.Bounds.center - eye;
            wall.transform.position = eye + to.normalized * 4f;
            wall.transform.rotation = Quaternion.LookRotation(new Vector3(to.x, 0f, to.z));
            wall.transform.localScale = new Vector3(4f, 4f, 0.3f);
        }

        private IEnumerator PhaseS9()
        {
            Line("\n-- S9: every stop route (readout grabbed ~0.5 s after the stop and near the end of its hold) --");
            float hold = hudConfig.SafeHoldSeconds;

            // Anchored to the clear stretch of his walk, so the readouts below describe a subject who is live and visible.
            AimAt(0.55f);
            yield return WaitTau(-7f);

            // Record pressed again: every clip above. Lowering the camera:
            AimAt(0.55f);
            yield return Wait(0.5f);
            BeginClip("S9_lowered");
            yield return Wait(1.5f);
            yield return EndClip(() => Lower());
            yield return Wait(Mathf.Max(0f, hold - 0.9f));
            yield return Grab("S9_lowered_hud_late", "near the end of the hold, camera down");
            Line($"   near the end of the hold: HUD showing clip {hud.IsShowingClip}, alpha {F(_hudGroup != null ? _hudGroup.alpha : -1f, "0.00")}, mode {photo.Mode}");

            // The gallery opened mid-hold: the readout must be gone that frame.
            photo.SetPhotoMode(true);
            AimAt(0.55f);
            yield return Wait(0.4f);
            BeginClip("S9_gallery_mid_hold");
            yield return Wait(1.0f);
            yield return EndClip(() => Lower());
            bool before = hud.IsShowingClip;
            gallery.Open();
            bool sameFrame = hud.IsShowing;
            yield return Grab("S9_gallery_open", "the gallery opened mid-hold");
            Line($"   gallery opened mid-hold: showing before {before}, right after Open() {sameFrame}, at the end of that frame {hud.IsShowing} (gallery open {gallery.IsOpen})");
            gallery.Close();
            yield return null;

            // A clip stopped BY a screen owner's suppression. The gallery itself opens only from Walk (GalleryView.Open),
            // so this is the switch a future pause menu would throw while the camera is up: it lowers the camera, which
            // stops the clip, and the readout must not appear over the owner's screen.
            photo.SetPhotoMode(true);
            AimAt(0.55f);
            yield return Wait(0.4f);
            BeginClip("S9_stopped_by_suppression");
            yield return Wait(1.0f);
            yield return EndClip(() => Suppress(), settle: 0.1f);
            Line($"   suppression on: HUD showing {hud.IsShowing}, mode {photo.Mode}");
            photo.SetRaiseSuppressed(false);
            yield return null;

            // The length limit (maxClipSeconds 3, restored and read back).
            photo.SetPhotoMode(true);
            recordingConfig.maxClipSeconds = 3f;
            _configTouched = true;
            AimAt(0.55f);
            yield return Wait(0.4f);
            BeginClip("S9_length_limit");
            yield return EndClip(() => WaitStopped(6f));
            RestoreConfig();
            Line($"   maxClipSeconds restored: {F(recordingConfig.maxClipSeconds)} (asset read back)");
        }

        private IEnumerator Lower() { photo.SetPhotoMode(false); yield break; }
        private IEnumerator Suppress() { photo.SetRaiseSuppressed(true); yield break; }

        private IEnumerator WaitStopped(float timeout)
        {
            float end = Time.realtimeSinceStartup + timeout;
            while (recorder.IsRecording && Time.realtimeSinceStartup < end) yield return null;
        }

        // =====================================================================================================
        // Phase H — the readout through the real input
        // =====================================================================================================

        private void Pad(float? lt = null, float? rt = null)
        {
            if (lt.HasValue) _padState.leftTrigger = lt.Value;
            if (rt.HasValue) _padState.rightTrigger = rt.Value;
            InputSystem.QueueStateEvent(_pad, _padState);
        }

        private IEnumerator PhaseH()
        {
            Line("\n== Phase H — the readout through the real input (virtual gamepad -> PlayerInput -> PhotoModeController) ==");
            photo.SetPhotoMode(false);
            Pad(lt: 0f, rt: 0f);
            yield return Wait(0.5f);
            AimAt(0.55f);

            for (int take = 0; take < 2; take++)
            {
                bool byLowering = take == 1;
                string tag = byLowering ? "H_stop_by_LT_release" : "H_stop_by_RT";
                _tag = tag;
                Pad(lt: 1f);
                for (int i = 0; i < 6; i++) yield return null;
                int actions = _captureActions;
                Pad(rt: 1f); yield return null; yield return null; Pad(rt: 0f); yield return null;
                yield return null;
                Line($"{tag}: LT raised -> mode {photo.Mode}; RT press arrived {_captureActions > actions} (Capture performed {_captureActions - actions}); recording {recorder.IsRecording}");
                yield return Wait(2.0f);

                int raises = _raises;
                long t0 = Ts();
                int f0 = Time.frameCount;
                if (byLowering) Pad(lt: 0f);
                else { Pad(rt: 1f); }
                double msRaise = -1, msShow = -1;
                int frames = 0;
                while (frames < 60)
                {
                    yield return null;
                    frames++;
                    if (msRaise < 0 && _raises > raises) msRaise = Ms(t0, Ts());
                    if (msShow < 0 && hud.IsShowingClip) { msShow = Ms(t0, Ts()); break; }
                }
                if (!byLowering) Pad(rt: 0f);
                yield return Grab(tag + "_hud_first", "first frame the readout is up");
                Line($"   stop press -> clip announced {F(msRaise, "0.0")} ms, readout up {F(msShow, "0.0")} ms ({Time.frameCount - f0} frames); mode {photo.Mode}");
                yield return Wait(0.5f);
                yield return Grab(tag + "_hud", "the readout, ~0.5 s after the stop");
                WriteClip(tag, _lastRaised, _raises > raises);
                yield return Wait(hudConfig.SafeVisibleSeconds);
            }
            Pad(lt: 0f, rt: 0f);
            yield return Wait(0.3f);
        }

        // =====================================================================================================
        // Phase E — cost and allocation
        // =====================================================================================================

        private static long HeapGrowth(Action body)
        {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long before = GC.GetTotalMemory(false);
            body();
            return GC.GetTotalMemory(false) - before;
        }

        private static byte[] _sink;
        private static object _sinkObject;

        private IEnumerator PhaseE()
        {
            Line("\n== Phase E — cost and allocation (live drunk, SampleScene) ==");
            AimAt(0.55f);
            photo.SetPhotoMode(true);
            yield return WaitTau(-6f);
            EventActor a = Live();
            const int N = 9000;

            // ShotGrader.Grade per call.
            ShotGrader.Grade(cam, a, gradingConfig, out _);
            var sw = Stopwatch.StartNew();
            for (int i = 0; i < N; i++) ShotGrader.Grade(cam, a, gradingConfig, out _);
            sw.Stop();
            ShotGrade one = ShotGrader.Grade(cam, a, gradingConfig, out GradeDetail d);
            Line($"ShotGrader.Grade: {F(sw.Elapsed.TotalMilliseconds * 1000.0 / N, "0.000")} µs per call over {N} calls ({(one.Counted ? "counted" : one.MissReason.ToString())}, " +
                 $"visible {d.VisibleText}; 2026-07-26 measured 7.2 µs against 16,739 colliders)");

            // The clip grader per tick: a SEPARATE instance on the same camera, manager and config (the recorder's own clip is untouched).
            var g = new ClipGrader(cam, manager, gradingConfig, RecordingConfig.MaxActorsPerTickOf(recordingConfig));
            g.Begin();
            for (int i = 0; i < 10; i++) g.SampleTick(1);
            sw.Restart();
            for (int i = 0; i < N; i++) g.SampleTick(1);
            sw.Stop();
            Line($"ClipGrader.SampleTick: {F(sw.Elapsed.TotalMilliseconds * 1000.0 / N, "0.000")} µs per tick over {N} ticks (one live actor); its grade {g.Finish()}");

            g.Begin();
            for (int i = 0; i < 10; i++) g.SampleTick(1);
            // The positive control RETAINS what it allocates (a garbage byte[] can be collected mid-loop and read 0 B,
            // which run1 did), and every loop also counts gen-0 collections: 0 B with 0 collections is a real zero.
            long[] growth = new long[3];
            int[] gcs = new int[3];
            for (int r = 0; r < 3; r++)
            {
                int c0 = GC.CollectionCount(0);
                growth[r] = HeapGrowth(() => { for (int i = 0; i < N; i++) g.SampleTick(1); });
                gcs[r] = GC.CollectionCount(0) - c0 - 2;   // minus HeapGrowth's own two Collect() calls
            }
            // Two controls. run3 measured a RETAINED byte[64] per call (576 KB) as 0 B: in this Mono, GetTotalMemory does not
            // see pointer-free arrays that way. Story 1.15's control, a retained new object() per call, does register.
            var keepBytes = new byte[N][];
            var keepObjects = new object[N];
            int cc0 = GC.CollectionCount(0);
            long controlBytes = HeapGrowth(() => { for (int i = 0; i < N; i++) { g.SampleTick(1); keepBytes[i] = new byte[64]; } });
            long control = HeapGrowth(() => { for (int i = 0; i < N; i++) { g.SampleTick(1); keepObjects[i] = new object(); } });
            int controlGcs = GC.CollectionCount(0) - cc0 - 4;   // HeapGrowth's own two Collect() calls, twice
            _sink = keepBytes[N - 1];
            _sinkObject = keepObjects[N - 1];
            long finish = HeapGrowth(() => { for (int i = 0; i < 100; i++) g.Finish(); });
            Line($"Heap growth over {N} full SampleTick calls: {growth[0]} / {growth[1]} / {growth[2]} B, gen-0 collections {gcs[0]} / {gcs[1]} / {gcs[2]} (3 rounds); " +
                 $"positive controls: + a RETAINED new object() per call (>= {N * 16} B) {control} B; + a RETAINED new byte[64] per call ({N * 64} B) {controlBytes} B; {controlGcs} collections inside them; " +
                 $"100 Finish() calls: {finish} B (Finish runs once per clip).");

            // GC collections while the real recorder records a 10-s graded clip.
            int gc0 = GC.CollectionCount(0);
            BeginClip("E_gc_10s");
            yield return Wait(10f);
            yield return EndClip(settle: 0.1f);
            Line($"GC gen-0 collections during a 10-s recorded, graded clip: {GC.CollectionCount(0) - gc0} (the editor allocates on its own; the build bench is the real measure).");
        }

        // =====================================================================================================
        // Phase F — fail-soft (provoked)
        // =====================================================================================================

        private ClipRecorder MakeRecorder(GradingConfig cfg, EventManager em)
        {
            var go = new GameObject("RigThrowawayRecorder");
            go.SetActive(false);
            var r = go.AddComponent<ClipRecorder>();
            void Set(string field, object v) =>
                typeof(ClipRecorder).GetField(field, BindingFlags.Instance | BindingFlags.NonPublic).SetValue(r, v);
            Set("clipRecordedChannel", channel);
            Set("config", recordingConfig);
            Set("recordCamera", cam);
            Set("eventManager", em);
            Set("listenerTap", tap);
            Set("gradingConfig", cfg);
            go.SetActive(true);
            return r;
        }

        private IEnumerator Throwaway(string tag, GradingConfig cfg, EventManager em, bool disableMidClip = false)
        {
            _logLines.Clear(); _collectLogs = true;
            ClipRecorder r = MakeRecorder(cfg, em);
            yield return null;
            _tag = tag;
            int raises = _raises;
            r.Begin();
            yield return Wait(1.0f);
            if (disableMidClip) r.enabled = false; else r.Stop(StopReason.PlayerPressed);
            yield return Wait(0.5f);
            _collectLogs = false;
            yield return Grab(tag + "_hud", "the shipped HUD, showing the throwaway recorder's clip");
            WriteClip(tag, _lastRaised, _raises > raises, r.Grader);
            Line($"   log lines: {(_logLines.Count == 0 ? "none" : string.Join(" || ", _logLines))}");
            Destroy(r.gameObject);
            yield return null;
        }

        private IEnumerator PhaseF()
        {
            Line("\n== Phase F — fail-soft (PROVOKED lines between the markers) ==");
            Debug.Log("[GradeClipShoot] PHASE F BEGIN — the lines below are provoked.");
            AimAt(0.55f);
            photo.SetPhotoMode(true);
            yield return Wait(0.5f);

            yield return Throwaway("F1_no_grading_config", null, manager);
            yield return Throwaway("F2_no_event_manager", gradingConfig, null);

            // F3: bad config values. The validator's ONE message (PhotoModeController.Awake logs it in the shipped wiring),
            // and a clip graded through each bad config: sane numbers, no line per tick, nothing thrown.
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity, 7f, -3f })
            {
                var c = Object.Instantiate(gradingConfig);
                c.clipPeakFloor = bad;
                c.clipAbsenceWeight = bad;
                c.TryGetConfigProblem(out string problem);
                Line($"F3 floor and absence = {bad}: validator -> \"{problem}\"");
                yield return Throwaway($"F3_bad_config_{bad}", c, manager);
                Destroy(c);
            }

            // F4: the drunk deactivated mid-clip (and put back).
            _logLines.Clear(); _collectLogs = true;
            BeginClip("F4_drunk_deactivated_mid_clip");
            yield return Wait(1.0f);
            EventActor a = Live();
            if (a != null) a.gameObject.SetActive(false);
            yield return Wait(1.0f);
            yield return EndClip();
            if (a != null) a.gameObject.SetActive(true);
            _collectLogs = false;
            Line($"   log lines: {(_logLines.Count == 0 ? "none" : string.Join(" || ", _logLines))}");

            // F5: a recorder disabled mid-clip.
            yield return Throwaway("F5_recorder_disabled_mid_clip", gradingConfig, manager, disableMidClip: true);

            // F6: the audio device changing mid-clip (AudioSettings.Reset, lesson 178).
            _logLines.Clear(); _collectLogs = true;
            BeginClip("F6_audio_device_changed");
            yield return Wait(1.0f);
            AudioSettings.Reset(AudioSettings.GetConfiguration());
            yield return EndClip(() => WaitStopped(3f));
            _collectLogs = false;
            Line($"   log lines: {(_logLines.Count == 0 ? "none" : string.Join(" || ", _logLines))}");

            Debug.Log("[GradeClipShoot] PHASE F END — anything after this is not provoked (except F7/F8, announced).");
        }

        // =====================================================================================================
        // Phase R — code review stress (Story 1.16 review, 2026-11-15). Paths the dev phases did not run live:
        // rapid reuse, timeScale 2 and 0.5, frame hitches past Time.maximumDeltaTime, a full-length 120-s clip
        // across several lifecycles, and several concurrent actors. Every clip also checks, tick by tick, that the
        // lifecycles the grader graded are the lifecycles the footage holds live (the "grade describes exactly the
        // frames the clip holds" invariant, at actor level). PROVOKED: with 3 concurrent actors and
        // maxActorsPerTick 2, "[Recording] More event actors are live than a tick can hold" and "[Grading] More event
        // actors are live than a clip grades per tick" (warnings, once each).
        // =====================================================================================================

        private List<KeyValuePair<int, int>> _reviewSets;
        private bool _reviewTouchedScale;
        private float _reviewPrevScale = 1f;
        private int _reviewPrevConcurrent;

        private int GetConcurrent() =>
            (int)typeof(EventManager).GetField("maxConcurrent", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(manager);

        private void SetConcurrent(int n) =>
            typeof(EventManager).GetField("maxConcurrent", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(manager, n);

        private int LiveCount()
        {
            int n = 0;
            IReadOnlyList<EventActor> a = manager.ActiveActors;
            for (int i = 0; i < a.Count; i++) if (a[i] != null && a[i].isActiveAndEnabled) n++;
            return n;
        }

        /// <summary>Compares, tick by tick, the lifecycles the grader graded with the LIVE lifecycles the footage holds.</summary>
        private void CompareSets(string tag)
        {
            var f = _lastRaised != null ? _lastRaised.Footage as ReplayFootage : null;
            if (f == null || f.IsDisposed || _reviewSets == null) { Line($"   {tag}: actor-set check skipped (no footage)"); return; }
            var graded = new Dictionary<int, HashSet<int>>();
            foreach (var kv in _reviewSets)
            {
                if (!graded.TryGetValue(kv.Key, out var set)) graded[kv.Key] = set = new HashSet<int>();
                set.Add(kv.Value);
            }
            int mismatched = 0, firstBad = -1, gradedNotInFootage = 0, footageNotGraded = 0;
            string example = "";
            for (int i = 0; i < f.TickCount; i++)
            {
                TickSample ts = f.TickAt(i);
                var held = new HashSet<int>();
                for (int k = 0; k < ts.ActorCount; k++)
                {
                    ActorSample a = f.ActorAt(ts.ActorStart + k);
                    if (a.Live) held.Add(a.Lifecycle);
                }
                graded.TryGetValue(i, out var g);
                g = g ?? new HashSet<int>();
                if (!held.SetEquals(g))
                {
                    mismatched++;
                    foreach (int x in g) if (!held.Contains(x)) gradedNotInFootage++;
                    foreach (int x in held) if (!g.Contains(x)) footageNotGraded++;
                    if (firstBad < 0)
                    {
                        firstBad = i;
                        example = $"tick {i}: footage live {{{string.Join(",", held)}}} vs graded {{{string.Join(",", g)}}}";
                    }
                }
            }
            Line($"   {tag}: actor sets, footage vs grader: {f.TickCount - mismatched}/{f.TickCount} ticks equal; graded-but-not-in-footage samples " +
                 $"{gradedNotInFootage}, in-footage-but-not-graded {footageNotGraded}{(firstBad >= 0 ? "; first: " + example : "")}");
        }

        private IEnumerator ReviewClip(string tag, float heightTarget, float startTau, float stopTau, float[] grabs = null, int stopLifecycles = 0)
        {
            _reviewSets = new List<KeyValuePair<int, int>>();
            yield return Clip(tag, heightTarget, 0f, startTau, stopTau, grabs, stopLifecycles: stopLifecycles);
            CompareSets(tag);
            _reviewSets = null;
        }

        private IEnumerator PhaseR()
        {
            Line("\n== Phase R — code review stress (PROVOKED, R6 only: the two 'More event actors are live' warnings) ==");
            float D = PeakLength;
            const float Prominent = 0.55f;
            photo.SetPhotoMode(true);
            AimAt(Prominent);
            yield return Wait(0.5f);

            // R1: rapid reuse. Twelve clips, each 0-3 frames long, every stop followed by the next start in the SAME
            // frame (ToggleRecording twice), through the real controller method.
            Line("\n-- R1: twelve rapid clips, stop and start in one frame --");
            int raises0 = _raises;
            var rapid = new List<string>();
            _tag = "R1_rapid";
            photo.ToggleRecording();
            for (int i = 0; i < 12; i++)
            {
                for (int k = 0; k < i % 4; k++) yield return null;
                int before = _raises;
                photo.ToggleRecording();                      // stop
                RecordedClip c = _raises > before ? _lastRaised : null;
                if (c != null)
                {
                    var f = c.Footage as ReplayFootage;
                    rapid.Add($"#{i} frames {i % 4}: footage {F(c.DurationSeconds)} s / {(f != null ? f.TickCount : -1)} ticks, graded " +
                              $"{F(c.Grade.GradedSeconds)} s / {recorder.Grader.TicksGraded} ticks, {(c.Grade.Counted ? "counted " + GradeText.Percent(c.Grade.Percent01) : c.Grade.MissReason.ToString())}");
                }
                else rapid.Add($"#{i}: NO CLIP RAISED");
                if (i < 11) photo.ToggleRecording();          // start the next one in the same frame
            }
            Line($"   {_raises - raises0} clips raised:");
            foreach (string r in rapid) Line("   " + r);
            yield return Wait(0.5f);

            // R2/R3: the whole window, framed, at timeScale 2 and 0.5. Coverage is measured on the subject's clock, so
            // it must still read 100 % (decision 1c's claim, so far proven only in EditMode tests).
            foreach (float ts in new[] { 2f, 0.5f })
            {
                _reviewPrevScale = Time.timeScale;
                _reviewTouchedScale = true;
                Time.timeScale = ts;
                yield return ReviewClip($"R2_timescale_{F(ts, "0.0")}", Prominent, -2f, D + 1f, new[] { 0.4f });
                Time.timeScale = _reviewPrevScale;
                _reviewTouchedScale = false;
            }

            // R4: frame hitches inside the peak window, each longer than Time.maximumDeltaTime, so the subject's clock
            // (clamped deltaTime) and the clip's clock (real time, repeats > 1) part company.
            Line($"\n-- R4_hitches -- (Time.maximumDeltaTime {F(Time.maximumDeltaTime)} s; three 450-ms stalls at actor time 0.2, 0.7, 1.2)");
            _reviewSets = new List<KeyValuePair<int, int>>();
            AimAt(Prominent);
            yield return WaitTau(-2f);
            BeginClip("R4_hitches");
            float[] stallAt = { 0.2f, 0.7f, 1.2f };
            int stalls = 0, maxRepeats = 0;
            float endBy = Time.realtimeSinceStartup + 30f;
            while (Time.realtimeSinceStartup < endBy)
            {
                EventActor a = Live();
                if (a == null) { yield return null; continue; }
                float tau = Tau(a);
                if (tau >= D + 1f) break;
                if (stalls < stallAt.Length && tau >= stallAt[stalls]) { System.Threading.Thread.Sleep(450); stalls++; }
                if (_haveTick && _lastTick.Repeats > maxRepeats) maxRepeats = _lastTick.Repeats;
                yield return null;
            }
            yield return EndClip();
            Line($"   stalls {stalls}, largest tick repeat count seen {maxRepeats}");
            CompareSets("R4_hitches");
            _reviewSets = null;

            // R5: the full-length clip, 120 s at the length limit, framed throughout, across several lifecycles.
            Line("\n-- R5_full_length -- (framed for the whole clip; stops by itself at maxClipSeconds)");
            _reviewSets = new List<KeyValuePair<int, int>>();
            AimAt(Prominent);
            yield return WaitTau(-12f);
            BeginClip("R5_full_length");
            yield return EndClip(() => WaitStopped(recordingConfig.SafeMaxClipSeconds + 5f));
            CompareSets("R5_full_length");
            _reviewSets = null;

            // R6: several concurrent actors (EventManager.maxConcurrent 3, restored), more than RecordingConfig's
            // maxActorsPerTick. R6a films with all three live; R6b deactivates the FIRST actor in ActiveActors mid-clip
            // (and puts it back), the shape in which the recorder's and the grader's per-tick actor caps count differently.
            Line($"\n-- R6: several concurrent actors (maxConcurrent {GetConcurrent()} -> 3; maxActorsPerTick {recordingConfig.SafeMaxActorsPerTick}) --");
            _reviewPrevConcurrent = GetConcurrent();
            SetConcurrent(3);
            float until = Time.realtimeSinceStartup + 40f;
            while (LiveCount() < 3 && Time.realtimeSinceStartup < until) yield return null;
            Line($"   live actors: {LiveCount()} (ActiveActors {manager.ActiveActors.Count})");
            AimAt(Prominent);
            yield return Wait(0.5f);

            _reviewSets = new List<KeyValuePair<int, int>>();
            BeginClip("R6a_three_live");
            yield return Wait(3f);
            yield return Grab("R6a_three_live_f0", "three concurrent actors, aimed at the first live one");
            yield return EndClip();
            CompareSets("R6a_three_live");

            _reviewSets = new List<KeyValuePair<int, int>>();
            BeginClip("R6b_first_deactivated");
            yield return Wait(1f);
            GameObject first = manager.ActiveActors.Count > 0 && manager.ActiveActors[0] != null ? manager.ActiveActors[0].gameObject : null;
            if (first != null) first.SetActive(false);
            Line($"   deactivated ActiveActors[0] '{(first != null ? first.name : "-")}'; live now {LiveCount()}");
            yield return Wait(2f);
            yield return EndClip();
            CompareSets("R6b_first_deactivated");
            _reviewSets = null;
            if (first != null) first.SetActive(true);

            // Allocation with three live actors (a SEPARATE grader; the recorder's clip is untouched).
            var g = new ClipGrader(cam, manager, gradingConfig, RecordingConfig.MaxActorsPerTickOf(recordingConfig));
            g.Begin();
            for (int i = 0; i < 10; i++) g.SampleTick(1);
            const int N = 9000;
            long growth = HeapGrowth(() => { for (int i = 0; i < N; i++) g.SampleTick(1); });
            var keep = new object[N];
            long control = HeapGrowth(() => { for (int i = 0; i < N; i++) { g.SampleTick(1); keep[i] = new object(); } });
            _sinkObject = keep[N - 1];
            Line($"   heap growth over {N} SampleTick calls with {LiveCount()} live actors: {growth} B (control, a retained object per call: {control} B); its grade {g.Finish()}");

            SetConcurrent(_reviewPrevConcurrent);
            Line($"   maxConcurrent restored to {GetConcurrent()}");
            _reviewPrevConcurrent = 0;
        }

        // =====================================================================================================
        // Phase G — allocation COUNTED, not inferred from heap growth (Story 1.16 review, 2026-11-15). In this Mono,
        // GC.GetTotalMemory and Profiler.GetMonoUsedSizeLong both read 0 B for 9,000 RETAINED strings or byte[64]
        // (measured in the editor), so the 0 B of Phase E proves only that no OBJECT is allocated. The profiler's
        // "GC Allocated In Frame" counter is read for whole frames that do nothing but the work under test, beside a
        // baseline frame, a string control and an object control.
        // =====================================================================================================

        private static string[] _sinkStrings;

        private IEnumerator PhaseG()
        {
            Line("\n== Phase G — per-tick allocation counted by the profiler (GC Allocated In Frame), with controls ==");
            AimAt(0.55f);
            photo.SetPhotoMode(true);
            yield return WaitTau(-6f);
            var rec = Unity.Profiling.ProfilerRecorder.StartNew(Unity.Profiling.ProfilerCategory.Memory, "GC Allocated In Frame");
            if (!rec.Valid) { Line("⚠ the 'GC Allocated In Frame' counter is not available here; nothing measured."); rec.Dispose(); yield break; }

            var g = new ClipGrader(cam, manager, gradingConfig, RecordingConfig.MaxActorsPerTickOf(recordingConfig));
            g.Begin();
            for (int i = 0; i < 10; i++) g.SampleTick(1);
            const int N = 9000;
            var keepS = new string[N];
            var keepO = new object[N];
            var rows = new List<string>();
            double tickFrameMs = 0;
            for (int round = 0; round < 3; round++)
            {
                for (int kind = 0; kind < 5; kind++)
                {
                    yield return null;                       // start a clean frame
                    var sw = Stopwatch.StartNew();
                    if (kind == 1) { for (int i = 0; i < N; i++) g.SampleTick(1); tickFrameMs = sw.Elapsed.TotalMilliseconds; }
                    else if (kind == 2) for (int i = 0; i < N; i++) keepS[i] = new string('x', 8 + (i & 7));
                    else if (kind == 3) for (int i = 0; i < N; i++) keepO[i] = new object();
                    else if (kind == 4) while (sw.Elapsed.TotalMilliseconds < tickFrameMs) { }   // a frame as LONG as the ticks', allocating nothing
                    double ms = sw.Elapsed.TotalMilliseconds;
                    yield return null;                       // that frame is now complete
                    long bytes = rec.LastValue;
                    string name = kind == 0 ? "baseline (nothing)" : kind == 1 ? $"{N} SampleTick, {LiveCount()} live" :
                                  kind == 2 ? $"{N} retained strings (control)" : kind == 3 ? $"{N} retained objects (control)" :
                                  "a busy-wait as long as the SampleTick frame (control: frame length alone)";
                    rows.Add($"round {round}: {name}: {bytes} B ({F(ms, "0")} ms of work)");
                }
            }
            _sinkStrings = keepS;
            _sinkObject = keepO[N - 1];
            rec.Dispose();
            foreach (string r in rows) Line("   " + r);
            Line($"   (the grader's own grade over these ticks: {g.Finish()})");
        }
    }
}
#endif
