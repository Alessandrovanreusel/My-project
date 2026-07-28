using UnityEngine;
using CameraGame.Core;

namespace CameraGame.Grading
{
    /// <summary>
    /// Designer-facing thresholds for photo grading (Stories 1.9–1.10). Lives as a ScriptableObject asset
    /// (Assets/Data/Grading/GradingConfig.asset) assigned to <c>PhotoModeController</c> in the Inspector,
    /// so the rules for "does this shot count" and "how good was it" re-tune WITHOUT a recompile
    /// (Architecture §Configuration). Mirrors <c>CameraConfig</c> / <c>CaptureConfig</c> — no grading magic
    /// numbers live in code.
    ///
    /// Story 1.9 covered only the GATE (is the subject really in this photo). Story 1.10 adds the SCORE:
    /// the prominence sweet spot, centred placement, the cut-off penalty and the peak-timing window.
    /// This asset is deliberately the one place that grows.
    ///
    /// ⚠️ PLAIN NUMBERS, NOT <c>AnimationCurve</c>. The architecture sketch shows
    /// <c>cfg.CoverageCurve.Evaluate(...)</c>, and this deliberately does not follow it. Three reasons:
    /// this project hand-authors ScriptableObject assets as YAML (Unity MCP cannot create custom SO
    /// assets), and a curve serialises as a nested keyframe list with tangents that is miserable to write
    /// and impossible to eyeball; a curve cannot be VALIDATED (there is no "this curve never reaches 1"
    /// check that isn't just sampling it), and silent misconfiguration is this project's recurring failure
    /// mode; and four named numbers are far more tunable by hand than a curve editor. The trapezoid below
    /// is exactly the shape the GDD describes anyway.
    /// </summary>
    [CreateAssetMenu(menuName = "CameraGame/Grading/Grading Config", fileName = "GradingConfig")]
    public class GradingConfig : ScriptableObject
    {
        [Header("Subject gate")]

        [Tooltip("How tall the subject's projected box must be, as a fraction of the FRAME HEIGHT, for the " +
                 "shot to count at all. Below this the shot is a miss no matter how well framed it is.\n\n" +
                 "HEIGHT, not area. Story 1.9 gated on the box's AREA and it did not work: the box around a " +
                 "tall, thin, arms-down figure is mostly empty air, so a perfectly good full-body portrait " +
                 "measured 4.5% and was rejected while the GDD called for ~8%. The same photograph measures " +
                 "39% of the frame height, which is both stable across the walk cycle and close to how a " +
                 "photographer actually judges 'he fills the frame'. Tune it against the photo shoot, not " +
                 "by arithmetic.")]
        [Range(0f, 1f)] public float minSubjectHeight = 0.20f;

        [Header("Occlusion")]

        [Tooltip("Which layers BLOCK line of sight to the subject. This must EXCLUDE the subject's own " +
                 "layer, or the subject occludes itself and every shot fails. Story 1.9 puts event actors " +
                 "on the 'Subject' layer and sets this to Default (the world). An empty mask means nothing " +
                 "can ever block the view, which silently passes every shot — it is warned about at Awake.")]
        public LayerMask occluderMask;

        [Tooltip("How many points across the subject are line-of-sight tested. 1 tests only the centre, " +
                 "which fails a 95%-visible subject standing behind a lamppost. 5 is a good default and " +
                 "already spans both sides of all three axes. The ceiling is 15 because that is how many " +
                 "DISTINCT sample points exist (centre + 8 box corners + 6 face centres) — asking for more " +
                 "would just re-test points already covered.")]
        [Range(1, ShotGrader.MaxOcclusionSamples)] public int occlusionSamples = 5;

        [Tooltip("Fraction of those sample points that must be unblocked for the subject to count as " +
                 "visible. 0.2 = 'at least a fifth of him is showing'. 1.0 demands a completely " +
                 "unobstructed view, which is harsher than it sounds once fences and railings exist.")]
        [Range(0f, 1f)] public float minVisibleSamples = 0.2f;

        [Header("Composition — prominence sweet spot")]

