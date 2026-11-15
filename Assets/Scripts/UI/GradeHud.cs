using UnityEngine;
using UnityEngine.UI;
using CameraGame.Core;
using CameraGame.Events;
using CameraGame.Grading;
using CameraGame.PhotoMode;
using CameraGame.Recording;

namespace CameraGame.UI
{
    /// <summary>
    /// The player's grade readout (Story 1.12, FR4/NFR10). Subscribes to <c>ClipRecordedChannel</c> (Story
    /// 1.16: every clip the player records) and to <c>ShotCapturedChannel</c> (every still, which since Story
    /// 1.15 only the regression rigs take) and, for each, puts the star rating, the percentage and the
    /// breakdown on screen for a moment, then fades it out. A clip shows framing / peak coverage / in-shot; a
    /// still shows composition / timing / seen.
    ///
    /// ================================================================================================
    /// WHEN EACH READOUT GETS OUT OF THE WAY (Story 1.16, decision 1g)
    /// ================================================================================================
    /// The two readouts yield to different things, on purpose:
    ///   - a STILL's readout hides the moment the camera comes down, when
    ///     <see cref="GradeHudConfig.hideOnCameraLowered"/> is on (unchanged since Story 1.12);
    ///   - a CLIP's readout ignores the camera's mode and yields only to a screen owner: it hides the moment
    ///     <see cref="PhotoModeController.RaiseSuppressed"/> turns on (the gallery). Lowering the camera is the
    ///     most natural way to END a clip, and by the time the clip is announced the camera is already down
    ///     (PhotoModeController.SetMode stops the recording AFTER Mode changes), so the still's rule would put
    ///     every such clip's grade on screen and take it off in the same frame.
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
    /// <see cref="PhotoModeController.IsPhotoMode"/> and <see cref="PhotoModeController.RaiseSuppressed"/>, which
    /// is UI → PhotoMode and already sanctioned (game-architecture.md §Architectural Boundaries).
    /// </summary>
    [RequireComponent(typeof(Canvas))]
    public class GradeHud : MonoBehaviour
    {
        [Header("Wiring")]

        [Tooltip("The channel raised on every capture (Assets/Data/Channels/ShotCapturedChannel.asset). " +
                 "The HUD's only wire to the capture path — if unassigned, the readout never appears and " +
                 "capture, grading, flash, shutter and the gallery all keep working exactly as before.")]
        [SerializeField] private ShotCapturedChannel shotCapturedChannel;

        [Tooltip("The channel raised once per finished clip (Assets/Data/Channels/ClipRecordedChannel.asset), " +
                 "carrying the clip's grade (Story 1.16). The readout for the player's whole verb: if unassigned, " +
                 "recording keeps working and no clip ever shows its grade. The HUD only READS the clip; it never " +
                 "claims it (it keeps no footage).")]
        [SerializeField] private ClipRecordedChannel clipRecordedChannel;

        [Tooltip("Designer-facing HUD tunables (hold, fade, colours). Required: without it there are no " +
                 "authored timings, and a readout on screen for an unauthored length of time is worse than " +
                 "no readout — so the HUD stands down instead. Same call CaptureConfig makes for the flash.")]
        [SerializeField] private GradeHudConfig config;

        [Tooltip("The CanvasGroup faded after the hold. Optional: without one the readout still appears " +
                 "and disappears, it just does not fade. Falls back to a CanvasGroup on this object.")]
        [SerializeField] private CanvasGroup fadeGroup;

        [Tooltip("The controller whose photo mode this readout watches, so it can get out of the way: a still's " +
                 "readout when the camera comes down (GradeHudConfig.hideOnCameraLowered), a clip's when the gallery " +
                 "takes the screen (RaiseSuppressed). READ-ONLY — the HUD never calls anything on it. If unassigned, " +
                 "the readout simply runs its full hold.")]
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

        // True while the readout on screen describes a CLIP, which yields to a screen owner rather than to the
        // camera coming down (see the class header).
        private bool _showingClip;

        /// <summary>True while the readout is on screen and describes a clip (false for a still). Read-only, for
        /// the verification rig.</summary>
        public bool IsShowingClip => _visible && _showingClip;

        /// <summary>True while the readout is on screen. Read-only; exposed for the verification rig, which
        /// has to know whether it is photographing a HUD that is up, mid-fade, or already gone.</summary>
        public bool IsShowing => _visible;

