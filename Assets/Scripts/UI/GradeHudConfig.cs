using UnityEngine;

namespace CameraGame.UI
{
    /// <summary>
    /// Designer-facing tunables for the grade-feedback HUD (Story 1.12): how long the readout stays up, how
    /// long it takes to fade, whether it goes away when the camera comes down, and the three text colours.
    /// Lives as a ScriptableObject asset (Assets/Data/UI/GradeHudConfig.asset) assigned to
    /// <see cref="GradeHud"/> in the Inspector, so the feel re-tunes WITHOUT a recompile
    /// (architecture §Configuration). Mirrors <c>CaptureConfig</c> for shape and <c>GalleryConfig</c> for
    /// the validation idiom — no HUD magic numbers live in code.
    ///
    /// ⚠️ EVERY VALUE IS READ THROUGH ITS <c>Safe*</c> ACCESSOR, NEVER RAW. <c>[Range]</c>/<c>[Min]</c> and
    /// <c>OnValidate</c> are BOTH editor-only, and this project hand-authors these assets as YAML (Unity MCP
    /// cannot create custom ScriptableObject assets), which passes through neither. A zero typed into the
    /// .asset therefore reaches the HUD intact — and a zero hold is a readout that flickers for one frame
    /// and is never read, with a perfectly clean console. That is this project's single most repeated
    /// failure mode: <c>cueRadius = 0</c> gave total silence (1.8), <c>minCoverage = NaN</c> and
    /// <c>minVisibleSamples = 0</c> each disabled a gate invisibly (1.9), <c>timingFullSeconds = 0</c>
    /// likewise (1.10), <c>cellSize.x = 0</c> drew a gallery with no photographs in it (1.11).
    ///
    /// ⚠️ AND THERE IS DELIBERATELY NO <c>OnValidate</c> HERE — see the note at the bottom of the file.
    /// </summary>
    [CreateAssetMenu(menuName = "CameraGame/UI/Grade HUD Config", fileName = "GradeHudConfig")]
    public class GradeHudConfig : ScriptableObject
    {
        [Header("Timing")]

        [Tooltip("Seconds the readout stays at FULL opacity after a capture, before it begins to fade. " +
                 "This is the number AC3 lives or dies on — it is how long a player has to read the grade. " +
                 "The fade below is on top of this, not part of it.")]
        [Range(MinHoldSeconds, MaxHoldSeconds)] public float holdSeconds = DefaultHoldSeconds;

        [Tooltip("Seconds the readout takes to fade from full to invisible, AFTER the hold. 0 is allowed " +
                 "and means it simply disappears.")]
        [Range(MinFadeSeconds, MaxFadeSeconds)] public float fadeSeconds = DefaultFadeSeconds;

        [Tooltip("Hide the readout the moment the camera is lowered.\n\n" +
                 "ON (default) is what stops a lingering HUD sitting on top of the gallery: the player can " +
                 "capture, release the raise and press Tab well inside the hold, and this HUD is a Screen " +
                 "Space - Overlay canvas, which draws OVER the gallery's Screen Space - Camera one.\n\n" +
                 "The cost is that RaiseCamera is a HOLD, so releasing the button takes the grade off " +
                 "screen immediately — a player who clicks and lets go may never read it. Turn this OFF to " +
                 "let the readout finish its hold after the camera comes down. This is a feel decision and " +
                 "is deliberately a switch rather than a code choice.")]
        public bool hideOnCameraLowered = true;

        [Header("Colours")]

        [Tooltip("Text colour for a COUNTED shot — one that passed every gate and carries a real score.")]
        public Color countedColor = new Color(0.94f, 0.94f, 0.90f);

        [Tooltip("Text colour for a MISSED shot. Distinct from the counted colour on purpose: a miss and " +
                 "an off-peak counted shot both read 1 star, so colour is one of the few things that can " +
                 "separate them at a glance.")]
        public Color missColor = new Color(1f, 0.55f, 0.45f);

        [Tooltip("Text colour for a shot that was never graded at all (grading unconfigured). Muted, " +
                 "because this is not a verdict on the photograph — it is the game saying it did not score " +
                 "one.")]
        public Color placeholderColor = new Color(0.62f, 0.62f, 0.66f);

        // --- Design defaults and clamp bounds ---------------------------------------------------------
        //
        // Named constants referenced by BOTH the field initializer and the accessor. Story 1.10 shipped
        // accessors whose fallback duplicated the initializer as a bare number in a second place, so
        // retuning a field left the corrupt-asset fallback silently restoring the OLD value (review
        // 2026-07-28). One constant, referenced twice, cannot drift.

        private const float DefaultHoldSeconds = 2.2f;
        private const float DefaultFadeSeconds = 0.6f;