        // A trapezoid on the same measure as the gate: 0 at falloffBelow, rising to full marks across
        // [idealMin, idealMax], easing back to 0 at falloffAbove.
        //
        //  1.0            ┌───────────────┐
        //                /                 \
        //  0.0  ────────┘                   └────────
        //          falloffBelow  idealMin  idealMax  falloffAbove

        [Tooltip("Bottom of the sweet spot: subject height fraction at which composition first hits FULL " +
                 "marks. The GDD's '~25-50% of the frame' was written about a photograph, not about a " +
                 "measurement, so this was settled by looking at the shoot instead: below about 0.45 he " +
                 "reads as a small figure in a lot of empty frame (photo-shoot/b_mid.png at 0.39).")]
        [Range(0f, 1f)] public float prominenceIdealMin = 0.45f;

        [Tooltip("Top of the sweet spot: the tallest the subject can be while still scoring full marks. " +
                 "Above this he starts running out of frame and the score eases off. Set from the shoot: " +
                 "a_close.png at 0.82 is cramped — head near the top edge, feet near the bottom — while " +
                 "e_far_zoomed.png at 0.55 and o_centred_closeup.png at 0.66 are the best-proportioned " +
                 "photographs in the set.\n\n" +
                 "⚠️ A first pass used 0.35..0.85 and it was too WIDE to be worth anything: every framing " +
                 "from 0.39 to 0.82 scored an identical 83%, so prominence did no work at all and the star " +
                 "rating could not tell a cramped shot from a well-proportioned one.")]
        [Range(0f, 1f)] public float prominenceIdealMax = 0.78f;

        [Tooltip("Height fraction at which prominence reaches ZERO on the small side. A subject smaller " +
                 "than this is a street scene, not a portrait of him. Keep it BELOW the gate " +
                 "(minSubjectHeight) or some shots that pass the gate score a flat 0 and read to the " +
                 "player as though the shutter had missed entirely.")]
        [Range(0f, 1f)] public float prominenceFalloffBelow = 0.15f;

        [Tooltip("Height fraction at which prominence reaches ZERO on the large side. Values above 1 are " +
                 "deliberate and normal: the measured box is CLAMPED to the frame, so it can never read " +
                 "more than 1.0, and a value of 1.15 means 'a subject filling the whole frame still keeps " +
                 "half of his prominence' — the cut-off penalty below is what actually punishes a subject " +
                 "spilling past the edges.")]
        [Min(0f)] public float prominenceFalloffAbove = 1.15f;

        [Header("Composition — placement & framing")]

        [Tooltip("How much placement can pull the composition score down, measured as distance from the " +
                 "CENTRE of the frame. 0 = placement is ignored; 1 = a subject jammed into a corner scores " +
                 "zero on composition.\n\n" +
                 "⚠️ CENTRED, NOT RULE-OF-THIRDS. The GDD originally asked for the opposite — a bonus for " +
                 "sitting near a thirds line — and it was built that way and then disproven by looking at " +
                 "the photographs. Alexv judged the centred shot the better picture in matched pairs " +
                 "(identical subject, identical instant, identical camera position, differing only in " +
                 "where in the frame he sits), in the empty test world AND again in the real town once the " +
                 "'the thirds shot only looks lopsided because the rest of the frame is empty' confound " +
                 "was removed. GDD FR6 was rewritten to match; see the story's Dev Agent Record.\n\n" +
                 "Keep it modest anyway: this should punish a subject shoved into a corner, not micro-" +
                 "manage a shot that is merely a little off-centre.")]
        [Range(0f, 1f)] public float centreWeight = 0.35f;

        [Tooltip("How much of the composition score the FRAME EDGE can take when it clips the subject. " +
                 "Measured as the fraction of his projected box that actually landed inside the frame, so " +
                 "0.6 means a subject cut exactly in half keeps 70% of his composition score. This is the " +
                 "term that stops a nose-to-nose shot — where the box fills the frame because most of him " +
                 "is outside it — from reading as a perfectly prominent subject.")]
        [Range(0f, 1f)] public float cutoffWeight = 0.6f;

        [Header("Timing")]

