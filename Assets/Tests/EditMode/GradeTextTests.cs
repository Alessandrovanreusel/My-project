using System;
using System.Globalization;
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

        // ⚠️ THE CONTRADICTION THE 2026-08-07 CODE REVIEW FOUND, PINNED.
        //
        // TimingAdvice used to test `Abs(offset) < 0.1f` with a hardcoded tenth of a second that knew nothing
        // about GradingConfig.timingFullSeconds. The shipped config awards a flat Timing01 = 1 anywhere
        // inside ±0.5s, so the readout printed "timing 100%" on one line and "0.2s early — wait for it" on
        // the next: the player was told to fix the axis that cost them nothing. Reproduced against the real
        // config at the time — 7 of 12 sampled offsets contradicted themselves.
        //
        // Written across the whole band rather than at one offset, because the bug was invisible at 0.0
        // (where every previous test and every rig scenario sat) and appeared everywhere else inside it.
        [TestCase(0.10f)]
        [TestCase(0.15f)]
        [TestCase(0.25f)]
        [TestCase(0.50f)]
        [TestCase(-0.15f)]
        [TestCase(-0.49f)]
        public void TimingAdvice_NeverTellsThePlayerToFixATimingThatScoredFullMarks(float offset)
        {
            // Timing01 = 1 is what the grader hands over for any offset inside timingFullSeconds.
            string advice = GradeText.TimingAdvice(
                ShotGrade.Scored(1f, 0.94f, 1f, StarScale.Default, "TownDrunk", offset));

            Assert.IsFalse(advice.Contains("early"),
                $"'{advice}' tells the player to wait, beneath a line reading 'timing 100%'");
            Assert.IsFalse(advice.Contains("late"),
                $"'{advice}' tells the player to shoot sooner, beneath a line reading 'timing 100%'");
        }

        // The other half of the same contract: the fix must not have silenced genuinely useful advice.
        // An offset OUTSIDE the full-marks band still has to name the direction the player was wrong.
        [TestCase(0.6f, 0.99f, "early")]
        [TestCase(1.4f, 0.35f, "early")]
        [TestCase(-1.4f, 0.35f, "late")]
        public void TimingAdvice_StillNamesTheDirection_WhenTimingDidNotScoreFullMarks(
            float offset, float timing01, string expectedWord)
        {
            string advice = GradeText.TimingAdvice(
                ShotGrade.Scored(1f, 0.94f, timing01, StarScale.Default, "TownDrunk", offset));

            StringAssert.Contains(expectedWord, advice);
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

        // =====================================================================================================
        // Story 1.19 — "prints 0 %" decides the red, so the printed text has to be pinned.
        // =====================================================================================================

        // ⚠️ AC2's FOUR CASES, THROUGH THE REAL FACTORY (never a hand-built struct): 0, 0.4 %, 0.6 % and −0.
        // The strings are what this runtime prints — Mono's percent pattern is "n %", with a space, measured
        // 2026-11-15 — and 0.6 % printing "1 %" is the case a threshold like `< 0.01f` gets wrong.
        [TestCase(0f, "0 %", true)]
        [TestCase(0.004f, "0 %", true)]
        [TestCase(0.006f, "1 %", false)]
        public void Percent_TheAcCasesThroughTheRealFactory(float timing01, string printed, bool zero)
        {
            ShotGrade g = ShotGrade.Scored(1f, 1f, timing01, StarScale.Default, "TownDrunk", -2f);

            Assert.AreEqual(printed, GradeText.Percent(g.Percent01));
            Assert.AreEqual(zero, GradeText.PrintsZero(g.Percent01));
            Assert.AreEqual(1, g.Stars, "every one of these is a 1-star shot; colour is what tells them apart");
        }

        // ⚠️ −0 IS A REAL INPUT, AND A TEST OF IT MUST PROVE IT IS −0. Mathf.Clamp01(-0f) keeps the sign bit,
        // so Scored(1, 1, -0f) carries Percent01 == −0 — but −0 == +0 in C#, so a fixture that silently
        // became +0 would pass every assertion below and test nothing. The sign bit is checked FIRST.
        [Test]
        public void Percent_NegativeZero_PrintsZeroWithNoMinusSign()
        {
            ShotGrade g = ShotGrade.Scored(1f, 1f, -0f, StarScale.Default, "TownDrunk", -2f);

            Assert.IsTrue(IsNegativeZero(g.Percent01),
                "fixture precondition: the factory must carry −0 through, or this test is testing +0");
            Assert.AreEqual("0 %", GradeText.Percent(g.Percent01));
            Assert.IsTrue(GradeText.PrintsZero(g.Percent01));
        }

        // Contract: the readout never states a sign, a NaN or more than 100 %, whatever reaches it.
        [TestCase(float.NaN, "0 %")]
        [TestCase(float.NegativeInfinity, "0 %")]
        [TestCase(-1f, "0 %")]
        [TestCase(-0.004f, "0 %")]
        [TestCase(-0f, "0 %")]
        [TestCase(float.PositiveInfinity, "100 %")]
        [TestCase(1.7f, "100 %")]
        public void Percent_NeverPrintsAMinusSignOrAnImpossibleNumber(float raw, string printed)
        {
            string text = GradeText.Percent(raw);

            Assert.AreEqual(printed, text);
            Assert.IsFalse(text.Contains("-"), $"'{text}' states a negative percentage");
        }

        // ⚠️ THE CULTURE PIN. {x:P0} follows the player's locale, and Mono's fr-FR prints U+066A, the Arabic
        // percent sign — on the player's screen, in a font that may not have the glyph. Percent is pinned to
        // the invariant culture, so the output must not move whatever the thread's culture is.
        [TestCase("en-US")]
        [TestCase("fr-FR")]
        [TestCase("")]
        public void Percent_DoesNotFollowThePlayersLocale(string culture)
        {
            CultureInfo previous = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = culture.Length == 0
                    ? CultureInfo.InvariantCulture
                    : new CultureInfo(culture);

                Assert.AreEqual("0 %", GradeText.Percent(0f));
                Assert.AreEqual("37 %", GradeText.Percent(0.37f));
                Assert.AreEqual("100 %", GradeText.Percent(1f));
                Assert.IsTrue(GradeText.PrintsZero(0.004f));
                Assert.IsFalse(GradeText.PrintsZero(0.006f));
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
            }
        }

        // The agreement half lives in GradeHudConfigTests (ColorFor_IsRedExactlyWhenTheReadoutPrintsZero),
        // because the colour is a UI decision and Grading must not reference UI.

        internal static bool IsNegativeZero(float f) =>
            f == 0f && (BitConverter.ToInt32(BitConverter.GetBytes(f), 0) & int.MinValue) != 0;
    }
}
