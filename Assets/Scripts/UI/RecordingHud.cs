using UnityEngine;
using UnityEngine.UI;
using CameraGame.Core;
using CameraGame.PhotoMode;
using CameraGame.Recording;

namespace CameraGame.UI
{
    /// <summary>
    /// The camcorder's REC feedback (Story 1.15, AC1): a red <c>● REC</c> and a running <c>H:MM:SS</c>
    /// timecode in the top-right corner while recording, <c>STBY</c> while the camera is raised but not
    /// recording, and the REC start/stop beeps. A 2000s consumer camcorder's viewfinder, kept plain on purpose
    /// (Story 1.12 AC4's rule: readable and recognisably camcorder is enough).
    ///
    /// ⚠️ IT ONLY READS THE RECORDER (decision 1b). <see cref="ClipRecorder.IsRecording"/> and
    /// <see cref="ClipRecorder.ElapsedSeconds"/> are read every frame; the beeps play from the recorder's
    /// <see cref="ClipRecorder.Started"/> / <see cref="ClipRecorder.Stopped"/> events, so EVERY stop beeps,
    /// including the length limit and an audio device change, through one code path, synchronously inside
    /// the call that started or stopped the clip (NFR2). It never calls anything on the recorder or on
    /// <see cref="PhotoModeController"/> (read for STBY only). UI → Recording is a read-only dependency,
    /// recorded in game-architecture.md §Architectural Boundaries.
    ///
    /// ⚠️ THE BEEPS STAY OUT OF THE FOOTAGE (decision 1f). Their AudioSource sets
    /// <c>bypassListenerEffects = true</c>, which skips the listener's filter chain and so the
    /// <see cref="ListenerTap"/>: a camcorder films the scene, not its own REC dot or its own beep. The
    /// rig measures that the beep is absent from the clip's PCM (and present at its own source). The source
    /// is deliberately NOT routed to a mixer group, so that exclusion is the one Story 1.14 measured.
    ///
    /// ⚠️ ITS OWN OVERLAY CANVAS, NOT A CHILD OF THE VIEWFINDER. PhotoViewfinder's CanvasScaler is Constant
    /// Pixel Size and its CanvasGroup fades in over 0.15 s, so a cluster inside it would be half-transparent
    /// on a record pressed right after raising, and would not scale with the screen. This canvas scales with
    /// the screen (1920×1080 reference, match 0.5, like GradeHudCanvas) and shows REC on the very frame
    /// recording starts. Overlay, so <c>cam.Render()</c> (the gallery thumbnail, a replay) never sees it.
    ///
    /// The cluster is built in code at Awake from the serialized layout fields below, so the scene holds one
    /// object and the layout is reviewable in one place.
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class RecordingHud : MonoBehaviour
    {
        [Header("Wiring")]
        [Tooltip("The recorder whose state this shows. Required: without it the HUD stays hidden and logs once.")]
        [SerializeField] private ClipRecorder recorder;

        [Tooltip("Optional. Read only, to show STBY while the camera is raised and not recording.")]
        [SerializeField] private PhotoModeController photoMode;

        [Tooltip("The recording tunables (the beep volume). Optional: the design default otherwise.")]
        [SerializeField] private RecordingConfig config;

        [Header("Beeps")]
        [Tooltip("2D AudioSource for the REC beeps: Spatial Blend 0, Play On Awake off, Bypass Listener Effects ON " +
                 "(keeps the beeps out of the recorded sound). Optional: without it the HUD is silent.")]
        [SerializeField] private AudioSource beepSource;

        [SerializeField] private AudioClip startBeep;
        [SerializeField] private AudioClip stopBeep;

        [Header("Look (reference pixels at 1920×1080)")]
        [Tooltip("Optional. Falls back to Unity's built-in LegacyRuntime font, as GradeHud does.")]
        [SerializeField] private Font font;

        [SerializeField] private int recFontSize = 44;
        [SerializeField] private int timecodeFontSize = 36;
        [SerializeField] private Vector2 margin = new Vector2(64f, 48f);
        [SerializeField] private Color recColor = new Color(1f, 0.16f, 0.12f, 1f);
        [SerializeField] private Color timecodeColor = Color.white;
        [SerializeField] private Color standbyColor = new Color(1f, 1f, 1f, 0.85f);
        [SerializeField] private Color outlineColor = new Color(0f, 0f, 0f, 0.85f);
        [SerializeField] private float outlinePixels = 1.5f;

        private bool _ready;
        private bool _subscribed;
        private CanvasGroup _recGroup;
        private CanvasGroup _standbyGroup;
        private Text _timecodeLabel;
        private long _shownSecond = -1;

        /// <summary>True while the REC cluster is showing. Exposed for the verification rig.</summary>
        public bool IsShowingRec => _recGroup != null && _recGroup.alpha > 0f;

        /// <summary>True while STBY is showing.</summary>
        public bool IsShowingStandby => _standbyGroup != null && _standbyGroup.alpha > 0f;

        /// <summary>The timecode text on screen now ("" before the first recording).</summary>
        public string TimecodeText => _timecodeLabel != null ? _timecodeLabel.text : string.Empty;

        /// <summary>Screen-space rect of the REC cluster (for the rig's pixel counts). Zero before Awake.</summary>
        public RectTransform RecRect { get; private set; }

        private void Awake()
        {
            if (font == null) font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var canvas = GetComponent<Canvas>();
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                GameLog.Warn("UI", $"RecordingHud canvas '{name}' is {canvas.renderMode}, not Screen Space - Overlay: " +
                                   "the REC indicator could appear in camera renders (thumbnails, replays).");

            Build();

            _ready = recorder != null;
            if (!_ready)
                GameLog.Error("UI", "RecordingHud has no ClipRecorder: the REC indicator and the beeps stay off.", this);

            if (beepSource != null && !beepSource.bypassListenerEffects)
                GameLog.Warn("UI", "The REC beep AudioSource does not bypass listener effects, so the beeps will be " +
                                   "recorded into every clip's sound.");
        }

        private void OnEnable()
        {
            if (recorder == null || _subscribed) return;
            recorder.Started += OnStarted;
            recorder.Stopped += OnStopped;
            _subscribed = true;
        }

        private void OnDisable()
        {
            if (recorder != null && _subscribed)
            {
                recorder.Started -= OnStarted;
                recorder.Stopped -= OnStopped;
            }
            _subscribed = false;
        }

        private void Update()
        {
            if (!_ready) return;
            bool rec = recorder.IsRecording;
            SetVisible(_recGroup, rec);
            // STBY means "ready to record": not shown by a camera that cannot record (no camera to film through, or
            // the recorder switched off), where it promised a press that does nothing (code review 2026-11-15).
            SetVisible(_standbyGroup, !rec && photoMode != null && photoMode.IsPhotoMode
                                      && recorder != null && recorder.CanRecord && recorder.isActiveAndEnabled);
            if (rec) ShowTimecode(recorder.ElapsedSeconds);
        }

        private void OnStarted()
        {
            // Shown at once, inside the press: the frame recording starts already carries REC, whatever order
            // this Update ran in relative to the press.
            _shownSecond = -1;
            ShowTimecode(0d);
            SetVisible(_recGroup, true);
            SetVisible(_standbyGroup, false);
            Beep(startBeep);
        }

        private void OnStopped(StopReason reason)
        {
            // Hidden at once, including for a stop the recorder decided in its LateUpdate.
            SetVisible(_recGroup, false);
            Beep(stopBeep);
        }

        private void Beep(AudioClip clip)
        {
            // isActiveAndEnabled: a stop during teardown (leaving play mode) must not make Unity log "Can not
            // play a disabled audio source".
            if (beepSource == null || clip == null || !beepSource.isActiveAndEnabled) return;
            beepSource.PlayOneShot(clip, RecordingConfig.BeepVolumeOf(config));
        }

        private void ShowTimecode(double seconds)
        {
            long whole = ClipTimecode.WholeSeconds(seconds);
            if (whole == _shownSecond || _timecodeLabel == null) return;   // a new string only when the second changes
            _shownSecond = whole;
            _timecodeLabel.text = ClipTimecode.Format(seconds);
        }

        private static void SetVisible(CanvasGroup g, bool on)
        {
            if (g == null) return;
            float a = on ? 1f : 0f;
            if (g.alpha != a) g.alpha = a;
        }

        // --- Construction (once, at Awake) -----------------------------------------------------------------

        private void Build()
        {
            RectTransform rec = NewRect("RecCluster", transform);
            rec.anchorMin = rec.anchorMax = rec.pivot = new Vector2(1f, 1f);   // top-right corner, pivot with it
            rec.anchoredPosition = new Vector2(-margin.x, -margin.y);
            rec.sizeDelta = new Vector2(360f, recFontSize + timecodeFontSize + 16f);
            _recGroup = NewGroup(rec);
            RecRect = rec;

            // "●" is U+25CF, which LegacyRuntime.ttf draws (checked on the rig's captures, like 1.11's stars).
            NewText("RecLabel", rec, "● REC", recFontSize, recColor, 0f, recFontSize + 6f);
            _timecodeLabel = NewText("Timecode", rec, string.Empty, timecodeFontSize, timecodeColor,
                                     -(recFontSize + 10f), timecodeFontSize + 6f);

            RectTransform stby = NewRect("Standby", transform);
            stby.anchorMin = stby.anchorMax = stby.pivot = new Vector2(1f, 1f);
            stby.anchoredPosition = new Vector2(-margin.x, -margin.y);
            stby.sizeDelta = new Vector2(360f, recFontSize + 6f);
            _standbyGroup = NewGroup(stby);
            NewText("StandbyLabel", stby, "STBY", recFontSize, standbyColor, 0f, recFontSize + 6f);
        }

        private static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        private static CanvasGroup NewGroup(RectTransform rt)
        {
            var g = rt.gameObject.AddComponent<CanvasGroup>();
            g.alpha = 0f;
            g.blocksRaycasts = false;
            g.interactable = false;
            return g;
        }

        private Text NewText(string name, RectTransform parent, string text, int size, Color color, float y, float height)
        {
            RectTransform rt = NewRect(name, parent);
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(0f, height);

            var t = rt.gameObject.AddComponent<Text>();
            t.font = font;
            t.fontSize = size;
            t.fontStyle = FontStyle.Bold;
            t.alignment = TextAnchor.UpperRight;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.color = color;
            t.raycastTarget = false;
            t.text = text;

            // The same legibility device as the grade readout (Story 1.19): a thin dark outline, so the red and the
            // white both read over bright sky and dark buildings alike.
            var o = rt.gameObject.AddComponent<Outline>();
            o.effectColor = outlineColor;
            o.effectDistance = new Vector2(outlinePixels, -outlinePixels);
            o.useGraphicAlpha = true;
            return t;
        }
    }
}
