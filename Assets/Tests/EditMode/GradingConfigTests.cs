using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CameraGame.Grading;

namespace CameraGame.Tests
{
    /// <summary>
    /// Regression pins for <see cref="GradingConfig"/>'s self-defence: the Safe* accessors and the two
    /// resolvers that hand the grader a mutually-consistent set of numbers.
    ///
    /// This is the highest-value thing in the project to pin, for a reason the config's own comments spell
    /// out: <c>[Range]</c>/<c>[Min]</c> and <c>OnValidate</c> are ALL editor-only, and this project
    /// hand-authors ScriptableObject assets as YAML — so an out-of-range or NaN value reaches the grader
    /// intact, and "silent misconfiguration is this project's recurring failure mode".
    ///
    /// ⚠️ NOTE HOW THE FALLBACK TESTS ARE WRITTEN. They never assert "the fallback is 0.20". They assert
    /// that a corrupted field resolves to whatever a PRISTINE config resolves to. That is the documented
    /// contract ("falls back to the design value"), and writing it this way means retuning a design number
    /// does not break the test — only breaking the fallback MECHANISM does. Asserting the literal would
    /// have been exactly the "encoding expected results" CLAUDE.md warns about.
    /// </summary>
    public class GradingConfigTests
    {
        private readonly List<GradingConfig> _made = new List<GradingConfig>();

        private GradingConfig NewConfig()
        {
            var c = ScriptableObject.CreateInstance<GradingConfig>();
            _made.Add(c);
            return c;
        }

        /// <summary>A config that should report no problem: shipped defaults plus an occluder mask, which
        /// is the one field with no sensible default (an empty mask means nothing can ever block).</summary>
        private GradingConfig NewValidConfig()
        {
            var c = NewConfig();
            c.occluderMask = 1;   // Default layer only — deliberately excludes the Subject layer.
            return c;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var c in _made)
                if (c != null) Object.DestroyImmediate(c);
            _made.Clear();
        }

        // --- Safe accessors: non-finite input falls back ------------------------------------------------
        //
        // Contract: "NaN is the dangerous one: it fails every comparison, so `x < NaN` is false forever and
        // the check it guards silently stops existing. (Note Mathf.Clamp01(NaN) is NaN, which is why plain
        // clamping is not enough.)"

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void SafeAccessors_NonFiniteInput_FallsBackToTheDesignValue(float bad)
        {
            var pristine = NewConfig();
            var corrupt = NewConfig();

            corrupt.minSubjectHeight = bad;
            corrupt.minVisibleSamples = bad;
            corrupt.prominenceIdealMin = bad;
            corrupt.prominenceIdealMax = bad;
            corrupt.prominenceFalloffBelow = bad;
            corrupt.prominenceFalloffAbove = bad;
            corrupt.centreWeight = bad;
            corrupt.cutoffWeight = bad;
            corrupt.timingFullSeconds = bad;
            corrupt.timingZeroSeconds = bad;
            corrupt.starThreshold5 = bad;
            corrupt.starThreshold4 = bad;
            corrupt.starThreshold3 = bad;
            corrupt.starThreshold2 = bad;

            Assert.AreEqual(pristine.SafeMinSubjectHeight, corrupt.SafeMinSubjectHeight, 1e-6f);
            Assert.AreEqual(pristine.SafeMinVisibleSamples, corrupt.SafeMinVisibleSamples, 1e-6f);
            Assert.AreEqual(pristine.SafeProminenceIdealMin, corrupt.SafeProminenceIdealMin, 1e-6f);
            Assert.AreEqual(pristine.SafeProminenceIdealMax, corrupt.SafeProminenceIdealMax, 1e-6f);
            Assert.AreEqual(pristine.SafeProminenceFalloffBelow, corrupt.SafeProminenceFalloffBelow, 1e-6f);
            Assert.AreEqual(pristine.SafeProminenceFalloffAbove, corrupt.SafeProminenceFalloffAbove, 1e-6f);
            Assert.AreEqual(pristine.SafeCentreWeight, corrupt.SafeCentreWeight, 1e-6f);
            Assert.AreEqual(pristine.SafeCutoffWeight, corrupt.SafeCutoffWeight, 1e-6f);
            Assert.AreEqual(pristine.SafeTimingFullSeconds, corrupt.SafeTimingFullSeconds, 1e-6f);
            Assert.AreEqual(pristine.SafeTimingZeroSeconds, corrupt.SafeTimingZeroSeconds, 1e-6f);
            Assert.AreEqual(pristine.SafeStarThreshold5, corrupt.SafeStarThreshold5, 1e-6f);
            Assert.AreEqual(pristine.SafeStarThreshold4, corrupt.SafeStarThreshold4, 1e-6f);
            Assert.AreEqual(pristine.SafeStarThreshold3, corrupt.SafeStarThreshold3, 1e-6f);
            Assert.AreEqual(pristine.SafeStarThreshold2, corrupt.SafeStarThreshold2, 1e-6f);
        }

