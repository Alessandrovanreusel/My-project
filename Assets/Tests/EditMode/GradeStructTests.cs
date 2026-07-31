using NUnit.Framework;
using UnityEngine;
using CameraGame.Grading;

namespace CameraGame.Tests
{
    /// <summary>
    /// Regression pins for <see cref="StarScale"/> and <see cref="ShotGrade"/> — the two pure-data structs
    /// the grade travels on.
    ///
    /// ⚠️ WHAT THESE TESTS ARE, AND WHAT THEY ARE NOT. CLAUDE.md is explicit that assertions must not
    /// encode expected results, because a test written from what the author already believed goes green
    /// while the feature is wrong. That rule is about whether the GAME IS GOOD — whether a shot reads as a
    /// good photograph, whether the star thresholds match Alexv's judgement. Nothing here tries to answer
    /// that; the photo shoot answered it, by producing photographs a human looked at.
    ///
    /// What these pin is narrower and worth having: each one asserts an invariant the SHIPPED CODE'S OWN
    /// DOC-COMMENT already states as a contract. No test below was written from a guess about what the
    /// grader ought to do. When one fails it means the code stopped honouring something it promises in
    /// writing — which is exactly the failure a rig cannot catch, because a rig re-measures the world and
    /// reports what it finds rather than noticing that a written guarantee quietly stopped holding.
    /// </summary>
    public class StarScaleTests
    {
        // Contract: "The star rating for a normalized grade" — 5★ at/above Five, then 4/3/2, floor of 1.
        // The boundaries are inclusive (>=), which matters: a grade landing exactly on the threshold must
        // earn the higher rating, not the lower one.
        [TestCase(1.00f, 5)]
        [TestCase(0.90f, 5)]   // exactly on the 5★ boundary
        [TestCase(0.8999f, 4)]
        [TestCase(0.70f, 4)]
        [TestCase(0.45f, 3)]
        [TestCase(0.20f, 2)]
        [TestCase(0.1999f, 1)]
        [TestCase(0.00f, 1)]
        public void StarsFor_MapsGradeToRating(float grade, int expected)
        {
            Assert.AreEqual(expected, StarScale.Default.StarsFor(grade));
        }

        // Contract: "NaN reads as the floor rather than throwing or propagating."
        [Test]
        public void StarsFor_NaN_IsOneStar()
        {
            Assert.AreEqual(1, StarScale.Default.StarsFor(float.NaN));
        }

        // Contract: "the GDD's scale starts at one star and there is no 0★." Nothing may produce 0 or 6.
        [TestCase(-5f)]
        [TestCase(5f)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(float.PositiveInfinity)]
        public void StarsFor_IsAlwaysBetweenOneAndFive(float grade)
        {
            int stars = StarScale.Default.StarsFor(grade);
            Assert.That(stars, Is.InRange(1, 5));
        }

        // Contract: the shipped tuning "moves x_time_peak_start from 5★ to 4★". That decision came from
        // Alexv judging two photographs, and it is the reason the struct exists at all — so the boundary
        // is pinned here. If someone retunes it, this failing is the point: it should be a decision.
        [Test]
        public void Default_FiveStarBoundary_IsNinety()
        {
            Assert.That(StarScale.Default.Five, Is.EqualTo(0.90f).Within(1e-6f));
            Assert.AreEqual(4, StarScale.Default.StarsFor(0.85f), "0.85 must be 4★, not 5★");
            Assert.AreEqual(5, StarScale.Default.StarsFor(1.00f), "a perfect shot must still be 5★");
        }
    }

    public class ShotGradeTests
    {
        // Contract: "The blend is MULTIPLICATIVE (composition x timing)... a beautifully framed shot two
        // seconds late genuinely is not the shot."
        [TestCase(1.0f, 1.0f, 1.00f)]
        [TestCase(1.0f, 0.5f, 0.50f)]
        [TestCase(0.8f, 0.5f, 0.40f)]
        [TestCase(1.0f, 0.0f, 0.00f)]
        public void Scored_BlendsCompositionTimesTiming(float comp, float timing, float expected)
        {
            var g = ShotGrade.Scored(1f, comp, timing, StarScale.Default, "TownDrunk");
            Assert.That(g.Percent01, Is.EqualTo(expected).Within(1e-5f));
        }

        // Contract: "Mathf.Clamp01(NaN) is NaN, so clamping alone would let one NaN sub-score propagate
        // into the HUD and the gallery — the same guard applied to all four axes."
        [Test]
        public void Scored_NaNAxis_DoesNotPropagate()
        {
            var g = ShotGrade.Scored(float.NaN, float.NaN, float.NaN, StarScale.Default, "TownDrunk");

            Assert.IsFalse(float.IsNaN(g.Percent01), "Percent01 must never be NaN");
            Assert.IsFalse(float.IsNaN(g.Subject01), "Subject01 must never be NaN");
            Assert.IsFalse(float.IsNaN(g.Composition01), "Composition01 must never be NaN");
            Assert.IsFalse(float.IsNaN(g.Timing01), "Timing01 must never be NaN");
        }

        // Contract: every axis is clamped into [0,1] on the way in.
        [Test]
        public void Scored_OutOfRangeAxes_AreClampedIntoUnitRange()
        {
            var g = ShotGrade.Scored(9f, 9f, -9f, StarScale.Default, "TownDrunk");

            Assert.That(g.Subject01, Is.InRange(0f, 1f));
            Assert.That(g.Composition01, Is.InRange(0f, 1f));
            Assert.That(g.Timing01, Is.InRange(0f, 1f));
            Assert.That(g.Percent01, Is.InRange(0f, 1f));
        }