        [Tooltip("Seconds either side of the PEAK WINDOW that still score full timing marks (the GDD's " +
                 "±0.5 s).\n\n" +
                 "⚠️ Either side of the WINDOW, not of an instant. The peak is an interval (1.5 s for the " +
                 "Town Drunk) and every frame of it scores 1.0 — see ISubject.PeakOffset for why measuring " +
                 "from the window's start instead would score the last frame of the money shot as 1.5 s " +
                 "early.")]
        [Min(0f)] public float timingFullSeconds = 0.5f;

        [Tooltip("Seconds either side of the peak window at which timing reaches ZERO (the GDD's ±2 s). " +
                 "Must be greater than timingFullSeconds, or the timing curve becomes a cliff — warned " +
                 "about at Awake.")]
        [Min(0f)] public float timingZeroSeconds = 2f;

        // --- Safe accessors ---------------------------------------------------------------------------
        //
        // Every tunable is read through one of these, never raw. [Range]/[Min] and OnValidate are BOTH
        // editor-only, and this project authors ScriptableObject assets as hand-written YAML, which passes
        // through neither — so an out-of-range or NaN value reaches the grader intact. NaN is the dangerous
        // one: it fails every comparison, so `x < NaN` is false forever and the check it guards silently
        // stops existing. (Note Mathf.Clamp01(NaN) is NaN, which is why plain clamping is not enough.)

        /// <summary>Occlusion samples, guaranteed usable regardless of what is serialized in the asset, and
        /// capped at the number of distinct sample points that actually exist.</summary>
        public int SafeOcclusionSamples =>
            Mathf.Clamp(occlusionSamples, 1, ShotGrader.MaxOcclusionSamples);

        /// <summary>Visible-fraction threshold, guaranteed in [0,1] and never NaN.</summary>
        public float SafeMinVisibleSamples => Clamp01Finite(minVisibleSamples, 0f);

        /// <summary>
        /// The subject-height gate, guaranteed in [0,1] and never NaN. Falls back to the design value
        /// rather than 0, so a corrupt asset fails CLOSED (gate active) instead of open (everything counts).
        /// </summary>
        public float SafeMinSubjectHeight => Clamp01Finite(minSubjectHeight, DefaultMinSubjectHeight);

        /// <summary>Bottom of the prominence sweet spot, finite and in [0,1].</summary>
        public float SafeProminenceIdealMin => Clamp01Finite(prominenceIdealMin, 0.45f);

        /// <summary>Top of the prominence sweet spot, finite and in [0,1].</summary>
        public float SafeProminenceIdealMax => Clamp01Finite(prominenceIdealMax, 0.78f);

        /// <summary>Small-side zero point, finite and in [0,1].</summary>
        public float SafeProminenceFalloffBelow => Clamp01Finite(prominenceFalloffBelow, 0.15f);

        /// <summary>Large-side zero point, finite. Allowed above 1 — see the field's tooltip.</summary>
        public float SafeProminenceFalloffAbove => ClampFinite(prominenceFalloffAbove, 0f, 4f, 1.15f);

        /// <summary>Off-centre placement penalty weight, finite and in [0,1].</summary>
        public float SafeCentreWeight => Clamp01Finite(centreWeight, 0.35f);

        /// <summary>Cut-off penalty weight, finite and in [0,1].</summary>
        public float SafeCutoffWeight => Clamp01Finite(cutoffWeight, 0.6f);

        /// <summary>Full-marks timing half-window in seconds, finite and non-negative.</summary>
        public float SafeTimingFullSeconds => ClampFinite(timingFullSeconds, 0f, 60f, 0.5f);

        /// <summary>Zero-marks timing half-window in seconds, finite and non-negative. Ordering against
        /// <see cref="SafeTimingFullSeconds"/> is enforced by <see cref="ResolveTimingWindow"/>, not here —
        /// a per-field accessor cannot see its neighbour.</summary>
        public float SafeTimingZeroSeconds => ClampFinite(timingZeroSeconds, 0f, 60f, 2f);

        // --- Resolvers --------------------------------------------------------------------------------
        //
        // A Safe* accessor can only repair ONE field; it cannot see that idealMin has been authored above
        // idealMax, or that falloffBelow sits on top of idealMin (which would make the curve divide by a
        // zero width). These two resolvers hand the grader a set that is guaranteed mutually consistent, so
        // the grader never has to defend itself against the asset.

