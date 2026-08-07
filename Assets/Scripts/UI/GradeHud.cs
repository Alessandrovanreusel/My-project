using UnityEngine;
using UnityEngine.UI;
using CameraGame.Core;
using CameraGame.Events;
using CameraGame.Grading;
using CameraGame.PhotoMode;

namespace CameraGame.UI
{
    /// <summary>
    /// The player's grade readout (Story 1.12, FR4/NFR10). Subscribes to <c>ShotCapturedChannel</c> and,
    /// for every capture, puts the star rating, the percentage and the subject / composition / timing
    /// breakdown on screen for a moment, then fades it out.
    ///
    /// ⚠️ THIS SHIPS. It is not debug code and is not wrapped in <c>#if UNITY_EDITOR</c> — that is the whole
    /// reason <see cref="ShotGrade"/> carries the breakdown as plain fields (ShotGrade.cs:55-60). The
    /// editor overlay on <c>PhotoModeController.OnGUI</c> is a DIFFERENT readout that stays: it shows the
    /// grader's internals (the projected box, line-of-sight, height vs the gate) which the player
    /// deliberately never sees. The two are placed so they do not collide — that panel occupies the
    /// top-left, this one the bottom of the screen.
    ///
    /// ================================================================================================
    /// WHY THIS CANVAS IS SCREEN SPACE - OVERLAY (the decision that gates this story)
    /// ================================================================================================
    /// <c>GalleryService.HandleShotCaptured</c> calls <c>photoCamera.Render()</c> SYNCHRONOUSLY inside
    /// <c>Capture()</c>, on the same frame, driven by the same channel raise this class subscribes to.
    /// Subscriber order on that channel is registration order — component/scene order, i.e. not something
    /// any of us controls. So a HUD that is Screen Space - Camera and switches itself on in its own handler
    /// could be baked into the thumbnail the gallery stores, depending on which subscriber ran first.
    ///
    /// An OVERLAY canvas is composited after every camera and therefore CANNOT appear in <c>cam.Render()</c>
    /// output at all. The guarantee is structural rather than a matter of ordering, which is what AC5 needs.
    /// It also sidesteps the URP trap that cost Story 1.11 a real defect: a Screen Space - Camera canvas
    /// composites BEFORE tonemapping, so a translucent panel over the bright town came back as a readable
    /// forest (GalleryView.cs:86-99). This panel is translucent and is safe precisely because Overlay
    /// composites after post-processing.
    ///
    /// The price is that no rig can photograph this with <c>cam.Render()</c> either — the technique every
    /// previous story in this project verified with. <c>GradeHudShootRunner</c> uses
    /// <c>ScreenCapture.CaptureScreenshotAsTexture()</c> instead, and proves it with control shots before
    /// trusting a single HUD image.
    ///
    /// ⚠️ IT DELIBERATELY DOES NOT CALL <c>PhotoModeController.SetRaiseSuppressed</c>. That flag is a bool,
    /// not a refcount, and both its own doc-comment and deferred-work.md name this story as the second
    /// caller that would break it. A transient readout has no business freezing the camera anyway: the
    /// player must be able to keep shooting THROUGH it, and chasing a better grade immediately is the loop
    /// this readout exists to encourage. Nothing here writes to PhotoModeController — it only READS
    /// <see cref="PhotoModeController.IsPhotoMode"/>, which is UI → PhotoMode and already sanctioned
    /// (game-architecture.md §Architectural Boundaries).
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class GradeHud : MonoBehaviour
    {
        [Header("Wiring")]

        [Tooltip("The channel raised on every capture (Assets/Data/Channels/ShotCapturedChannel.asset). " +
                 "The HUD's only wire to the capture path — if unassigned, the readout never appears and " +
                 "capture, grading, flash, shutter and the gallery all keep working exactly as before.")]
        [SerializeField] private ShotCapturedChannel shotCapturedChannel;

        [Tooltip("Designer-facing HUD tunables (hold, fade, colours). Required: without it there are no " +
                 "authored timings, and a readout on screen for an unauthored length of time is worse than " +
                 "no readout — so the HUD stands down instead. Same call CaptureConfig makes for the flash.")]
        [SerializeField] private GradeHudConfig config;

        [Tooltip("The CanvasGroup faded after the hold. Optional: without one the readout still appears " +
                 "and disappears, it just does not fade. Falls back to a CanvasGroup on this object.")]
        [SerializeField] private CanvasGroup fadeGroup;

        [Tooltip("The controller whose photo mode this readout watches, so it can get out of the way when " +
                 "the camera comes down (see GradeHudConfig.hideOnCameraLowered). READ-ONLY — the HUD never " +
                 "calls anything on it. If unassigned, the readout simply runs its full hold.")]
        [SerializeField] private PhotoModeController photoMode;

        [Header("Labels")]

        [Tooltip("Line 1: the star rating and the overall percentage.")]
        [SerializeField] private Text ratingLabel;

        [Tooltip("Line 2: the three-axis breakdown — composition x timing, and how much of him was seen.")]
        [SerializeField] private Text axesLabel;

        [Tooltip("Line 3: the actionable 'why' — the miss reason in plain words, or how early/late the " +
                 "shutter was. Blank when there is nothing measured to say.")]
        [SerializeField] private Text whyLabel;

        [Tooltip("Font for the readout. Optional — falls back to Unity's built-in LegacyRuntime font, which " +
                 "is a built-in ENGINE resource and not a project asset, so this is not the Resources.Load " +
                 "the architecture rules out for game data (same call GalleryView makes).")]
        [SerializeField] private Font hudFont;

        // Resolved INDEPENDENTLY in Awake into their own flags, so one missing reference never disables the
        // others — the independence the 1.4 and 1.5 reviews both praised and PhotoModeController.cs:99-110
        // documents. _hudReady = can show anything at all; the labels are each optional on their own.
        private bool _hudReady;

        private Canvas _canvas;

        // Seconds of on-screen life left for the CURRENT capture, counted down on unscaled time. Above
        // SafeFadeSeconds the readout is at full; below it, that is also the fade's own progress.
        private float _remaining;

        private bool _visible;

        /// <summary>True while the readout is on screen. Read-only; exposed for the verification rig, which
        /// has to know whether it is photographing a HUD that is up, mid-fade, or already gone.</summary>
        public bool IsShowing => _visible;

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();

            // Each reference on its own line into its own flag. A missing channel must not be reported as a
            // missing config, and a missing label must disable only that line.
            bool haveChannel = shotCapturedChannel != null;
            if (!haveChannel)
                GameLog.Error("GradeHud",
                    "ShotCapturedChannel unassigned — the grade readout will never appear. Capture, " +
                    "grading, flash, shutter and the gallery are unaffected.", this);

            bool haveConfig = config != null;
            if (!haveConfig)
                GameLog.Error("GradeHud",
                    "GradeHudConfig unassigned — the grade readout is inert. Everything else about capture " +
                    "is unaffected.", this);
            else if (config.TryGetConfigProblem(out string problem))
                // Fail-soft must not mean invisible: these are values that let the HUD run while doing
                // something other than what was authored, which is far harder to notice than an outright
                // failure.
                GameLog.Warn("GradeHud", $"GradeHudConfig: {problem}");

            if (fadeGroup == null) fadeGroup = GetComponent<CanvasGroup>();
            if (fadeGroup == null)
                // Info, not Error: a HUD that appears and disappears without a fade is a supported, legible
                // state, not a broken one. Keeps the console clean (NFR5).
                GameLog.Info("GradeHud", "No CanvasGroup — the readout will show and hide without fading.");

            if (ratingLabel == null || axesLabel == null || whyLabel == null)
                GameLog.Error("GradeHud",
                    $"Missing label(s) — rating:{(ratingLabel != null ? "ok" : "MISSING")} " +
                    $"axes:{(axesLabel != null ? "ok" : "MISSING")} " +
                    $"why:{(whyLabel != null ? "ok" : "MISSING")}. The remaining lines still draw.", this);

            _hudReady = haveChannel && haveConfig
                        && (ratingLabel != null || axesLabel != null || whyLabel != null);

            ApplyFont();

            if (fadeGroup != null)
            {
                // The readout must never eat a click. It is feedback, not a control.
                fadeGroup.blocksRaycasts = false;
                fadeGroup.interactable = false;
            }

            Show(false);
        }