        /// <summary>Shortest hold worth authoring. Below about this the readout is a flicker rather than
        /// something a person reads, which is a feature that appears to work and does not.</summary>
        public const float MinHoldSeconds = 0.3f;

        /// <summary>Longest hold. Not arbitrary: the readout describes ONE capture, and a hold measured in
        /// tens of seconds is still describing a shot the player took several shots ago.</summary>
        public const float MaxHoldSeconds = 10f;

        public const float MinFadeSeconds = 0f;
        public const float MaxFadeSeconds = 5f;

        /// <summary>Total on-screen time (hold + fade) above which the readout is warned about as
        /// long-lived. Purely an authoring guard-rail — it warns, it does not clamp, because a deliberately
        /// slow readout is a legitimate designer choice.</summary>
        public const float LingerWarnSeconds = 6f;

        /// <summary>Below this alpha a text colour is invisible on screen while reading as a perfectly
        /// valid colour in the Inspector — the silent-nothing shape, in a channel nobody thinks to check.</summary>
        public const float MinVisibleAlpha = 0.05f;

        // --- Safe accessors ---------------------------------------------------------------------------

        /// <summary>Hold, guaranteed usable however the asset was authored.</summary>
        public float SafeHoldSeconds => ClampFinite(holdSeconds, MinHoldSeconds, MaxHoldSeconds,
                                                    DefaultHoldSeconds);

        /// <summary>Fade, guaranteed usable however the asset was authored.</summary>
        public float SafeFadeSeconds => ClampFinite(fadeSeconds, MinFadeSeconds, MaxFadeSeconds,
                                                    DefaultFadeSeconds);

        /// <summary>Total seconds the readout is on screen for one capture.</summary>
        public float SafeVisibleSeconds => SafeHoldSeconds + SafeFadeSeconds;

        /// <summary>The colour for a given grade shape, guaranteed visible. An alpha authored at (or near)
        /// zero is forced back to opaque rather than left as an invisible readout — and
        /// <see cref="TryGetConfigProblem"/> says so, so the repair is never silent.</summary>
        public Color SafeCountedColor => Visible(countedColor);
        public Color SafeMissColor => Visible(missColor);
        public Color SafePlaceholderColor => Visible(placeholderColor);

        /// <summary>
        /// Clamp that handles NaN and infinity EXPLICITLY rather than trusting <c>Mathf.Clamp</c>.
        ///
        /// ⚠️ <c>Mathf.Clamp(NaN, a, b)</c> RETURNS NaN — every comparison against NaN is false, so both
        /// branches fall through. A NaN hold would then make the countdown `_timer -= dt` NaN forever, so
        /// `_timer &lt;= 0f` never becomes true and the readout stays on screen for the rest of the session
        /// at an alpha of NaN. Same discipline as <c>GradingConfig.Clamp01Finite</c> and
        /// <c>GalleryConfig.ThumbnailWidthFor</c>.
        ///
        /// Falls back to the DESIGN DEFAULT for a non-finite value (there is no sane clamp target for NaN)
        /// and to the nearest bound for a merely out-of-range one, so a deliberate 10 survives while a
        /// hand-authored 0 fails into something readable.
        /// </summary>
        private static float ClampFinite(float value, float min, float max, float fallback)
        {
            if (float.IsNaN(value) || float.IsInfinity(value)) return fallback;
            return Mathf.Clamp(value, min, max);
        }

        private static Color Visible(Color c) =>
            c.a < MinVisibleAlpha ? new Color(c.r, c.g, c.b, 1f) : c;