        /// <summary>The smallest width any segment of a curve may have, so InverseLerp can never divide by
        /// zero however the asset is authored.</summary>
        private const float MinCurveWidth = 0.001f;

        /// <summary>
        /// Resolves the prominence trapezoid into four points guaranteed to satisfy
        /// <c>falloffBelow &lt; idealMin &lt;= idealMax &lt; falloffAbove</c>.
        /// Note <paramref name="falloffBelow"/> may come back NEGATIVE when the sweet spot starts at 0 —
        /// that is correct and means "prominence never reaches zero on the small side", not an error.
        /// </summary>
        public void ResolveProminenceCurve(out float falloffBelow, out float idealMin,
                                           out float idealMax, out float falloffAbove)
        {
            float a = SafeProminenceIdealMin, b = SafeProminenceIdealMax;

            // Sorted rather than rejected: an inverted sweet spot is warned about at Awake, and grading must
            // still produce a usable score in the meantime (NFR8 — the game has no fail state).
            idealMin = Mathf.Min(a, b);
            idealMax = Mathf.Max(a, b);

            falloffBelow = Mathf.Min(SafeProminenceFalloffBelow, idealMin - MinCurveWidth);
            falloffAbove = Mathf.Max(SafeProminenceFalloffAbove, idealMax + MinCurveWidth);
        }

        /// <summary>
        /// Resolves the timing window so that <c>zero &gt; full</c> always holds. Without this a
        /// <c>timingZeroSeconds &lt;= timingFullSeconds</c> asset turns the smooth falloff into a cliff —
        /// or divides by zero — and every shot outside the peak scores a flat 0 with a clean console.
        /// </summary>
        public void ResolveTimingWindow(out float fullSeconds, out float zeroSeconds)
        {
            fullSeconds = SafeTimingFullSeconds;
            zeroSeconds = Mathf.Max(SafeTimingZeroSeconds, fullSeconds + MinCurveWidth);
        }

        /// <summary>The design value for the subject-height gate — the fallback for a bad asset.</summary>
        private const float DefaultMinSubjectHeight = 0.20f;

        /// <summary>Clamps to [0,1], substituting <paramref name="fallback"/> for NaN/Infinity. Mathf.Clamp01
        /// alone is not enough: it returns NaN for NaN, so the bad value survives every guard downstream.</summary>
        private static float Clamp01Finite(float value, float fallback) =>
            ClampFinite(value, 0f, 1f, fallback);