        // Subscribe/unsubscribe symmetrically (architecture §Communication Patterns). EventChannel<T> also
        // clears its subscribers on domain reload, but that is a backstop, not a substitute: a GradeHud that
        // is merely DISABLED must leave no live delegate on the channel asset, or a hidden HUD goes on
        // formatting strings once per shutter press for a canvas nobody can see.
        private void OnEnable()
        {
            if (shotCapturedChannel != null) shotCapturedChannel.Raised += HandleShotCaptured;
        }

        private void OnDisable()
        {
            if (shotCapturedChannel != null) shotCapturedChannel.Raised -= HandleShotCaptured;

            // Leave nothing on screen. Without this, disabling mid-hold and re-enabling later would bring
            // back a readout describing a capture from before — a stale verdict is worse than none.
            Show(false);
        }

        /// <summary>
        /// Runs synchronously inside <c>PhotoModeController.Capture()</c>, on the shutter frame.
        ///
        /// ⚠️ THE STRINGS ARE BUILT ONCE, HERE — never in <see cref="Update"/>. String interpolation in a
        /// per-frame path allocates every frame for a value that changes once per shutter press. Same shape
        /// as the capture flash (PhotoModeController.cs:237-243) and the viewfinder fade (:217-224): the
        /// event sets the state, Update only drives alpha.
        ///
        /// ⚠️ AND IT DOES NOT LOG. Grading already writes the full breakdown on this same frame
        /// (PhotoModeController.cs:391-394); a second line per shutter press is spam.
        /// </summary>
        private void HandleShotCaptured(ShotGrade grade)
        {
            if (!_hudReady) return;

            Format(grade);

            // Restart cleanly on every capture: a second shutter press while the readout is still up gets a
            // full hold again, not the remainder of the previous one. This project's bugs live almost
            // exclusively in reuse and in the second cycle onward.
            _remaining = config.SafeVisibleSeconds;
            Show(true);
        }