        private void Awake()
        {
            _canvas = GetComponent<Canvas>();

            // ⚠️ THE ONE PROPERTY THIS WHOLE STORY RESTS ON, AND IT USED TO BE THE ONLY ONE NOT CHECKED.
            //
            // [RequireComponent(typeof(Canvas))] guarantees a Canvas EXISTS. It guarantees nothing about its
            // render mode — and the class header above explains at length that Overlay is what makes "the
            // HUD can never reach a stored photograph" structural rather than a matter of subscriber order.
            // Flip this canvas to Screen Space - Camera in the Inspector (chasing a sorting-order problem, a
            // prefab merge, a new scene authored from memory) and the grade text bakes into every thumbnail
            // GalleryService stores, with a completely clean console. All three layers of the 2026-08-07
            // code review flagged this independently, including the one with no project access.
            //
            // Repaired as well as reported, exactly as the sibling class does it (GalleryView.cs:230 sets
            // its own render mode rather than trusting the scene): the acceptance criterion is not
            // negotiable, so the code owns the property rather than the scene asset. The Error is what stops
            // the repair being silent — "fail-soft must not mean invisible".
            if (_canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                GameLog.Error("GradeHud",
                    $"Canvas render mode is {_canvas.renderMode}, but the grade readout MUST be " +
                    "Screen Space - Overlay: any other mode can be captured by photoCamera.Render() and " +
                    "baked into the stored photograph. Forcing it back to Overlay — fix the scene asset.",
                    this);
                _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            }

            // Each reference on its own line into its own flag. A missing channel must not be reported as a
            // missing config, and a missing label must disable only that line.
            bool haveShotChannel = shotCapturedChannel != null;
            if (!haveShotChannel)
                GameLog.Error("GradeHud",
                    "ShotCapturedChannel unassigned — a still's grade readout will never appear. Capture, " +
                    "grading, flash, shutter and the gallery are unaffected.", this);

            // An ERROR, like the shot channel: without it the HUD is blind to the player's whole verb (Story 1.16).
            bool haveClipChannel = clipRecordedChannel != null;
            if (!haveClipChannel)
                GameLog.Error("GradeHud",
                    "ClipRecordedChannel unassigned — no clip will ever show its grade. Recording is unaffected.", this);

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

            // photoMode is genuinely optional, but a null here SILENTLY DROPS A GUARD rather than merely
            // doing less: the hide-on-lowered test short-circuits on `photoMode != null`, so the readout
            // runs its full hold over the gallery grid — the exact thing hideOnCameraLowered exists to
            // prevent — while the switch still reads ON in the Inspector. Every sibling reference in this
            // class fails loudly; this one used to fail with no line at all. Warn rather than Error: the
            // readout itself still works perfectly.
            if (photoMode == null && haveConfig && config.hideOnCameraLowered)
                GameLog.Warn("GradeHud",
                    "No PhotoModeController assigned while hideOnCameraLowered is ON — the guards cannot " +
                    "fire, so a readout (a still's or a clip's) will run its full hold and can sit over the gallery.");

            if (ratingLabel == null || axesLabel == null || whyLabel == null)
                GameLog.Error("GradeHud",
                    $"Missing label(s) — rating:{(ratingLabel != null ? "ok" : "MISSING")} " +
                    $"axes:{(axesLabel != null ? "ok" : "MISSING")} " +
                    $"why:{(whyLabel != null ? "ok" : "MISSING")}. The remaining lines still draw.", this);

            // Either channel is enough: each missing one disables only its own readout.
            _hudReady = (haveShotChannel || haveClipChannel) && haveConfig
                        && (ratingLabel != null || axesLabel != null || whyLabel != null);

            ApplyFont();
            ApplyOutline();

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
        //
        // ⚠️ THE CHANNEL SUBSCRIBED TO IS CACHED, NOT RE-READ FROM THE FIELD. Both halves used to
        // dereference the CURRENT field value, so reassigning the channel in the Inspector during play —
        // which is how this project tunes and verifies — unsubscribed from the NEW asset (a no-op) and left
        // the OLD one holding a live delegate to a disabled GradeHud and its whole GameObject. The symptom
        // is the one this comment already warned about, plus a Canvas switching itself back on that the
        // developer believes is off.
        private ShotCapturedChannel _subscribedChannel;
        private ClipRecordedChannel _subscribedClipChannel;   // the same caching, for the same reason (Story 1.16)

        private void OnEnable()
        {
            _subscribedChannel = shotCapturedChannel;
            if (_subscribedChannel != null) _subscribedChannel.Raised += HandleShotCaptured;
            _subscribedClipChannel = clipRecordedChannel;
            if (_subscribedClipChannel != null) _subscribedClipChannel.Raised += HandleClipRecorded;
        }

        private void OnDisable()
        {
            if (_subscribedChannel != null) _subscribedChannel.Raised -= HandleShotCaptured;
            _subscribedChannel = null;
            if (_subscribedClipChannel != null) _subscribedClipChannel.Raised -= HandleClipRecorded;
            _subscribedClipChannel = null;

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
            _showingClip = false;
            Show(true);
        }

        /// <summary>
        /// Runs synchronously inside <c>ClipRecorder.Stop</c>, once per finished clip (Story 1.16), whatever stopped
        /// it: Record pressed again, the camera lowered, the length limit, the audio device changing.
        ///
        /// ⚠️ IT DOES NOT CLAIM THE CLIP. The HUD keeps no footage, and a claim here would take ownership of
        /// megabytes it never disposes (RecordedClip's ownership rule). It reads the grade and lets go.
        /// Strings are built here, once per clip, never in <see cref="Update"/>; and it does not log, because the
        /// recorder already wrote the grade's breakdown on this frame.
        /// </summary>
        private void HandleClipRecorded(RecordedClip clip)
        {
            if (!_hudReady || clip == null) return;

            // A clip stopped BY a screen owner (the gallery suppresses the raise, which lowers the camera, which
            // stops the clip) is announced with the gallery already owning the screen: show nothing, and take
            // down whatever was up, rather than flash a readout over it for a frame.
            if (ClipYields())
            {
                Show(false);
                return;
            }

            Format(clip.Grade);
            _remaining = config.SafeVisibleSeconds;
            _showingClip = true;
            Show(true);
        }

        /// <summary>A clip's readout yields to a screen owner only (see the class header).</summary>
        private bool ClipYields() => photoMode != null && photoMode.RaiseSuppressed;

        /// <summary>
        /// A clip's readout: the same four states, the same every-branch-writes-every-label rule and the same
        /// single colour rule as a still's (<see cref="Format(ShotGrade)"/>), with a clip's breakdown and advice.
        /// </summary>
        private void Format(ClipGrade grade)
        {
            Color colour = config.ColorFor(grade.Counted, grade.IsMiss, grade.Stars, grade.Percent01);

            if (!grade.Counted && !grade.IsMiss)
            {
                // Not graded (no grading config, or the grader failed), and the zeroed default(ClipGrade): nothing
                // was measured, so no rating either.
                SetText(ratingLabel, "NOT GRADED");
                SetText(axesLabel, GradeText.ClipAxesNotMeasured);
                SetText(whyLabel, GradeText.ClipMissLong(GradeMiss.Unevaluated));
                SetColor(colour);
                return;
            }

            if (grade.IsMiss)
            {
                // Dashes, never zeroes: a miss measured no framing and no coverage.
                SetText(ratingLabel, $"{GradeText.Stars(grade.Stars)}   MISSED");
                SetText(axesLabel, GradeText.ClipAxesNotMeasured);
                SetText(whyLabel, GradeText.ClipMissLong(grade.MissReason));
                SetColor(colour);
                return;
            }

            SetText(ratingLabel, $"{GradeText.Stars(grade.Stars)}   {GradeText.Percent(grade.Percent01)}");
            SetText(axesLabel, GradeText.ClipAxes(grade));
            SetText(whyLabel, GradeText.PeakAdvice(grade));
            SetColor(colour);
        }

        /// <summary>
        /// The three shapes AC2 requires, and they are visibly different from each other. What separates
        /// them is not the stars — a MISS and an off-peak COUNTED shot both read 1★, which Story 1.10's
        /// review settled as designed — it is the breakdown and the reason.
        ///
        /// ⚠️ EVERY COLOUR COMES FROM ONE RULE, <see cref="GradeHudConfig.ColorFor(ShotGrade)"/>, AND EVERY
        /// PERCENTAGE FROM ONE FORMATTER, <see cref="GradeText.Percent"/> (Story 1.19). Colour says whether the
        /// shot EARNED anything — a miss and a counted shot printing "0 %" share one red on purpose — and the
        /// words say which it was. Because the red is decided from the printed text, the readout cannot
        /// show "1 %" in red or "0 %" in amber.
        /// </summary>
        private void Format(ShotGrade grade)
        {
            // ⚠️ EVERY BRANCH WRITES EVERY LABEL. Leaving one untouched would let a line from the PREVIOUS
            // capture sit under the current one — a miss reason beneath a counted shot's percentage, which
            // is a readout that is wrong rather than merely incomplete.

            // ⚠️ FOUR STATES, NOT THREE — AND THE FOURTH USED TO LAND IN THE WRONG BRANCH.
            //
            // Counted / miss / placeholder do NOT partition ShotGrade. A grade whose MissReason is
            // GradeMiss.Unevaluated but which is not a placeholder belongs to none of them: Unevaluated is
            // deliberately the enum's ZERO value (ShotGrader.cs:10-15) and IsMiss excludes it, so
            // default(ShotGrade) — from new ShotGrade[n], an unassigned struct field, a failed TryGetValue —
            // is neither Counted nor IsMiss nor IsPlaceholder. It fell through both guards below into the
            // COUNTED branch and printed "composition 0% × timing 0% · seen 0%" in the counted colour: the
            // exact all-zero Counted shape ShotGrade.FromPercent was deleted for in this same story, on the
            // one readout the player sees. Found by the 2026-08-07 code review.
            //
            // No production path raises it today (PhotoModeController.cs:379 raises only Placeholder,
            // Scored, or Missed with a real reason) — this branch is what keeps it that way, and it costs
            // one comparison. Branch on Counted, which is POSITIVE, rather than on two negatives that a
            // fourth state can slip between.
            if (!grade.Counted && !grade.IsMiss)
            {
                // ⚠️ NOT "1★ · 0%". A placeholder is 0%, 1 star and GradeMiss.Unevaluated because nothing
                // was ever measured — rendering that as a rating tells the player they took a terrible
                // photograph when the game never graded one. Identical wording for the zeroed fourth state,
                // because it means the same thing: nobody graded this.
                SetText(ratingLabel, "NOT GRADED");
                SetText(axesLabel, NotMeasuredAxes);
                SetText(whyLabel, GradeText.MissLong(GradeMiss.Unevaluated));
                SetColor(config.ColorFor(grade));
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
                // thing a counted-but-late shot also shows. Since Story 1.19 the two share a colour on
                // purpose (both earned nothing), so these WORDS — MISSED and dashes, against a breakdown and
                // the timing advice — are what tell a miss from a 0 % shot.
                SetText(ratingLabel, $"{GradeText.Stars(grade.Stars)}   MISSED");
                SetText(axesLabel, NotMeasuredAxes);
                SetText(whyLabel, GradeText.MissLong(grade.MissReason));
                SetColor(config.ColorFor(grade));
                return;
            }

            // A COUNTED shot — including one that scores a hard 0%, which is a real and common state and is
            // NOT a miss (an off-peak shot: timing is the first pillar, ruled as designed on 2026-07-28; all
            // 24 shots in the town placement study read "counted — 0% 1★"). The breakdown is what makes it
            // different from a miss: "composition 92% x timing 0%" says "you were late", which is exactly
            // the "why" NFR10 asks for.
            SetText(ratingLabel, $"{GradeText.Stars(grade.Stars)}   {GradeText.Percent(grade.Percent01)}");

            // ⚠️ "seen", NOT "how big he was". Subject01 is the fraction of line-of-sight samples that were
            // clear — a REPORT, not a multiplier; the subject check is a pass/fail gate (ShotGrade.cs:78-84).
            // The size measurement (HeightFraction) lives only in the editor-only GradeDetail and is not
            // available here at all.
            //
            // Through GradeText.Percent like the rating, and TimingAdvice's seconds are pinned the same way, so
            // the whole readout prints in one culture: the player's locale used to decide it, fr-FR printed
            // U+066A for the percent sign and "2,4s late" for the advice (Story 1.19 and its code review).
            SetText(axesLabel,
                $"composition {GradeText.Percent(grade.Composition01)}   ×   timing {GradeText.Percent(grade.Timing01)}" +
                $"      ·      seen {GradeText.Percent(grade.Subject01)}");

            // Empty when the subject reported no usable timing — the line simply says nothing rather than
            // inventing "0.0s late". GradeText.TimingAdvice guards that.
            SetText(whyLabel, GradeText.TimingAdvice(grade));

            // ⚠️ BANDED BY GRADE, NOT ONE COLOUR FOR EVERY COUNTED SHOT. Alexv's AC3 check (2026-09-11)
            // found the miss "obvious" and the 5★-versus-0% pair "not obvious which one is which" — because
            // the miss changed colour and the two counted shots did not. See GradeHudConfig.CountedColorFor.
            // And a counted shot that PRINTS "0 %" earned nothing, so it reads in the miss's red, while one
            // printing "1 %" keeps its band (Story 1.19). ColorFor decides both from the printed text.
            SetColor(config.ColorFor(grade));
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

        /// <summary>
        /// Gives every label a thin dark outline, once, at Awake (Story 1.19).
        ///
        /// ⚠️ WHY CODE AND NOT THE SCENE: the same call <see cref="Awake"/> makes for the render mode — the
        /// requirement is not negotiable (on bright ground no readout red reaches 4.5:1 against the
        /// translucent panel alone; the outline is what keeps it readable), so the code owns it rather than
        /// trusting each label in a 17 MB scene asset to carry the component. It also covers labels built in
        /// code. The cost is that the outline shows in Play mode only, not in the Scene view.
        ///
        /// Colour and thickness come from <see cref="GradeHudConfig"/> through its Safe* accessors, so a 0,
        /// NaN or alpha-0 value is repaired (and reported at Awake), never applied: uGUI's
        /// <c>Shadow.effectDistance</c> setter lets NaN straight through. A label that is missing disables
        /// only its own outline, exactly as it disables only its own line.
        ///
        /// uGUI's built-in <see cref="Outline"/>: four copies of the text mesh at (±d, ±d), drawn behind it.
        /// It fades with the text because the CanvasGroup's alpha applies to the whole canvas renderer,
        /// outline copies included; <c>useGraphicAlpha</c> is a different thing — it multiplies the copies by
        /// the TEXT COLOUR's own alpha, so a translucent readout colour gets an equally translucent outline.
        /// The mesh is rebuilt when the text changes — once per shutter press — never per frame.
        ///
        /// ⚠️ APPLIED ONCE, HERE — NOT LIVE. Unlike the colours and the hold, which are read on every shutter
        /// press, editing outlineColor or outlineDistance during Play mode changes nothing until the next
        /// Play. Deliberate for now: the Story 1.19 candidate sweep sets each candidate thickness on these
        /// Outline components and relies on a shutter press not overwriting it (deferred-work.md).
        /// </summary>
        private void ApplyOutline()
        {
            if (config == null) return;

            Color colour = config.SafeOutlineColor;
            float d = config.SafeOutlineDistance;

            OutlineLabel(ratingLabel, colour, d);
            OutlineLabel(axesLabel, colour, d);
            OutlineLabel(whyLabel, colour, d);
        }

        private static void OutlineLabel(Text label, Color colour, float distance)
        {
            if (label == null) return;

            // TryGetComponent, not GetComponent + `??`: in the editor GetComponent returns a fake-null
            // object for a missing component, which `??` treats as present.
            if (!label.TryGetComponent(out Outline outline)) outline = label.gameObject.AddComponent<Outline>();

            // ⚠️ SWITCHED ON, NOT ONLY CONFIGURED. An Outline the label already carries is reused, and one
            // that was unticked in the Inspector (or arrives disabled on a cloned HUD) used to stay
            // disabled: Outline.ModifyMesh returns at `!IsActive()`, so that line drew with NO outline and
            // a clean console while its colour and thickness read back perfectly. The 2026-11-15 code review
            // reproduced it: 1,324 outline pixels on a fresh label, 0 on one that carried a disabled Outline.
            outline.enabled = true;
            outline.effectColor = colour;
            outline.effectDistance = new Vector2(distance, -distance);
            outline.useGraphicAlpha = true;
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
            //
            // A CLIP's readout yields to a screen owner instead (Story 1.16; see the class header): a clip ended by
            // lowering the camera must still show its grade.
            bool yield = _showingClip
                ? ClipYields()
                : config.hideOnCameraLowered && photoMode != null && !photoMode.IsPhotoMode;
            if (yield)
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
