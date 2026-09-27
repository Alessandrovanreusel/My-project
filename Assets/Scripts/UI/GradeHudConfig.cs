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

        [Tooltip("Text colour for a COUNTED shot of 3 stars — the middle of the grade. The counted readout " +
                 "is BANDED: 4-5 stars use strongColor, 3 stars this one, 1-2 stars weakColor. Colour was " +
                 "already what made a MISS unmistakable and was doing nothing for the grade, so a 5-star " +
                 "and a 0% shot read alike (AC3, 2026-09-11). See GradeHudConfig.CountedColorFor.")]
        public Color countedColor = new Color(0.94f, 0.94f, 0.90f);

        [Tooltip("Text colour for a STRONG counted shot (4-5 stars). See countedColor for why the counted " +
                 "readout is banded by grade at all.")]
        public Color strongColor = new Color(0.55f, 0.93f, 0.62f);

        [Tooltip("Text colour for a WEAK counted shot (1-2 stars). Must not be confusable with missColor: " +
                 "a weak shot and a miss both read 1 star, so colour is most of what separates them.\n\n" +
                 "Bright on purpose: it is drawn on a translucent panel with the live world behind it, and " +
                 "must clear 4.5:1 contrast there on a bright background. A darker amber measured below " +
                 "that off the real captures (2026-09-12). If you retune it, re-measure a capture with " +
                 "tools/verification/build_hud_panel_sheet.py --rect rather than judging by eye.")]
        public Color weakColor = new Color(1.00f, 0.88f, 0.31f);

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

        /// <summary>
        /// How far apart two readout colours must be, in CIE-Lab ΔE, to count as tellable apart at a glance.
        ///
        /// ⚠️ ΔE, NOT RGB DISTANCE. The 2026-09-12 review found the shipped `weakColor` and `missColor`
        /// clearing a Euclidean-RGB threshold of 0.25 by 0.0067 while being ΔE 45.5 apart — a pair that is
        /// obviously different to the eye, sitting one nudge away from failing its own test. RGB distance is
        /// not perceptually uniform, so its verdicts near a threshold mean little.
        ///
        /// ΔE76 is better, not perfect, and two limits are worth knowing (2026-09-27 review): it does NOT
        /// check lightness on its own — two colours of equal luminance pass on hue alone, which normal
        /// vision handles but colour-blind players may not — and it overstates distances between saturated
        /// colours (see <see cref="PerceptualDistance"/>). Both are logged in deferred-work.md.
        ///
        /// 25 is the floor, not the target. On the six pairs the validator checks, the shipped palette sits
        /// at ΔE 50-87 (measured 2026-09-27) and should stay well clear of it. This exists to catch an
        /// authoring mistake, not to police tuning.
        /// </summary>
        public const float MinBandSeparation = 25f;

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
        public Color SafeStrongColor => Visible(strongColor);
        public Color SafeWeakColor => Visible(weakColor);
        public Color SafeMissColor => Visible(missColor);
        public Color SafePlaceholderColor => Visible(placeholderColor);

        /// <summary>
        /// The colour for a counted shot of <paramref name="stars"/> stars.
        ///
        /// ⚠️ THIS EXISTS BECAUSE ALEXV COULD NOT TELL A 5★ READOUT FROM A 0% ONE AT A GLANCE (AC3,
        /// 2026-09-11). Shown the three panels with the world cropped away he said the MISS was "obvious"
        /// but "for the timing and the not timed ones, it's not obvious which one is which". The reason is
        /// visible in his own answer: the miss changes COLOUR, swaps the number for the word MISSED and
        /// prints dashes, while both counted shots shared one cream colour and differed only by small star
        /// glyphs and a percentage. Colour was already doing the heavy lifting for the miss; it simply was
        /// not doing any for the grade.
        ///
        /// Banded rather than continuously lerped, so the readout says "this was good / this was weak"
        /// instead of asking the player to judge a hue. The bands follow the star scale the player already
        /// sees, so nothing new has to be learned — and 3★ keeps the original neutral cream.
        ///
        /// ⚠️ "3★ IS THE MIDDLE" IS A STATEMENT ABOUT THE STAR SCALE, NOT ABOUT ANY PARTICULAR SHOT. This
        /// comment has been wrong twice by naming shots: first claiming "a middling shot looks exactly as it
        /// always did", then quoting exemplar grades that were already stale when written — the rig's
        /// shutter timing varies run to run, and a grading story can move every number. Which percentages
        /// get 3★ is decided by the star thresholds on GradingConfig; which photographs land there is a
        /// MEASUREMENT. If you change a band, photograph it — do not reason about which shot lands where.
        ///
        /// 1★ and 2★ deliberately SHARE the weak band (Alexv's call, 2026-09-12): the readout is meant to
        /// say "this shot was weak", not to rank one weak shot against another — the stars and the
        /// percentage already do that.
        /// </summary>
        public Color CountedColorFor(int stars) =>
            stars >= 4 ? SafeStrongColor :
            stars <= 2 ? SafeWeakColor :
                         SafeCountedColor;

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

        /// <summary>
        /// A colour guaranteed to be drawable, however the asset was authored.
        ///
        /// ⚠️ WRITTEN AS <c>!(a >= min)</c>, NOT <c>(a &lt; min)</c> — AND THAT IS THE WHOLE POINT.
        /// <c>NaN &lt; 0.05f</c> is <b>false</b>, so the original comparison waved a NaN alpha straight
        /// through unrepaired, and <see cref="ReportInvisible"/> used the identical comparison so the
        /// validator said nothing either. The Color32 conversion of NaN yields 0, i.e. invisible or black
        /// text with a completely clean console — the silent-nothing shape this file's header enumerates
        /// five prior instances of, in the one member of the set that had not been given the
        /// <see cref="ClampFinite"/> treatment two members up. Found by the 2026-08-07 code review.
        ///
        /// The negated form is true for NaN, so a non-finite alpha is repaired like any other unusable one.
        /// RGB goes through the same finite-clamp: a NaN in a colour channel is just as unrenderable as one
        /// in the alpha, and clamping preserves the hue a designer chose for every value that is sane.
        /// </summary>
        private static Color Visible(Color c) =>
            new Color(Channel(c.r), Channel(c.g), Channel(c.b),
                      !(c.a >= MinVisibleAlpha) ? 1f : Mathf.Clamp01(c.a));

        /// <summary>One colour channel, guaranteed finite and in range. NaN becomes 0 rather than
        /// propagating, for the same reason <see cref="ClampFinite"/> exists.</summary>
        private static float Channel(float v) => float.IsNaN(v) ? 0f : Mathf.Clamp01(v);

        /// <summary>
        /// Perceptual distance between two colours (CIE-Lab ΔE76) — how different two readout colours look
        /// from EACH OTHER. Both are put through <see cref="Visible"/> first, so a repaired value (NaN,
        /// out of range, near-zero alpha) is measured as it would be drawn rather than as it was typed.
        ///
        /// ⚠️ WHAT THIS DOES NOT ANSWER: whether either colour is READABLE. It compares the two as if opaque
        /// and ignores the alpha, the panel and the world behind the text, so it says nothing about
        /// contrast. That is measured off the real captures (tools/verification/build_hud_panel_sheet.py).
        ///
        /// ΔE76 rather than ΔE2000 is a simplicity trade with a known cost: ΔE76 overstates distances
        /// between SATURATED colours — two strong blues can score ΔE76 25.2 and ΔE2000 3.8, i.e. pass here
        /// while being hard to tell apart. Nothing in the shipped palette is in that region. Switching metric
        /// is not a drop-in fix: the shipped palette is ΔE2000 23-58, so the floor would need re-deriving
        /// (deferred-work.md, 2026-09-27).
        /// </summary>
        public static float PerceptualDistance(Color a, Color b)
        {
            Vector3 la = Lab(Visible(a)), lb = Lab(Visible(b));
            return Vector3.Distance(la, lb);
        }

        /// <summary>sRGB to CIE-Lab (D65). Gamma is undone first — skipping that step is the usual reason a
        /// "perceptual" comparison turns out to be no such thing.</summary>
        private static Vector3 Lab(Color c)
        {
            float r = Linear(c.r), g = Linear(c.g), b = Linear(c.b);

            float x = (r * 0.4124564f + g * 0.3575761f + b * 0.1804375f) / 0.95047f;
            float y = (r * 0.2126729f + g * 0.7151522f + b * 0.0721750f) / 1.00000f;
            float z = (r * 0.0193339f + g * 0.1191920f + b * 0.9503041f) / 1.08883f;

            float fx = LabF(x), fy = LabF(y), fz = LabF(z);
            return new Vector3(116f * fy - 16f, 500f * (fx - fy), 200f * (fy - fz));
        }

        private static float Linear(float v) =>
            v <= 0.04045f ? v / 12.92f : Mathf.Pow((v + 0.055f) / 1.055f, 2.4f);

        private static float LabF(float t) =>
            t > 0.008856f ? Mathf.Pow(t, 1f / 3f) : 7.787f * t + 16f / 116f;

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
            all = ReportInvisible(all, strongColor, nameof(strongColor));
            all = ReportInvisible(all, weakColor, nameof(weakColor));
            all = ReportInvisible(all, missColor, nameof(missColor));
            all = ReportInvisible(all, placeholderColor, nameof(placeholderColor));

            // ⚠️ THE BANDING'S RUNTIME DEFENCE — THE ONE THAT SEES WHATEVER CONFIG THE HUD IS ACTUALLY GIVEN.
            //
            // The 2026-09-12 review set strongColor = weakColor in the SHIPPED .asset and ran the suite:
            // all green, clean console, and a readout with no banding left in it at all. The fixtures in
            // GradeHudConfigTests are CreateInstance objects, so they pin the C# defaults while the player
            // sees the asset. The answer is the project's standard one for its most repeated failure shape
            // — a hand-authored value that disables a feature silently: the validator reads what the
            // designer typed and says so at Awake. The suite now backs it up before anyone presses Play
            // (ShippedAsset_PassesItsOwnValidator validates every GradeHudConfig asset in the project).
            all = ReportConfusable(all, strongColor, nameof(strongColor), countedColor, nameof(countedColor), WhyBand);
            all = ReportConfusable(all, weakColor, nameof(weakColor), countedColor, nameof(countedColor), WhyBand);
            all = ReportConfusable(all, strongColor, nameof(strongColor), weakColor, nameof(weakColor), WhyBand);

            // A COUNTED shot and a MISS are different outcomes, and colour is most of what separates them —
            // both can read 1★. That requirement is older than the banding, so these three explain
            // themselves differently. The weak/miss pair is the one that was nearly lost: the amber shipped
            // on 2026-09-11 sat ΔE 45.5 from the miss, on a metric (RGB) that called it borderline.
            all = ReportConfusable(all, weakColor, nameof(weakColor), missColor, nameof(missColor), WhyMiss);
            all = ReportConfusable(all, countedColor, nameof(countedColor), missColor, nameof(missColor), WhyMiss);
            all = ReportConfusable(all, strongColor, nameof(strongColor), missColor, nameof(missColor), WhyMiss);

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
        /// in the Inspector. Exactly the silent-nothing class, one channel over from the numbers.
        ///
        /// ⚠️ THE NEGATED COMPARISON IS LOAD-BEARING HERE TOO — see <see cref="Visible"/>. With
        /// <c>c.a &lt; MinVisibleAlpha</c> a NaN alpha reported nothing, so the one authoring mistake that
        /// defeated the repair was also the one the validator stayed silent about.</summary>
        private static string ReportInvisible(string all, Color c, string field) =>
            !(c.a >= MinVisibleAlpha) || float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b)
                ? Add(all, $"{field} is {ColorText(c)}, so that line would be invisible or mis-coloured on " +
                           "screen while reading as a perfectly valid colour in the Inspector — repairing it.")
                : all;

        /// <summary>
        /// Reports two readout colours that a player could not tell apart, naming both fields and the
        /// measured distance so the designer can see how far off it is rather than just that it is off.
        ///
        /// Warns rather than repairs, for the same reason <see cref="LingerWarnSeconds"/> warns: there is no
        /// correct colour to substitute, and quietly moving a designer's hue would be worse than saying so.
        ///
        /// Three details, each from the 2026-09-27 review, and each the "message has to match the mistake"
        /// rule from <see cref="WhyHold"/> again:
        /// - It prints the colours AS DRAWN, because that is what was measured. A NaN field repaired to
        ///   black, or a 0-255 value clamped to white, would otherwise produce a line about a colour the
        ///   designer never typed, with nothing to connect the two.
        /// - The distance is TRUNCATED, not rounded: 24.97 rounded prints "ΔE 25.0 apart (needs 25)", a
        ///   message that contradicts itself.
        /// - The reason is the caller's (<see cref="WhyBand"/> or <see cref="WhyMiss"/>): a counted shot
        ///   that reads like a miss is a different failure from two grades that read alike.
        /// </summary>
        private static string ReportConfusable(string all, Color a, string fieldA, Color b, string fieldB,
                                               string why)
        {
            float d = PerceptualDistance(a, b);
            return d < MinBandSeparation
                ? Add(all, $"{fieldA} {Rgb(Visible(a))} and {fieldB} {Rgb(Visible(b))} are only " +
                           $"ΔE {Mathf.Floor(d * 10f) / 10f:0.0} apart as drawn (needs {MinBandSeparation:0}) — " +
                           why)
                : all;
        }

        private const string WhyBand =
            "a player could not tell those two grades apart at a glance, which is the whole reason the " +
            "counted readout is banded by grade.";

        private const string WhyMiss =
            "a player could not tell that counted shot from a MISS at a glance — both can read 1 star, so " +
            "colour is most of what separates them.";

        private static string Rgb(Color c) => $"({c.r:0.00}, {c.g:0.00}, {c.b:0.00})";

        /// <summary>Names what is actually wrong with a colour, rather than always blaming the alpha — the
        /// same rule <see cref="WhyHold"/> follows. A validator whose explanation is wrong costs more than
        /// one that only states the value.</summary>
        private static string ColorText(Color c) =>
            float.IsNaN(c.r) || float.IsNaN(c.g) || float.IsNaN(c.b)
                ? $"({c.r}, {c.g}, {c.b}, {c.a}) — a non-finite colour channel"
                : float.IsNaN(c.a) ? "alpha NaN" : $"alpha {c.a}";

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