        /// <summary>
        /// The three shapes AC2 requires, and they are visibly different from each other. What separates
        /// them is not the stars — a MISS and an off-peak COUNTED shot both read 1★, which Story 1.10's
        /// review settled as designed — it is the breakdown and the reason.
        /// </summary>
        private void Format(ShotGrade grade)
        {
            // ⚠️ EVERY BRANCH WRITES EVERY LABEL. Leaving one untouched would let a line from the PREVIOUS
            // capture sit under the current one — a miss reason beneath a counted shot's percentage, which
            // is a readout that is wrong rather than merely incomplete.

            if (grade.IsPlaceholder)
            {
                // ⚠️ NOT "1★ · 0%". A placeholder is 0%, 1 star and GradeMiss.Unevaluated because nothing
                // was ever measured — rendering that as a rating tells the player they took a terrible
                // photograph when the game never graded one.
                SetText(ratingLabel, "NOT GRADED");
                SetText(axesLabel, NotMeasuredAxes);
                SetText(whyLabel, GradeText.MissLong(GradeMiss.Unevaluated));
                SetColor(config.SafePlaceholderColor);
                return;
            }

            if (grade.IsMiss)
            {
                // ⚠️ NO PERCENTAGES ON A MISS. Composition, timing and subject are all hard 0f because
                // grading early-outs at the failed gate BEFORE scoring them — printing "composition 0% x
                // timing 0%" asserts three measurements that never happened. Dashes, exactly as the editor
                // overlay was patched to do (PhotoModeController.cs:469-478).
                //
                // The overall percentage is dropped too: it is genuinely 0, but "0%" beside "1★" is the one
                // thing a counted-but-late shot also shows, and telling those two apart at a glance IS the
                // acceptance criterion.
                SetText(ratingLabel, $"{GradeText.Stars(grade.Stars)}   MISSED");
                SetText(axesLabel, NotMeasuredAxes);
                SetText(whyLabel, GradeText.MissLong(grade.MissReason));
                SetColor(config.SafeMissColor);
                return;
            }

            // A COUNTED shot — including one that scores a hard 0%, which is a real and common state and is
            // NOT a miss (an off-peak shot: timing is the first pillar, ruled as designed on 2026-07-28; all
            // 24 shots in the town placement study read "counted — 0% 1★"). The breakdown is what makes it
            // different from a miss: "composition 92% x timing 0%" says "you were late", which is exactly
            // the "why" NFR10 asks for.
            SetText(ratingLabel, $"{GradeText.Stars(grade.Stars)}   {grade.Percent01:P0}");

            // ⚠️ "seen", NOT "how big he was". Subject01 is the fraction of line-of-sight samples that were
            // clear — a REPORT, not a multiplier; the subject check is a pass/fail gate (ShotGrade.cs:78-84).
            // The size measurement (HeightFraction) lives only in the editor-only GradeDetail and is not
            // available here at all.
            SetText(axesLabel,
                $"composition {grade.Composition01:P0}   ×   timing {grade.Timing01:P0}" +
                $"      ·      seen {grade.Subject01:P0}");

            // Empty when the subject reported no usable timing — the line simply says nothing rather than
            // inventing "0.0s late". GradeText.TimingAdvice guards that.
            SetText(whyLabel, GradeText.TimingAdvice(grade));
            SetColor(config.SafeCountedColor);
        }