        // Contract: "A miss (0%) still reads as 1 star... which is exactly why MissReason exists."
        // IsMiss and Counted must be able to tell a miss apart from a merely weak shot.
        [Test]
        public void Missed_IsZeroPercentOneStarAndFlaggedAsMiss()
        {
            var miss = ShotGrade.Missed(GradeMiss.Occluded, "TownDrunk");

            Assert.That(miss.Percent01, Is.EqualTo(0f).Within(1e-6f));
            Assert.AreEqual(1, miss.Stars);
            Assert.IsTrue(miss.IsMiss);
            Assert.IsFalse(miss.Counted);
            Assert.AreEqual(GradeMiss.Occluded, miss.MissReason);
        }

        // Contract: "A MISS may still carry an id, and should: 'the drunk, behind a wall' is more truthful
        // than 'nobody'." Only the gates that run before a subject is known leave it empty.
        [Test]
        public void Missed_CarriesSubjectIdWhenOneWasMeasured()
        {
            Assert.AreEqual("TownDrunk", ShotGrade.Missed(GradeMiss.Occluded, "TownDrunk").SubjectId);
            Assert.IsTrue(ShotGrade.Missed(GradeMiss.Occluded, "TownDrunk").HasSubject);

            // NoConfig runs before a subject is resolved, so it legitimately has none.
            Assert.IsFalse(ShotGrade.Missed(GradeMiss.NoConfig).HasSubject);
        }

        // Contract: a weak-but-counted shot is NOT a miss, even though it also reads 1★. This is the
        // distinction the whole MissReason field exists for, so it is pinned directly.
        [Test]
        public void WeakButCountedShot_IsNotAMiss()
        {
            var weak = ShotGrade.Scored(1f, 0.1f, 0.1f, StarScale.Default, "TownDrunk");

            Assert.AreEqual(1, weak.Stars, "a 1% shot reads 1 star, same as a miss");
            Assert.IsFalse(weak.IsMiss, "...but it is not a miss");
            Assert.IsTrue(weak.Counted);
        }

        // ⚠️ THE DOCUMENTED TRAP. ShotGrade.SubjectId's comment says outright: "NOT ALWAYS NON-NULL,
        // DESPITE THE CONSTRUCTOR NORMALIZING... default(T) on a struct zero-initialises the memory and
        // runs NO constructor, so the field is null." It names Epic 5 as where this would throw, because
        // CapturedShot instructs the next author to map this field into a JSON DTO.
        //
        // This is the single most valuable test in the file: it is a live bug waiting for a future story,
        // the code documents it rather than fixing it, and HasSubject is the sanctioned way past it.
        [Test]
        public void DefaultGrade_HasSubjectIsSafeEvenThoughSubjectIdIsNull()
        {
            ShotGrade zeroed = default;

            Assert.IsNull(zeroed.SubjectId, "documented: default(ShotGrade) runs no constructor");
            Assert.DoesNotThrow(() => { bool _ = zeroed.HasSubject; }, "HasSubject must stay null-safe");
            Assert.IsFalse(zeroed.HasSubject);
        }

        // Same trap, reached the way a real caller would hit it: an unassigned array slot.
        [Test]
        public void GradeArray_UnassignedEntries_AreSafeToInspect()
        {
            var roll = new ShotGrade[3];

            foreach (var g in roll)
            {
                Assert.DoesNotThrow(() => { bool _ = g.HasSubject; });
                Assert.IsFalse(g.HasSubject);
                Assert.IsFalse(g.Counted, "a zeroed grade must not claim to be a counted shot");
            }
        }

        // Contract: "SubjectId ... Normalized here and nowhere else, so no reader ever has to defend
        // itself against null" — for grades that DO go through a constructor.
        [Test]
        public void ConstructedGrade_NullSubjectId_BecomesEmptyNotNull()
        {
            var g = ShotGrade.Scored(1f, 1f, 1f, StarScale.Default, null);

            Assert.IsNotNull(g.SubjectId);
            Assert.AreEqual(string.Empty, g.SubjectId);
            Assert.IsFalse(g.HasSubject);
        }

        // Contract: "True while this is a temporary stand-in (Story 1.5) until real grading exists."
        // A placeholder must not read as a counted shot, or the HUD would score it.
        [Test]
        public void Placeholder_IsNeitherCountedNorMiss()
        {
            var p = ShotGrade.Placeholder;

            Assert.IsTrue(p.IsPlaceholder);
            Assert.IsFalse(p.Counted, "a placeholder is not a real score");
            Assert.IsFalse(p.IsMiss, "...and Unevaluated is not a miss either");
        }

        // Contract: "Stars = Mathf.Clamp(stars, 1, 5)". Guards against a StarScale authored so badly that
        // StarsFor could ever return outside the ladder.
        [Test]
        public void Stars_AreAlwaysWithinTheLadder()
        {
            var absurd = new StarScale(float.NaN, -3f, 99f, float.NegativeInfinity);
            var g = ShotGrade.Scored(1f, 1f, 1f, absurd, "TownDrunk");

            Assert.That(g.Stars, Is.InRange(1, 5));
        }
    }
}
