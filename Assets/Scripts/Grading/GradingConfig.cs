using UnityEngine;

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
                 "which fails a 95%-visible subject standing behind a lamppost. 5 is a good default.")]
        [Range(1, 16)] public int occlusionSamples = 5;

        [Tooltip("Fraction of those sample points that must be unblocked for the subject to count as " +
                 "visible. 0.2 = 'at least a fifth of him is showing'. 1.0 demands a completely " +
                 "unobstructed view, which is harsher than it sounds once fences and railings exist.")]
        [Range(0f, 1f)] public float minVisibleSamples = 0.2f;

        /// <summary>Occlusion samples, guaranteed usable regardless of what is serialized in the asset.</summary>
        public int SafeOcclusionSamples => Mathf.Clamp(occlusionSamples, 1, 16);

        /// <summary>Visible-fraction threshold, guaranteed in [0,1].</summary>
        public float SafeMinVisibleSamples => Mathf.Clamp01(minVisibleSamples);

        /// <summary>
        /// Reports authoring mistakes that would silently break grading rather than loudly fail it — the
        /// project's standing "fail-soft must not mean invisible" rule, which Story 1.8's review added
        /// after a <c>cueRadius</c> of 0 produced total silence with a completely clean console.
        /// Called once at Awake by the capture controller; returns false when everything is sane.
        /// </summary>
        public bool TryGetConfigProblem(out string problem)
        {
            if (occluderMask.value == 0)
            {
                problem = "occluderMask is empty (Nothing) — no geometry can ever block the view, so the " +
                          "occlusion half of the subject gate will pass every shot.";
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

            problem = null;
            return false;
        }

        private void OnValidate()
        {
            minCoverage = Mathf.Clamp01(minCoverage);
            occlusionSamples = SafeOcclusionSamples;
            minVisibleSamples = SafeMinVisibleSamples;
        }
    }
}
