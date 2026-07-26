using UnityEngine;
using CameraGame.Core;

namespace CameraGame.Grading
{
    /// <summary>
    /// Designer-facing thresholds for photo grading (Story 1.9). Lives as a ScriptableObject asset
    /// (Assets/Data/Grading/GradingConfig.asset) assigned to <c>PhotoModeController</c> in the Inspector,
    /// so the rules for "does this shot count" re-tune WITHOUT a recompile (Architecture §Configuration).
    /// Mirrors <c>CameraConfig</c> / <c>CaptureConfig</c> — no grading magic numbers live in code.
    ///
    /// Story 1.9 covers only the GATE (is the subject really in this photo). The composition sweet-spot
    /// curve, rule-of-thirds placement and peak-timing falloff are Story 1.10 and belong here too when
    /// they land — this asset is deliberately the one place that grows.
    /// </summary>
    [CreateAssetMenu(menuName = "CameraGame/Grading/Grading Config", fileName = "GradingConfig")]
    public class GradingConfig : ScriptableObject
    {
        [Header("Coverage gate")]

        [Tooltip("Fraction of the SCREEN the subject's projected bounding box must fill for the shot to " +
                 "count at all. The GDD's design value is ~8% (0.08). Below this the shot is a miss no " +
                 "matter how well framed it is. Screen-space, so this is resolution-independent — but note " +
                 "it is measured on an axis-aligned box, which over-reports a tall figure standing at an " +
                 "angle. Tune it against the on-screen debug box, not by arithmetic.")]
        [Range(0f, 1f)] public float minCoverage = 0.08f;

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

        /// <summary>Occlusion samples, guaranteed usable regardless of what is serialized in the asset, and
        /// capped at the number of distinct sample points that actually exist.</summary>
        public int SafeOcclusionSamples =>
            Mathf.Clamp(occlusionSamples, 1, ShotGrader.MaxOcclusionSamples);

        /// <summary>Visible-fraction threshold, guaranteed in [0,1] and never NaN.</summary>
        public float SafeMinVisibleSamples => Clamp01Finite(minVisibleSamples, 0f);

        /// <summary>
        /// Coverage threshold, guaranteed in [0,1] and never NaN — this was the one tunable the grader read
        /// raw. It matters because <c>[Range]</c> and <see cref="OnValidate"/> are BOTH editor-only, and this
        /// project authors ScriptableObject assets as hand-written YAML (Unity MCP cannot create custom SO
        /// assets), which passes through neither. A NaN threshold makes <c>coverage &lt; minCoverage</c> false
        /// forever, disabling the coverage gate with a completely clean console.
        /// Falls back to the GDD's design value rather than 0, so a corrupt asset fails CLOSED (gate active)
        /// instead of open (everything counts).
        /// </summary>
        public float SafeMinCoverage => Clamp01Finite(minCoverage, DefaultMinCoverage);

        /// <summary>The GDD's design value for the coverage gate (line 199) — the fallback for a bad asset.</summary>
        private const float DefaultMinCoverage = 0.08f;

        /// <summary>Clamps to [0,1], substituting <paramref name="fallback"/> for NaN/Infinity. Mathf.Clamp01
        /// alone is not enough: it returns NaN for NaN, so the bad value survives every guard downstream.</summary>
        private static float Clamp01Finite(float value, float fallback) =>
            float.IsNaN(value) || float.IsInfinity(value) ? fallback : Mathf.Clamp01(value);

        /// <summary>
        /// Reports authoring mistakes that would silently break grading rather than loudly fail it — the
        /// project's standing "fail-soft must not mean invisible" rule, which Story 1.8's review added
        /// after a <c>cueRadius</c> of 0 produced total silence with a completely clean console.
        /// Called once at Awake by the capture controller; returns false when everything is sane.
        /// </summary>
        public bool TryGetConfigProblem(out string problem)
        {
            // Non-finite first: NaN fails every comparison below, so without this check a NaN threshold
            // reports "everything is fine" and then disables its gate at runtime.
            if (float.IsNaN(minCoverage) || float.IsInfinity(minCoverage))
            {
                problem = $"minCoverage is {minCoverage}, which is not a usable number — the coverage gate " +
                          $"would never reject anything. Falling back to {DefaultMinCoverage:P0}.";
                return true;
            }

            if (float.IsNaN(minVisibleSamples) || float.IsInfinity(minVisibleSamples))
            {
                problem = $"minVisibleSamples is {minVisibleSamples}, which is not a usable number — the " +
                          "occlusion gate would never reject anything. Falling back to 0.";
                return true;
            }

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

            if (minCoverage <= 0f)
            {
                problem = "minCoverage is 0 — the frame-coverage gate is disabled, so a subject one pixel " +
                          "across counts as captured.";
                return true;
            }

            if (minCoverage >= 1f)
            {
                problem = "minCoverage is 1 — the subject would have to fill the entire screen, so every " +
                          "shot will fail.";
                return true;
            }

            // Symmetry with the two minCoverage checks above: `visible < 0f` is false even when EVERY sample
            // is blocked, so a zero threshold turns the occlusion gate off entirely. This was the only one of
            // the four boundaries that went unwarned, which made the omission read as deliberate.
            if (minVisibleSamples <= 0f)
            {
                problem = "minVisibleSamples is 0 — the occlusion gate is disabled, so a subject completely " +
                          "hidden behind a wall still counts as captured.";
                return true;
            }

            problem = null;
            return false;
        }

        private void OnValidate()
        {
            // Through the Safe* accessors so a NaN typed into the Inspector is repaired rather than clamped
            // to another NaN (Mathf.Clamp01(NaN) is NaN).
            minCoverage = SafeMinCoverage;
            occlusionSamples = SafeOcclusionSamples;
            minVisibleSamples = SafeMinVisibleSamples;
        }
    }
}
