using System;
using NUnit.Framework;
using CameraGame.Grading;

namespace CameraGame.Tests
{
    /// <summary>
    /// Regression pins for <see cref="GradeText"/> — the wording the gallery and the grade-feedback HUD
    /// SHARE (Story 1.12, Task 2).
    ///
    /// ⚠️ SAME RULE AS <c>GradeStructTests</c>: nothing here asserts that a phrase is GOOD. Whether
    /// "something was in the way — move so you can see him" reads well to a first-time player is a question
    /// for eyes, and it is handed to Alexv with photographs (AC3). What is pinned below is narrower and is
    /// exactly what a rig cannot catch: that the short set the gallery grid depends on has not drifted, and
    /// that no code path can put a developer's enum name in front of a player.
    /// </summary>
    public class GradeTextTests
    {
        // Contract: "Five glyphs, always — filled up to the rating. A fixed width so the ratings line up
        // down the grid and can be compared at a glance."
        [TestCase(0, "☆☆☆☆☆")]
        [TestCase(1, "★☆☆☆☆")]
        [TestCase(3, "★★★☆☆")]
        [TestCase(5, "★★★★★")]
        public void Stars_IsAlwaysFiveGlyphs(int stars, string expected)
        {
            Assert.AreEqual(expected, GradeText.Stars(stars));
        }

        // Contract: the glyph count is clamped, so a rating outside the ladder still lines up in the grid
        // rather than producing a ragged or negative-length string.
        [TestCase(-3)]
        [TestCase(9)]
        public void Stars_OutOfLadderRatings_StillProduceFiveGlyphs(int stars)
        {
            Assert.AreEqual(5, GradeText.Stars(stars).Length);
        }

        // ⚠️ THE LOAD-BEARING ONE. These exact strings shipped in Story 1.11 and were chosen AGAINST
        // EVIDENCE: a longer phrasing came back from a real run truncated to "missed — something in the",
        // which reads as a broken gallery. Story 1.12 moved them out of GalleryView; this pins that the
        // move was byte-for-byte, and that a future author reaching for a "better" word has to change a
        // test that says why they should not.
        [TestCase(GradeMiss.NoSubject,        "nobody there")]
        [TestCase(GradeMiss.TooSmall,         "too far away")]
        [TestCase(GradeMiss.Occluded,         "blocked")]
        [TestCase(GradeMiss.OutsideFrustum,   "out of frame")]
        [TestCase(GradeMiss.BehindCamera,     "behind you")]
        [TestCase(GradeMiss.DegenerateBounds, "nothing there")]
        [TestCase(GradeMiss.NoCamera,         "no camera")]
        [TestCase(GradeMiss.NoConfig,         "grading not set up")]
        [TestCase(GradeMiss.NoViewport,       "no viewport")]
        [TestCase(GradeMiss.Unevaluated,      "not graded")]
        public void MissShort_IsUnchangedFromWhatTheGalleryShipped(GradeMiss miss, string expected)
        {
            Assert.AreEqual(expected, GradeText.MissShort(miss));
        }

        // Contract: "EVERY GradeMiss MEMBER IS NAMED HERE ON PURPOSE. Falling through to miss.ToString()
        // would put a developer's enum name — 'DegenerateBounds' — on the player's screen."
        //
        // Written over Enum.GetValues rather than as a list, so ADDING a member to GradeMiss without
        // wording it fails this test instead of shipping silently.
        [Test]
        public void EveryMissReason_HasPlayerFacingWordsInBothLengths()
        {
            foreach (GradeMiss miss in Enum.GetValues(typeof(GradeMiss)))
            {
                if (miss == GradeMiss.None) continue;   // not a miss; nothing to word

                string shortText = GradeText.MissShort(miss);
                string longText = GradeText.MissLong(miss);

                Assert.IsFalse(string.IsNullOrEmpty(shortText), $"{miss} has no short wording");
                Assert.IsFalse(string.IsNullOrEmpty(longText), $"{miss} has no long wording");

                Assert.AreNotEqual(miss.ToString(), shortText,
                    $"{miss} falls through to the enum name — a developer's word on the player's screen");
                Assert.AreNotEqual(miss.ToString(), longText,
                    $"{miss} falls through to the enum name — a developer's word on the player's screen");
            }
        }

        // Contract: the HUD line is the one with room for the actionable half, so it should not simply be
        // the gallery's cell caption again. Pinned as "different", not as "longer by N" — the point is that
        // two distinct sets exist and neither silently became an alias of the other.
        [Test]
        public void MissLong_IsNotJustMissShort()
        {
            int differing = 0;
            foreach (GradeMiss miss in Enum.GetValues(typeof(GradeMiss)))
            {
                if (miss == GradeMiss.None) continue;
                if (GradeText.MissLong(miss) != GradeText.MissShort(miss)) differing++;
            }

            Assert.That(differing, Is.GreaterThan(0),
                "MissLong exists because the HUD has a full-width line; if it matches MissShort everywhere " +
                "it has been collapsed back into one set and the gallery's truncation guard is now the HUD's " +
                "wording budget too");
        }

        // ⚠️ AC2, AT THE STRING LAYER. TimingAdvice must say NOTHING when there is no measurement, rather
        // than "0.0s late" — a fabricated number is the defect class this whole story is about, and the
        // HUD prints whatever this returns.
        [Test]
        public void TimingAdvice_SaysNothingWhenTimingWasNeverMeasured()
        {
            Assert.AreEqual(string.Empty, GradeText.TimingAdvice(default));
            Assert.AreEqual(string.Empty, GradeText.TimingAdvice(ShotGrade.Placeholder));
            Assert.AreEqual(string.Empty,
                GradeText.TimingAdvice(ShotGrade.Missed(GradeMiss.Occluded, "TownDrunk")));
            Assert.AreEqual(string.Empty,
                GradeText.TimingAdvice(ShotGrade.Scored(1f, 1f, 0f, StarScale.Default, "TownDrunk", float.NaN)));
        }

        // Contract: "Positive is EARLY and negative is LATE (ISubject.PeakOffset)." Getting this backwards
        // would tell every player to do the opposite of the right thing, and would look perfectly plausible
        // in a screenshot — nothing else in the game states the direction.
        [Test]
        public void TimingAdvice_NamesTheDirectionThePlayerWasWrong()
        {
            string early = GradeText.TimingAdvice(
                ShotGrade.Scored(1f, 1f, 0.2f, StarScale.Default, "TownDrunk", 1.4f));
            string late = GradeText.TimingAdvice(
                ShotGrade.Scored(1f, 1f, 0.2f, StarScale.Default, "TownDrunk", -1.4f));

            StringAssert.Contains("early", early);
            StringAssert.Contains("late", late);
            Assert.AreNotEqual(early, late);
        }

        // Contract: "Inside a tenth of a second of the peak window there is nothing useful to say — and
        // rounding would print '0.0s early', which reads as a criticism of a shot that was perfectly timed."
        [TestCase(0f)]
        [TestCase(0.05f)]
        [TestCase(-0.05f)]
        public void TimingAdvice_OnThePeak_DoesNotScoldAPerfectShot(float offset)
        {
            string advice = GradeText.TimingAdvice(
                ShotGrade.Scored(1f, 1f, 1f, StarScale.Default, "TownDrunk", offset));

            Assert.IsFalse(advice.Contains("early"), $"'{advice}' criticises a shot taken on the peak");
            Assert.IsFalse(advice.Contains("late"), $"'{advice}' criticises a shot taken on the peak");
            Assert.IsFalse(string.IsNullOrEmpty(advice), "a perfectly timed shot should still be told so");
        }
    }
}