        /// <summary>
        /// Reports authoring mistakes that would break the HUD SILENTLY rather than loudly — the project's
        /// standing "fail-soft must not mean invisible" rule. Called once at <c>Awake</c> by
        /// <see cref="GradeHud"/>; returns false when everything is sane.
        ///
        /// Reports EVERY problem in one message rather than the first. Both <c>GradingConfig</c> and
        /// <c>GalleryConfig</c> return on the first hit and both carry a standing deferred item saying a
        /// designer with three mistakes should not need three play-mode cycles to find them
        /// (deferred-work.md, 1.10 and 1.11 reviews). A new validator has no reason to inherit that.
        /// </summary>
        public bool TryGetConfigProblem(out string problem)
        {
            string all = null;

            // Compared against the Safe* value, so this fires for the value the DESIGNER TYPED — which is
            // the whole reason there is no OnValidate repairing the field before Awake can read it.
            if (!Mathf.Approximately(holdSeconds, SafeHoldSeconds))
                all = Add(all, $"holdSeconds is {holdSeconds}, outside the usable range {MinHoldSeconds} to " +
                               $"{MaxHoldSeconds} — using {SafeHoldSeconds}s. {WhyHold(holdSeconds)}");

            if (!Mathf.Approximately(fadeSeconds, SafeFadeSeconds))
                all = Add(all, $"fadeSeconds is {fadeSeconds}, outside the usable range {MinFadeSeconds} to " +
                               $"{MaxFadeSeconds} — using {SafeFadeSeconds}s. {WhyFade(fadeSeconds)}");

            // The silent one in this shape: everything in range, nothing looks wrong, and the readout for
            // one capture is still on screen several captures later — describing a photograph the player
            // has already stopped thinking about.
            if (SafeVisibleSeconds > LingerWarnSeconds)
                all = Add(all, $"the readout stays on screen for {SafeVisibleSeconds:0.0}s " +
                               $"({SafeHoldSeconds:0.0}s hold + {SafeFadeSeconds:0.0}s fade), over the " +
                               $"{LingerWarnSeconds}s this project budgets for it. It describes ONE capture, " +
                               "so a hold this long will still be up when the next shot is taken.");

            all = ReportInvisible(all, countedColor, nameof(countedColor));
            all = ReportInvisible(all, missColor, nameof(missColor));
            all = ReportInvisible(all, placeholderColor, nameof(placeholderColor));

            problem = all;
            return all != null;
        }

        /// <summary>
        /// Why a bad hold matters, in the terms of the value that was actually typed.
        ///
        /// ⚠️ THE MESSAGE HAS TO MATCH THE MISTAKE. This used to say "a hold at or below zero is a readout
        /// that appears for a single frame" for EVERY out-of-range value, so the 2026-08-07 verification run
        /// printed that sentence under a hold of 9999 and again under a hold of NaN — advice that described
        /// neither. A validator whose explanation is wrong is worse than one that only states the number:
        /// the reader stops trusting the part that was right.
        /// </summary>
        private static string WhyHold(float authored)
        {
            if (float.IsNaN(authored) || float.IsInfinity(authored))
                return "A non-finite hold never counts down, so the readout would stay on screen — at an " +
                       "alpha of NaN — for the rest of the session.";

            return authored < MinHoldSeconds
                ? "A hold this short is a readout that appears for barely a frame and is never read, with a " +
                  "clean console."
                : "A hold this long is still describing one capture several captures later.";
        }

        /// <summary>Why a bad fade matters, in the terms of the value that was actually typed. Same rule as
        /// <see cref="WhyHold"/>: the explanation follows the mistake, not the field.</summary>
        private static string WhyFade(float authored)
        {
            if (float.IsNaN(authored) || float.IsInfinity(authored))
                return "A non-finite fade drives the alpha to NaN, which renders as nothing at all.";

            return authored < MinFadeSeconds
                ? "A negative fade would drive the alpha ramp backwards."
                : "A fade this long leaves the readout ghosting over the shots that follow it.";
        }

        /// <summary>A colour authored at alpha 0 is text that is not there, and it looks completely normal
        /// in the Inspector. Exactly the silent-nothing class, one channel over from the numbers.</summary>
        private static string ReportInvisible(string all, Color c, string field) =>
            c.a < MinVisibleAlpha
                ? Add(all, $"{field} has alpha {c.a}, so that line would be invisible on screen while " +
                           "reading as a perfectly valid colour in the Inspector — forcing it opaque.")
                : all;

        private static string Add(string all, string next) =>
            string.IsNullOrEmpty(all) ? next : all + "  " + next;

        // ⚠️ THERE IS DELIBERATELY NO OnValidate HERE, AND IT MUST NOT BE ADDED.
        //
        // GalleryConfig had one that repaired its fields through the Safe* accessors, with a comment
        // claiming that was harmless because the Awake warning arrived first. The 2026-07-30 code review
        // reproduced the claim and it is FALSE: Unity calls OnValidate when an asset is loaded and imported
        // and on every domain reload, long before any Awake runs. Every branch above detects trouble by
        // comparing the RAW field against its Safe* value, so once OnValidate has written Safe back into
        // the raw field the two agree and the branch can never fire again. Measured on GalleryConfig:
        //
        //     hand-authored maxStoredShots: 0
        //     before OnValidate -> warning fires ("outside the usable range 1 to 500")
        //     after  OnValidate -> raw is 1, warning silent, console clean
        //
        // So the failure mode the validator exists to catch was being silently repaired in the editor,
        // which is precisely where designers author these values. (In a player build OnValidate does not
        // exist, so the warning worked only where it was not needed.)
        //
        // Nothing is lost: every reader goes through a Safe* accessor, so an out-of-range asset still RUNS
        // correctly. The only difference is that it now also says so.
    }
}