        /// <summary>The axis line for a shot whose axes were never scored. Dashes, not zeroes — "the
        /// overlay must not state more than it knows" (PhotoModeController.cs:469-478).</summary>
        private const string NotMeasuredAxes = "composition —   ×   timing —      ·      seen —";

        /// <summary>
        /// Gives every label a font, once, at Awake.
        ///
        /// ⚠️ A <c>Text</c> WITH NO FONT DRAWS NOTHING AT ALL, with a clean console — the silent-nothing
        /// shape this project has been caught by six times, and the one that would be hardest to spot here
        /// because <c>Text.text</c> would still read back perfectly correct in any structural check. Only a
        /// photograph of the screen can tell the difference, which is exactly why this story's evidence is
        /// screenshots rather than a readout of the labels.
        ///
        /// Only fills in what is missing, so a font deliberately assigned in the scene wins.
        /// </summary>
        private void ApplyFont()
        {
            Font font = hudFont != null
                ? hudFont
                : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            if (font == null)
            {
                GameLog.Warn("GradeHud",
                    "No font available for the grade readout — the panel will draw with no text on it.");
                return;
            }

            if (ratingLabel != null && ratingLabel.font == null) ratingLabel.font = font;
            if (axesLabel != null && axesLabel.font == null) axesLabel.font = font;
            if (whyLabel != null && whyLabel.font == null) whyLabel.font = font;
        }

        private static void SetText(Text label, string value)
        {
            if (label != null) label.text = value;
        }

        private void SetColor(Color c)
        {
            if (ratingLabel != null) ratingLabel.color = c;
            if (axesLabel != null) axesLabel.color = c;
            if (whyLabel != null) whyLabel.color = c;
        }

        private void Update()
        {
            if (!_visible) return;

            // Get out of the way when the camera comes down, so a lingering Overlay readout cannot sit on
            // top of the gallery — which is Screen Space - Camera and therefore draws UNDERNEATH this
            // whatever the sorting orders say. Reads IsPhotoMode and nothing else; see the class header for
            // why this HUD never writes to PhotoModeController.
            if (config.hideOnCameraLowered && photoMode != null && !photoMode.IsPhotoMode)
            {
                Show(false);
                return;
            }

            // ⚠️ UNSCALED, matching the capture flash (PhotoModeController.cs:241). Capture feedback that
            // freezes with timeScale would be wrong the day a pause menu lands, and a readout stuck at full
            // alpha over a paused game is worse than one that finishes fading.
            _remaining -= Time.unscaledDeltaTime;

            if (_remaining <= 0f)
            {
                Show(false);
                return;
            }

            if (fadeGroup == null) return;

            // _remaining starts at hold + fade, so it is above SafeFadeSeconds for the whole hold (clamped
            // to 1) and then IS the fade's own progress for the last stretch. One expression, no state.
            float fade = config.SafeFadeSeconds;
            fadeGroup.alpha = fade > 0f ? Mathf.Clamp01(_remaining / fade) : 1f;
        }

        /// <summary>
        /// Shows or hides the readout.
        ///
        /// ⚠️ <c>Canvas.enabled</c> IS DRIVEN AS WELL AS THE ALPHA, for the same reason
        /// <c>GalleryView.ApplyOpenState</c> does it (GalleryView.cs:466-483): an alpha-0 canvas is still
        /// geometry submitted every frame — including the frame a photograph is taken on. Disabling the
        /// Canvas stops it rendering outright while leaving the hierarchy intact.
        /// </summary>
        private void Show(bool visible)
        {
            _visible = visible;

            if (_canvas != null) _canvas.enabled = visible;
            if (fadeGroup != null) fadeGroup.alpha = visible ? 1f : 0f;
            if (!visible) _remaining = 0f;
        }
    }
}