        /// <summary>As <see cref="Clamp01Finite"/> but for tunables whose sensible range is not [0,1].</summary>
        private static float ClampFinite(float value, float min, float max, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp(value, min, max);

        /// <summary>
        /// Reports authoring mistakes that would silently break grading rather than loudly fail it — the
        /// project's standing "fail-soft must not mean invisible" rule, which Story 1.8's review added
        /// after a <c>cueRadius</c> of 0 produced total silence with a completely clean console.
        /// Called once at Awake by the capture controller; returns false when everything is sane.
        ///
        /// The 1.10 additions are the harder category to notice than a missing reference: a sweet spot that
        /// can never be reached does not crash, does not warn and does not look wrong — it just quietly
        /// caps every photograph the player will ever take at three stars.
        /// </summary>
        public bool TryGetConfigProblem(out string problem)
        {
            // --- Non-finite first ---------------------------------------------------------------------
            // NaN fails every comparison below, so without these checks a NaN threshold reports "everything
            // is fine" and then disables its gate at runtime.
            // Each fallback is read back from its own Safe* accessor rather than repeated as a literal: for
            // a non-finite input the accessor RETURNS the fallback, so the two cannot drift apart. A warning
            // that names a fallback the code does not actually use is worse than no warning — it sends the
            // reader to check a value that was never applied.
            if (TryReportNonFinite(nameof(minSubjectHeight), minSubjectHeight,
                    "the subject-height gate would never reject anything", SafeMinSubjectHeight, out problem))
                return true;

            if (TryReportNonFinite(nameof(minVisibleSamples), minVisibleSamples,
                    "the occlusion gate would never reject anything", SafeMinVisibleSamples, out problem))
                return true;

            const string CompositionNaN = "composition would score NaN and every shot would read as 0%";

            if (TryReportNonFinite(nameof(prominenceIdealMin), prominenceIdealMin,
                    CompositionNaN, SafeProminenceIdealMin, out problem))
                return true;

            if (TryReportNonFinite(nameof(prominenceIdealMax), prominenceIdealMax,
                    CompositionNaN, SafeProminenceIdealMax, out problem))
                return true;

            if (TryReportNonFinite(nameof(prominenceFalloffBelow), prominenceFalloffBelow,
                    CompositionNaN, SafeProminenceFalloffBelow, out problem))
                return true;

            if (TryReportNonFinite(nameof(prominenceFalloffAbove), prominenceFalloffAbove,
                    CompositionNaN, SafeProminenceFalloffAbove, out problem))
                return true;

            if (TryReportNonFinite(nameof(centreWeight), centreWeight,
                    CompositionNaN, SafeCentreWeight, out problem))
                return true;

            if (TryReportNonFinite(nameof(cutoffWeight), cutoffWeight,
                    CompositionNaN, SafeCutoffWeight, out problem))
                return true;

            const string TimingNaN = "timing would score NaN and every shot would read as 0%";

            if (TryReportNonFinite(nameof(timingFullSeconds), timingFullSeconds,
                    TimingNaN, SafeTimingFullSeconds, out problem))
                return true;

            if (TryReportNonFinite(nameof(timingZeroSeconds), timingZeroSeconds,
                    TimingNaN, SafeTimingZeroSeconds, out problem))
                return true;

            // --- Occlusion mask -----------------------------------------------------------------------
            if (occluderMask.value == 0)
            {
                problem = "occluderMask is empty (Nothing) — no geometry can ever block the view, so the " +
                          "occlusion half of the subject gate will pass every shot.";
                return true;
            }

            // The one mistake the occluderMask tooltip warns about IN CAPITALS was the one nothing checked.
            // If the subject's own layer is in the mask, every linecast hits the subject itself, the visible
            // fraction is 0, and EVERY shot fails Occluded regardless of framing — with a clean console.
            // (Latent today only because the drunk prefab carries no colliders at all; the moment anyone adds
            // one for stealth or interaction, this becomes total and invisible.)
            int subjectLayer = LayerMask.NameToLayer(GameConstants.Layers.Subject);
            if (subjectLayer < 0)
            {
                problem = $"there is no '{GameConstants.Layers.Subject}' layer in this project — event actors " +
                          "cannot be excluded from their own line-of-sight test. Add it in Project Settings > Tags and Layers.";
                return true;
            }

            if ((occluderMask.value & (1 << subjectLayer)) != 0)
            {
                problem = $"occluderMask includes the '{GameConstants.Layers.Subject}' layer, so the subject " +
                          "blocks the line of sight to itself — every shot will fail as Occluded no matter " +
                          "how well framed it is. Remove that layer from the mask.";
                return true;
            }

            // --- Gate boundaries ----------------------------------------------------------------------
            if (minSubjectHeight <= 0f)
            {
                problem = "minSubjectHeight is 0 — the subject-size gate is disabled, so a figure one pixel " +
                          "tall counts as captured.";
                return true;
            }

            if (minSubjectHeight >= 1f)
            {
                problem = "minSubjectHeight is 1 — the subject would have to be as tall as the whole frame, " +
                          "so almost every shot will fail.";
                return true;
            }

            // Symmetry with the two minSubjectHeight checks above: `visible < 0f` is false even when EVERY
            // sample is blocked, so a zero threshold turns the occlusion gate off entirely.
            if (minVisibleSamples <= 0f)
            {
                problem = "minVisibleSamples is 0 — the occlusion gate is disabled, so a subject completely " +
                          "hidden behind a wall still counts as captured.";
                return true;
            }

            // --- Composition curve shape --------------------------------------------------------------
            if (prominenceIdealMin > prominenceIdealMax)
            {
                problem = $"the prominence sweet spot is inverted (prominenceIdealMin {prominenceIdealMin:0.###} " +
                          $"is above prominenceIdealMax {prominenceIdealMax:0.###}) — grading will sort them to " +
                          "keep running, but the band you authored is not the band being scored. Swap them.";
                return true;
            }

            if (prominenceFalloffBelow >= prominenceIdealMin)
            {
                problem = $"prominenceFalloffBelow ({prominenceFalloffBelow:0.###}) is not below " +
                          $"prominenceIdealMin ({prominenceIdealMin:0.###}) — the small side of the curve has no " +
                          "width, so composition drops from full marks straight to zero with nothing in " +
                          "between. Lower it.";
                return true;
            }

            if (prominenceFalloffAbove <= prominenceIdealMax)
            {
                problem = $"prominenceFalloffAbove ({prominenceFalloffAbove:0.###}) is not above " +
                          $"prominenceIdealMax ({prominenceIdealMax:0.###}) — the large side of the curve has no " +
                          "width, so a subject one pixel over the sweet spot scores zero composition. Raise it.";
                return true;
            }

            // --- "Five stars is unreachable" ----------------------------------------------------------
            // The silent one. None of these crash, none of them look wrong; they just cap every photograph
            // the player will ever take, forever, with a clean console.
            if (minSubjectHeight > prominenceIdealMax)
            {
                problem = $"the gate (minSubjectHeight {minSubjectHeight:0.###}) is above the top of the " +
                          $"prominence sweet spot ({prominenceIdealMax:0.###}), so every shot that passes the " +
                          "gate is ALREADY past full marks and composition can never reach 1 — five stars " +
                          "would be unreachable. Lower the gate or raise the sweet spot.";
                return true;
            }

            if (prominenceFalloffBelow >= minSubjectHeight)
            {
                problem = $"prominenceFalloffBelow ({prominenceFalloffBelow:0.###}) is at or above the gate " +
                          $"({minSubjectHeight:0.###}), so shots that just pass the gate score a flat 0% — " +
                          "indistinguishable to the player from the shutter missing entirely. Lower it below " +
                          "the gate.";
                return true;
            }

            // --- Timing window ------------------------------------------------------------------------
            if (timingZeroSeconds <= timingFullSeconds)
            {
                problem = $"timingZeroSeconds ({timingZeroSeconds:0.###}) is not greater than " +
                          $"timingFullSeconds ({timingFullSeconds:0.###}) — the timing falloff has no width, so " +
                          "every shot outside the peak window scores zero and the blended grade goes to 0% " +
                          "with it. Raise timingZeroSeconds.";
                return true;
            }

            problem = null;
            return false;
        }

        /// <summary>Shared body for the ten non-finite checks — same sentence shape every time: name the
        /// mistake, say what it does to the player's grades, say what we fall back to.</summary>
        private static bool TryReportNonFinite(string field, float value, string consequence,
                                               float fallback, out string problem)
        {
            if (!float.IsNaN(value) && !float.IsInfinity(value))
            {
                problem = null;
                return false;
            }

            problem = $"{field} is {value}, which is not a usable number — {consequence}. " +
                      $"Falling back to {fallback:0.###}.";
            return true;
        }

        private void OnValidate()
        {
            // Through the Safe* accessors so a NaN typed into the Inspector is repaired rather than clamped
            // to another NaN (Mathf.Clamp01(NaN) is NaN).
            minSubjectHeight = SafeMinSubjectHeight;
            occlusionSamples = SafeOcclusionSamples;
            minVisibleSamples = SafeMinVisibleSamples;

            prominenceIdealMin = SafeProminenceIdealMin;
            prominenceIdealMax = SafeProminenceIdealMax;
            prominenceFalloffBelow = SafeProminenceFalloffBelow;
            prominenceFalloffAbove = SafeProminenceFalloffAbove;
            centreWeight = SafeCentreWeight;
            cutoffWeight = SafeCutoffWeight;
            timingFullSeconds = SafeTimingFullSeconds;
            timingZeroSeconds = SafeTimingZeroSeconds;

            // NOT sorted/reordered here, deliberately. OnValidate fires on every keystroke in the Inspector,
            // so "repairing" an inverted sweet spot would fight the designer mid-edit — typing 0.9 into
            // idealMin on the way to changing both fields would silently swap them. Ordering is enforced
            // where it matters (ResolveProminenceCurve) and reported where it helps (TryGetConfigProblem).
        }
    }
}