        // No Safe* accessor may ever hand the grader a NaN, whatever was authored.
        [Test]
        public void SafeAccessors_NeverReturnNonFinite()
        {
            var c = NewConfig();
            c.minSubjectHeight = float.NaN;
            c.prominenceFalloffAbove = float.PositiveInfinity;
            c.timingZeroSeconds = float.NegativeInfinity;
            c.starThreshold5 = float.NaN;

            foreach (float v in new[]
            {
                c.SafeMinSubjectHeight, c.SafeMinVisibleSamples, c.SafeProminenceIdealMin,
                c.SafeProminenceIdealMax, c.SafeProminenceFalloffBelow, c.SafeProminenceFalloffAbove,
                c.SafeCentreWeight, c.SafeCutoffWeight, c.SafeTimingFullSeconds, c.SafeTimingZeroSeconds,
                c.SafeStarThreshold5, c.SafeStarThreshold4, c.SafeStarThreshold3, c.SafeStarThreshold2
            })
            {
                Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), $"non-finite value leaked: {v}");
            }
        }

        // Contract: the gate "falls back to the design value rather than 0, so a corrupt asset fails CLOSED
        // (gate active) instead of open (everything counts)." The direction of that failure is the point.
        [Test]
        public void SafeMinSubjectHeight_CorruptAsset_FailsClosedNotOpen()
        {
            var c = NewConfig();
            c.minSubjectHeight = float.NaN;

            Assert.Greater(c.SafeMinSubjectHeight, 0f,
                "a NaN gate must not resolve to 0 — that would count a one-pixel figure as captured");
        }

        // --- Safe accessors: out-of-range input clamps --------------------------------------------------

        [Test]
        public void SafeAccessors_OutOfRangeInput_IsClampedIntoRange()
        {
            var c = NewConfig();
            c.minSubjectHeight = 5f;
            c.minVisibleSamples = -5f;
            c.centreWeight = 9f;
            c.cutoffWeight = -9f;
            c.occlusionSamples = 9999;

            Assert.That(c.SafeMinSubjectHeight, Is.InRange(0f, 1f));
            Assert.That(c.SafeMinVisibleSamples, Is.InRange(0f, 1f));
            Assert.That(c.SafeCentreWeight, Is.InRange(0f, 1f));
            Assert.That(c.SafeCutoffWeight, Is.InRange(0f, 1f));
            Assert.That(c.SafeOcclusionSamples, Is.InRange(1, ShotGrader.MaxOcclusionSamples));
        }

        // Contract: "capped at the number of distinct sample points that actually exist (centre + 8 box
        // corners + 6 face centres)" — and never below 1, which would test nothing at all.
        [TestCase(0, 1)]
        [TestCase(-50, 1)]
        [TestCase(1, 1)]
        public void SafeOcclusionSamples_NeverDropsBelowOne(int authored, int expected)
        {
            var c = NewConfig();
            c.occlusionSamples = authored;
            Assert.AreEqual(expected, c.SafeOcclusionSamples);
        }

        // --- Resolvers ----------------------------------------------------------------------------------
        //
        // Contract: "Resolves the prominence trapezoid into four points guaranteed to satisfy
        // falloffBelow < idealMin <= idealMax < falloffAbove." Asserted as a PROPERTY over a spread of
        // hostile inputs rather than against hand-computed numbers — the guarantee is what matters, and a
        // property holds for inputs nobody thought to enumerate.

        private static IEnumerable<float[]> HostileProminenceInputs()
        {
            // { idealMin, idealMax, falloffBelow, falloffAbove }
            yield return new[] { 0.45f, 0.78f, 0.15f, 1.15f };            // the shipped values
            yield return new[] { 0.9f, 0.1f, 0.5f, 0.05f };               // fully inverted
            yield return new[] { 0.5f, 0.5f, 0.5f, 0.5f };                // everything identical
            yield return new[] { 0f, 0f, 0f, 0f };                        // all zero
            yield return new[] { 1f, 1f, 1f, 1f };                        // all one
            yield return new[] { float.NaN, float.NaN, float.NaN, float.NaN };
            yield return new[] { -5f, 5f, -5f, 5f };                      // way out of range both ways
            yield return new[] { float.PositiveInfinity, float.NegativeInfinity, 0.3f, 0.9f };
        }

        [Test]
        public void ResolveProminenceCurve_AlwaysProducesAUsableTrapezoid(
            [ValueSource(nameof(HostileProminenceInputs))] float[] input)
        {
            var c = NewConfig();
            c.prominenceIdealMin = input[0];
            c.prominenceIdealMax = input[1];
            c.prominenceFalloffBelow = input[2];
            c.prominenceFalloffAbove = input[3];

            c.ResolveProminenceCurve(out float below, out float min, out float max, out float above);

            Assert.IsFalse(float.IsNaN(below) || float.IsNaN(min) || float.IsNaN(max) || float.IsNaN(above),
                "a resolved curve must never contain NaN");

            // The documented ordering. Strict on the outside, non-strict in the middle.
            Assert.Less(below, min, "falloffBelow must be strictly below idealMin");
            Assert.LessOrEqual(min, max, "idealMin must not exceed idealMax");
            Assert.Less(max, above, "falloffAbove must be strictly above idealMax");

            // Non-zero widths are what stop InverseLerp dividing by zero downstream.
            Assert.Greater(min - below, 0f, "the small side of the curve must have width");
            Assert.Greater(above - max, 0f, "the large side of the curve must have width");
        }

        // Contract: "falloffBelow may come back NEGATIVE when the sweet spot starts at 0 — that is correct
        // and means 'prominence never reaches zero on the small side', not an error." Pinned so a future
        // 'tidy-up' that clamps it to 0 reintroduces the divide-by-zero this guards.
        [Test]
        public void ResolveProminenceCurve_NegativeFalloffBelow_IsAllowed()
        {
            var c = NewConfig();
            c.prominenceIdealMin = 0f;
            c.prominenceIdealMax = 0.5f;
            c.prominenceFalloffBelow = 0f;

            c.ResolveProminenceCurve(out float below, out float min, out _, out _);

            Assert.Less(below, min);
            Assert.Less(below, 0f, "documented: a sweet spot starting at 0 pushes falloffBelow negative");
        }

        // Contract: "Resolves the timing window so that zero > full always holds. Without this a
        // timingZeroSeconds <= timingFullSeconds asset turns the smooth falloff into a cliff."
        // The 70/90 case is called out by name in the config: it used to pass validation and then silently
        // resolve to 60 / 60.001, which is the exact cliff the check exists to prevent.
        [TestCase(0.5f, 2f)]
        [TestCase(2f, 0.5f)]        // inverted
        [TestCase(1f, 1f)]          // identical
        [TestCase(70f, 90f)]        // both above the 60s ceiling — the named regression
        [TestCase(0f, 0f)]
        [TestCase(float.NaN, float.NaN)]
        [TestCase(-5f, -5f)]
        public void ResolveTimingWindow_ZeroIsAlwaysStrictlyAboveFull(float full, float zero)
        {
            var c = NewConfig();
            c.timingFullSeconds = full;
            c.timingZeroSeconds = zero;

            c.ResolveTimingWindow(out float resolvedFull, out float resolvedZero);

            Assert.IsFalse(float.IsNaN(resolvedFull) || float.IsNaN(resolvedZero));
            Assert.Greater(resolvedZero, resolvedFull,
                "the timing falloff must always have width, or every shot outside the peak scores 0");
        }

        // Contract: "the star boundaries as a resolved, guaranteed-DESCENDING set, so the grader can never
        // be handed a scale where a higher grade earns fewer stars."
        [Test]
        public void SafeStarScale_IsAlwaysDescending()
        {
            var c = NewConfig();
            c.starThreshold5 = 0.1f;    // deliberately upside down
            c.starThreshold4 = 0.4f;
            c.starThreshold3 = 0.7f;
            c.starThreshold2 = 0.95f;

            StarScale s = c.SafeStarScale;

            Assert.GreaterOrEqual(s.Five, s.Four);
            Assert.GreaterOrEqual(s.Four, s.Three);
            Assert.GreaterOrEqual(s.Three, s.Two);
        }

        // The consequence of the above, stated the way it actually matters: stars must be monotonic, so a
        // better photograph can never earn a lower rating than a worse one.
        [Test]
        public void SafeStarScale_StarsNeverDecreaseAsGradeIncreases()
        {
            var c = NewConfig();
            c.starThreshold5 = 0.1f;
            c.starThreshold4 = 0.4f;
            c.starThreshold3 = 0.7f;
            c.starThreshold2 = 0.95f;

            StarScale s = c.SafeStarScale;

            int previous = 0;
            for (float g = 0f; g <= 1.0001f; g += 0.01f)
            {
                int stars = s.StarsFor(g);
                Assert.GreaterOrEqual(stars, previous,
                    $"stars went DOWN as the grade rose, at grade {g:0.###}");
                previous = stars;
            }
        }

        // --- Validation reporting -----------------------------------------------------------------------

        // The shipped defaults must be internally consistent. If this fails, the tuning someone just
        // committed contradicts one of the config's own rules.
        [Test]
        public void ShippedDefaults_ReportNoProblem()
        {
            var c = NewValidConfig();

            bool hasProblem = c.TryGetConfigProblem(out string problem);

            Assert.IsFalse(hasProblem, $"the shipped defaults should be self-consistent, but: {problem}");
            Assert.IsNull(problem);
        }

        // Contract: "an empty mask means nothing can ever block, which silently passes every shot."
        [Test]
        public void EmptyOccluderMask_IsReported()
        {
            var c = NewConfig();
            c.occluderMask = 0;

            Assert.IsTrue(c.TryGetConfigProblem(out string problem));
            Assert.That(problem, Does.Contain("occluderMask"));
        }

        // The silent class of mistake the config exists to catch: none of these crash, none look wrong,
        // they just quietly cap every photograph the player will ever take.
        [Test]
        public void GateAboveTheSweetSpot_MakingFiveStarsUnreachable_IsReported()
        {
            var c = NewValidConfig();
            c.minSubjectHeight = 0.95f;          // above prominenceIdealMax
            c.prominenceIdealMax = 0.78f;

            Assert.IsTrue(c.TryGetConfigProblem(out string problem),
                "a gate above the sweet spot caps composition below 1 forever — it must be reported");
            Assert.That(problem, Is.Not.Null.And.Not.Empty);
        }

        // Contract: the out-of-range checks run BEFORE the structural ones, specifically so a clamped field
        // cannot make a structural check lie. The config names the 70/90 timing pair as the missed alarm.
        [Test]
        public void OutOfRangeTimingPair_IsReportedRatherThanSilentlyClamped()
        {
            var c = NewValidConfig();
            c.timingFullSeconds = 70f;
            c.timingZeroSeconds = 90f;

            Assert.IsTrue(c.TryGetConfigProblem(out string problem),
                "70/90 both clamp to 60 and resolve to a 1ms cliff — this must not pass silently");
            Assert.That(problem, Is.Not.Null.And.Not.Empty);
        }
    }
}
