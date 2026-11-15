using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using NUnit.Framework;
using UnityEngine;
using CameraGame.Grading;
using CameraGame.Recording;
using Object = UnityEngine.Object;

namespace CameraGame.Tests
{
    /// <summary>
    /// Story 1.16 (Task 7): a clip's grade, pinned with no scene. Pure logic only: the blend's arithmetic and its
    /// guards, peak coverage on the subject's clock at every tick alignment, the candidate and miss rules, reuse,
    /// the payload's invariants, the readout's words under foreign cultures, and the config's validator.
    ///
    /// ⚠️ NOTHING HERE PINS THE CHOSEN TUNING. The floor and the absence weight were decided by measuring recorded
    /// clips (the Grade Clip Shoot rig and its sweep); these tests use their OWN blends, or the shipped config
    /// only for the requirement the AC states ("a well-framed clip that missed the peak prints above 0 %"). Whether
    /// the ladder reads right is Alexv's human check, never a test (CLAUDE.md: do not encode expected results).
    /// </summary>
    public class ClipGradeTests
    {
        private const double Tick = 1.0 / 30.0;
        private const float Peak = 1.5f;

        private static readonly ClipBlend Mult = new ClipBlend(0.3f, 0f, ClipBlendShape.FloorTimesCoverage,
                                                               ClipFramingSource.GatedTicks, ClipMissRule.MostTime);

        private readonly List<GradingConfig> _made = new List<GradingConfig>();

        [TearDown]
        public void TearDown()
        {
            foreach (var c in _made) if (c != null) Object.DestroyImmediate(c);
            _made.Clear();
        }

        private GradingConfig NewConfig()
        {
            var c = ScriptableObject.CreateInstance<GradingConfig>();
            c.occluderMask = 1;
            _made.Add(c);
            return c;
        }

        private static ClipSample Framed(int lifecycle, float actorTime, float comp = 0.9f, string id = "TownDrunk") =>
            new ClipSample
            {
                Lifecycle = lifecycle, SubjectId = id, Counted = true, Miss = GradeMiss.None, Composition01 = comp,
                ActorTime = actorTime, PeakDuration = Peak, AtPeak = actorTime >= 0f && actorTime < Peak,
            };

        private static ClipSample Missed(int lifecycle, float actorTime, GradeMiss miss, string id = "TownDrunk") =>
            new ClipSample
            {
                Lifecycle = lifecycle, SubjectId = id, Counted = false, Miss = miss, Composition01 = 0f,
                ActorTime = actorTime, PeakDuration = Peak, AtPeak = actorTime >= 0f && actorTime < Peak,
            };

        /// <summary>One tick of <paramref name="seconds"/> holding the given samples.</summary>
        private static void TickOf(ClipGradeBuilder b, double seconds, params ClipSample[] samples)
        {
            b.BeginTick(seconds);
            foreach (var s in samples) b.Add(in s);
            b.EndTick();
        }

        /// <summary>A clip of one lifecycle, its clock running from <paramref name="from"/> to <paramref name="to"/> at
        /// 30 ticks a second, every tick framed at <paramref name="comp"/>.</summary>
        private static ClipGradeBuilder FramedClip(float from, float to, float comp = 0.9f, int lifecycle = 1)
        {
            var b = new ClipGradeBuilder();
            int n = Mathf.RoundToInt((to - from) * 30f);
            for (int i = 0; i <= n; i++) TickOf(b, Tick, Framed(lifecycle, from + i / 30f, comp));
            return b;
        }

        // --- Coverage, on the subject's clock -----------------------------------------------------------

        [Test]
        public void Coverage_WholeWindowFramedThroughout_ReadsExactlyOne()
        {
            ClipGrade g = FramedClip(-1f, 2.5f).Finish(in Mult, StarScale.Default);
            Assert.IsTrue(g.Counted);
            Assert.AreEqual(1f, g.PeakCoverage01, 0f, "the whole window, framed, must read 100 % with no tick lost");
            Assert.AreEqual(0f, g.PeakBeforeClip01);
            Assert.AreEqual(0f, g.PeakAfterClip01);
        }

        // ⚠️ THE QUANTISATION CASE (decision 1c). Counting 1/30-s ticks inside the window reads 44, 45 or 46 of 45
        // depending on where the phase boundary falls between ticks and on the frame rate; on the subject's clock
        // the stretches join end to end and the whole window reads exactly 1 at every alignment and every rate.
        [TestCase(0.0f, 1)]
        [TestCase(0.013f, 1)]
        [TestCase(0.029f, 1)]
        [TestCase(0.0f, 2)]
        [TestCase(0.017f, 3)]
        [TestCase(0.004f, 7)]
        public void Coverage_WholeWindow_IsExactAtEveryTickAlignmentAndFrameRate(float phase, int framesPerTick)
        {
            var b = new ClipGradeBuilder();
            var rng = new System.Random(1234);
            double t = -0.8 + phase;
            while (t < Peak + 0.8)
            {
                // Irregular frame times, several slots per tick at low rates: a real editor's timing.
                double dt = framesPerTick * Tick * (0.85 + 0.3 * rng.NextDouble());
                t += dt;
                TickOf(b, dt, Framed(1, (float)t));
            }
            ClipGrade g = b.Finish(in Mult, StarScale.Default);
            Assert.AreEqual(1f, g.PeakCoverage01, 1e-6f);
        }

        [Test]
        public void Coverage_FollowsTheSubjectsClock_NotTheClips()
        {
            // timeScale 0.5: the subject's clock advances half as fast as the footage. The window still reads 1, and
            // the clip held twice the footage for it.
            var b = new ClipGradeBuilder();
            for (int i = 0; i <= 240; i++) TickOf(b, Tick, Framed(1, -0.5f + i / 60f));
            ClipGrade g = b.Finish(in Mult, StarScale.Default);
            Assert.AreEqual(1f, g.PeakCoverage01, 1e-6f);
            Assert.AreEqual(241 / 30f, g.GradedSeconds, 1e-4f);
        }

        [Test]
        public void Coverage_RisesWithTheCoveredSeconds()
        {
            float last = -1f;
            foreach (float start in new[] { 1.4f, 1.1f, 0.75f, 0.3f, -0.2f })
            {
                ClipGrade g = FramedClip(start, 3f).Finish(in Mult, StarScale.Default);
                Assert.That(g.PeakCoverage01, Is.GreaterThan(last), $"start {start}");
                last = g.PeakCoverage01;
            }
            Assert.AreEqual(1f, last, 1e-6f);
        }

        [Test]
        public void Coverage_StartingPartWayIn_CreditsWhatTheClipHolds_AndNamesTheLoss()
        {
            // The clip's first tick stands for one tick's length before it (decision 1c), so a clip started at 0.75 s
            // into the window holds 0.75 + 1/30 s of it.
            ClipGrade g = FramedClip(0.75f, 3f).Finish(in Mult, StarScale.Default);
            Assert.AreEqual((Peak - 0.75f + 1f / 30f) / Peak, g.PeakCoverage01, 1e-5f);
            Assert.AreEqual((0.75f - 1f / 30f) / Peak, g.PeakBeforeClip01, 1e-5f);
            Assert.AreEqual(0f, g.PeakAfterClip01);
            Assert.AreEqual(0f, g.PeakOutOfShot01, 1e-5f);
        }

        [Test]
        public void Coverage_TheWindowPassingOutOfShot_IsHeldButNotCovered()
        {
            var b = new ClipGradeBuilder();
            for (int i = 0; i <= 120; i++)
            {
                float at = -1f + i / 30f;
                // He leaves the frame for exactly half a second of the window: the ticks at 0.533 … 1.0 miss, and
                // each stands for the 1/30 s before it, so (0.5, 1.0] is held out of shot.
                bool inShot = at <= 0.5f || at > 1.0f;
                TickOf(b, Tick, inShot ? Framed(1, at) : Missed(1, at, GradeMiss.OutsideFrustum));
            }
            ClipGrade g = b.Finish(in Mult, StarScale.Default);
            Assert.AreEqual(1f - 0.5f / Peak, g.PeakCoverage01, 1e-5f);
            Assert.AreEqual(0.5f / Peak, g.PeakOutOfShot01, 1e-5f);
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void Coverage_NoUsablePeakWindow_IsNotMeasured_AndTheClipStillCounts(float duration)
        {
            var b = new ClipGradeBuilder();
            for (int i = 0; i < 60; i++)
            {
                ClipSample s = Framed(1, -1f + i / 30f);
                s.PeakDuration = duration;
                TickOf(b, Tick, s);
            }
            ClipGrade g = b.Finish(in Mult, StarScale.Default);
            Assert.IsTrue(g.Counted);
            Assert.IsFalse(g.HasPeak);
            Assert.AreEqual(0f, g.PeakCoverage01);
            Assert.AreEqual("—", GradeText.ClipAxes(g).Split(new[] { "peak " }, StringSplitOptions.None)[1].Substring(0, 1));
            Assert.AreEqual(string.Empty, GradeText.PeakAdvice(g));
        }

        // --- The blend ----------------------------------------------------------------------------------

        [Test]
        public void Blend_TheShippedConfig_GivesAWellFramedMissedPeakClipMoreThanZero()
        {
            // AC3 as the player reads it: the readout prints at least "1 %" (GradeText.PrintsZero, Story 1.19).
            var cfg = Load();
            ClipGrade g = FramedClip(-12f, -2f, comp: 0.98f).Finish(cfg);
            Assert.IsTrue(g.Counted);
            Assert.AreEqual(0f, g.PeakCoverage01);
            Assert.IsFalse(GradeText.PrintsZero(g.Percent01), $"printed {GradeText.Percent(g.Percent01)}");
        }

        [Test]
        public void Blend_FloorTimesCoverage_IsFramingTimesTheFlooredCoverage()
        {
            Assert.AreEqual(0.9f * 0.3f, ClipScoring.Percent(0.9f, 0f, in Mult), 1e-6f);
            Assert.AreEqual(0.9f, ClipScoring.Percent(0.9f, 1f, in Mult), 1e-6f);
            Assert.AreEqual(0.9f * (0.3f + 0.7f * 0.5f), ClipScoring.Percent(0.9f, 0.5f, in Mult), 1e-6f);

            var add = new ClipBlend(0.3f, 0f, ClipBlendShape.Additive, ClipFramingSource.GatedTicks, ClipMissRule.MostTime);
            Assert.AreEqual(0.3f * 0.9f + 0.7f * 0.5f, ClipScoring.Percent(0.9f, 0.5f, in add), 1e-6f);
        }

        [Test]
        public void Blend_AbsenceWeight_ScalesFramingByTheInShotShare()
        {
            Assert.AreEqual(0.8f, ClipScoring.Framing(0.8f, 0.25f, 0f), 1e-6f, "weight 0: empty footage costs nothing");
            Assert.AreEqual(0.8f * 0.25f, ClipScoring.Framing(0.8f, 0.25f, 1f), 1e-6f);
            Assert.AreEqual(0.8f * (1f - 0.5f * 0.75f), ClipScoring.Framing(0.8f, 0.25f, 0.5f), 1e-6f);

            // Through the builder: the same clip with 5 s of nobody-in-shot first.
            var cfgA = new ClipBlend(0.3f, 1f, ClipBlendShape.FloorTimesCoverage, ClipFramingSource.GatedTicks, ClipMissRule.MostTime);
            var b = new ClipGradeBuilder();
            for (int i = 0; i < 150; i++) TickOf(b, Tick, Missed(1, -6.5f + i / 30f, GradeMiss.OutsideFrustum));
            for (int i = 0; i <= 105; i++) TickOf(b, Tick, Framed(1, -1.5f + i / 30f, 1f));
            ClipGrade g = b.Finish(in cfgA, StarScale.Default);
            Assert.AreEqual(106f / 256f, g.InShot01, 1e-4f);
            Assert.AreEqual(106f / 256f, g.Framing01, 1e-4f);
            Assert.AreEqual(1f, g.Composition01, 1e-6f);
        }

        [Test]
        public void Presence_AnySubject_CountsAClipFramedAcrossARespawnAsInShot_Lifecycle_DoesNot()
        {
            // Lifecycle 1 framed for 2 s, then the respawn's lifecycle 2 framed for 2 s: the subject was in shot the
            // whole clip. Story 1.16's S8 measured the Lifecycle rule charging the first half as absence.
            var b = new ClipGradeBuilder();
            for (int i = 0; i < 60; i++) TickOf(b, Tick, Framed(1, 4f + i / 30f, 0.9f));
            for (int i = 0; i < 60; i++) TickOf(b, Tick, Framed(2, -1f + i / 30f, 0.9f));
            var any = new ClipBlend(0.3f, 1f, ClipBlendShape.FloorTimesCoverage, ClipFramingSource.GatedTicks, ClipMissRule.MostTime, ClipPresence.AnySubject);
            var own = new ClipBlend(0.3f, 1f, ClipBlendShape.FloorTimesCoverage, ClipFramingSource.GatedTicks, ClipMissRule.MostTime, ClipPresence.Lifecycle);
            Assert.AreEqual(1f, b.Finish(in any, StarScale.Default).InShot01, 1e-5f);
            Assert.AreEqual(0.5f, b.Finish(in own, StarScale.Default).InShot01, 1e-5f);

            // A tick on which NOBODY passed the gate is absence under both rules.
            var e = new ClipGradeBuilder();
            for (int i = 0; i < 30; i++) TickOf(e, Tick, Missed(1, -3f + i / 30f, GradeMiss.OutsideFrustum));
            for (int i = 0; i < 90; i++) TickOf(e, Tick, Framed(1, -2f + i / 30f, 0.9f));
            Assert.AreEqual(0.75f, e.Finish(in any, StarScale.Default).InShot01, 1e-5f);
            Assert.AreEqual(0.75f, e.Finish(in own, StarScale.Default).InShot01, 1e-5f);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(-0.5f)]
        [TestCase(-0f)]
        [TestCase(1e30f)]
        public void Blend_HostileInputs_StayAFiniteNonNegativeShare(float bad)
        {
            var hostile = new ClipBlend(bad, bad, ClipBlendShape.FloorTimesCoverage, ClipFramingSource.GatedTicks, ClipMissRule.MostTime);
            foreach (float p in new[]
                     {
                         ClipScoring.Percent(bad, 0.5f, in Mult), ClipScoring.Percent(0.5f, bad, in Mult),
                         ClipScoring.Percent(0.5f, 0.5f, in hostile), ClipScoring.Framing(bad, 0.5f, 0.5f),
                         ClipScoring.Framing(0.5f, bad, 0.5f), ClipScoring.Framing(0.5f, 0.5f, bad),
                     })
            {
                Assert.IsFalse(float.IsNaN(p) || float.IsInfinity(p), $"{p}");
                Assert.That(p, Is.InRange(0f, 1f));
                Assert.IsFalse(p == 0f && float.IsNegative(p), "a share is never -0");
            }
        }

        // --- Candidates and the miss reason ---------------------------------------------------------------

        [Test]
        public void Candidates_ACountedLifecycleBeatsAMiss_WhicheverCameFirst()
        {
            var b = new ClipGradeBuilder();
            for (int i = 0; i < 60; i++)
                TickOf(b, Tick, Missed(7, -3f + i / 30f, GradeMiss.TooSmall, "Far"),
                                Framed(8, -12f + i / 30f, 0.01f, "Near"));
            ClipGrade g = b.Finish(in Mult, StarScale.Default);
            Assert.IsTrue(g.Counted);
            Assert.AreEqual("Near", g.SubjectId);
        }

        // The shared rule itself (PhotoModeController.GradeBestSubject and the builder's overflow both call it). The
        // builder test above passes by construction too (a lifecycle that never passed the gate is never a
        // candidate), which the Task 7.2 mutation run showed: it stayed green with this rule broken. This one does not.
        [Test]
        public void Ranking_ACountedGradeBeatsAMiss_ThenTheHigherPercent_AndATieKeepsTheFirst()
        {
            Assert.IsTrue(GradeRanking.Beats(true, 0f, false, 0f), "a counted 0 % beats a miss");
            Assert.IsFalse(GradeRanking.Beats(false, 0f, true, 0f), "a miss never beats a counted grade");
            Assert.IsTrue(GradeRanking.Beats(true, 0.6f, true, 0.5f));
            Assert.IsFalse(GradeRanking.Beats(true, 0.5f, true, 0.5f), "strictly greater: the first keeps a tie");
            Assert.IsFalse(GradeRanking.Beats(false, 0f, false, 0f));
        }

        [Test]
        public void Candidates_ATieKeepsTheFirstSeen()
        {
            var b = new ClipGradeBuilder();
            for (int i = 0; i < 60; i++)
                TickOf(b, Tick, Framed(3, -1f + i / 30f, 0.7f, "First"), Framed(4, -1f + i / 30f, 0.7f, "Second"));
            Assert.AreEqual("First", b.Finish(in Mult, StarScale.Default).SubjectId);
        }

        [Test]
        public void Candidates_TwoLifecyclesOfOneActor_AreGradedApart_NeverMerged()
        {
            // Lifecycle 1: framed at 0.4 through its whole peak. Lifecycle 2 (the respawn, same pooled actor): framed
            // at 1.0 but stopped before its peak. A merge would mix 1's coverage with 2's composition.
            var both = new ClipGradeBuilder();
            var only1 = new ClipGradeBuilder();
            var only2 = new ClipGradeBuilder();
            for (int i = 0; i <= 90; i++)
            {
                ClipSample s = Framed(1, -0.5f + i / 30f, 0.4f);
                TickOf(both, Tick, s); TickOf(only1, Tick, s);
            }
            for (int i = 0; i <= 90; i++)
            {
                ClipSample s = Framed(2, -13f + i / 30f, 1.0f);
                TickOf(both, Tick, s); TickOf(only2, Tick, s);
            }
            ClipGrade g = both.Finish(in Mult, StarScale.Default);
            ClipGrade g1 = only1.Finish(in Mult, StarScale.Default);
            ClipGrade g2 = only2.Finish(in Mult, StarScale.Default);
            Assert.AreEqual(2, both.CandidateCount);

            // The clip's grade is ONE of the two lifecycles, its shares exactly that lifecycle's own.
            ClipGrade expected = g1.Percent01 >= g2.Percent01 ? g1 : g2;
            Assert.AreEqual(expected.Composition01, g.Composition01, 1e-6f);
            Assert.AreEqual(expected.PeakCoverage01, g.PeakCoverage01, 1e-6f);
            Assert.That(g.Composition01, Is.EqualTo(0.4f).Within(1e-5f).Or.EqualTo(1f).Within(1e-5f), "never a mix");
        }

        [Test]
        public void MissReason_MostTime_TakesTheLongestHeldGate_DeepestGate_TakesTheClosest()
        {
            var b = new ClipGradeBuilder();
            for (int i = 0; i < 300; i++) TickOf(b, Tick, Missed(1, -5f + i / 30f, GradeMiss.OutsideFrustum));
            for (int i = 0; i < 3; i++) TickOf(b, Tick, Missed(1, 5f + i / 30f, GradeMiss.Occluded));
            Assert.AreEqual(GradeMiss.OutsideFrustum, b.Finish(in Mult, StarScale.Default).MissReason);

            var deepest = new ClipBlend(0.3f, 0f, ClipBlendShape.FloorTimesCoverage, ClipFramingSource.GatedTicks, ClipMissRule.DeepestGate);
            Assert.AreEqual(GradeMiss.Occluded, b.Finish(in deepest, StarScale.Default).MissReason);
        }

        [Test]
        public void MissReason_ATieGoesToTheGateClosestToPassing()
        {
            var b = new ClipGradeBuilder();
            for (int i = 0; i < 30; i++) TickOf(b, Tick, Missed(1, i / 30f, GradeMiss.TooSmall));
            for (int i = 0; i < 30; i++) TickOf(b, Tick, Missed(1, 1f + i / 30f, GradeMiss.Occluded));
            ClipGrade g = b.Finish(in Mult, StarScale.Default);
            Assert.AreEqual(GradeMiss.Occluded, g.MissReason);
            Assert.IsTrue(g.IsMiss);
            Assert.AreEqual("TownDrunk", g.SubjectId, "a miss against a subject who was there names him");
        }

        [Test]
        public void MissReason_NobodyEverLive_IsNoSubject_AndNoTicksAtAll_IsNotGraded()
        {
            var b = new ClipGradeBuilder();
            for (int i = 0; i < 30; i++) TickOf(b, Tick);
            ClipGrade g = b.Finish(in Mult, StarScale.Default);
            Assert.AreEqual(GradeMiss.NoSubject, g.MissReason);
            Assert.IsFalse(g.HasSubject);
            Assert.AreEqual(1f, g.GradedSeconds, 1e-5f);

            var empty = new ClipGradeBuilder();
            ClipGrade none = empty.Finish(in Mult, StarScale.Default);
            Assert.IsFalse(none.Counted || none.IsMiss);
        }

        [Test]
        public void Reuse_ASecondClipStartsFromZero()
        {
            ClipGradeBuilder b = FramedClip(-1f, 2.5f, 1f);
            Assert.IsTrue(b.Finish(in Mult, StarScale.Default).Counted);

            b.Begin();
            Assert.AreEqual(0, b.TicksGraded);
            Assert.AreEqual(0d, b.GradedSeconds);
            Assert.AreEqual(0, b.CandidateCount);
            for (int i = 0; i < 30; i++) TickOf(b, Tick, Missed(1, -1f + i / 30f, GradeMiss.TooSmall));
            ClipGrade g = b.Finish(in Mult, StarScale.Default);
            Assert.AreEqual(GradeMiss.TooSmall, g.MissReason, "the first clip's counted lifecycle must not leak in");
            Assert.AreEqual(1f, g.GradedSeconds, 1e-5f);
        }

        [Test]
        public void Overflow_MoreLifecyclesThanCapacity_IsFlagged_AndNeverThrows()
        {
            var b = new ClipGradeBuilder(capacity: 2);
            for (int life = 1; life <= 5; life++)
                for (int i = 0; i < 10; i++) TickOf(b, Tick, Framed(life, -1f + i / 30f, life / 10f));
            Assert.IsTrue(b.Overflowed);
            Assert.AreEqual(2, b.CandidateCount);
            Assert.IsTrue(b.Finish(in Mult, StarScale.Default).Counted);
        }

        // --- The payload ------------------------------------------------------------------------------

        [Test]
        public void Default_IsNeitherCountedNorAMiss_AndPrintsNoNumber()
        {
            ClipGrade d = default;
            Assert.IsFalse(d.Counted);
            Assert.IsFalse(d.IsMiss);
            Assert.IsFalse(d.HasSubject);
            Assert.That(d.ToString(), Does.Contain("not graded"));
            Assert.That(d.ToString(), Does.Not.Contain("%"));
            Assert.AreEqual(GradeText.ClipAxesNotMeasured, GradeText.ClipAxes(d));
            Assert.AreEqual(string.Empty, GradeText.PeakAdvice(d));
        }

        [Test]
        public void Factories_SanitiseEveryShare_AndMissedNoneIsNotACountedClip()
        {
            ClipGrade s = ClipGrade.Scored(float.NaN, 9, float.NaN, -1f, 7f, float.PositiveInfinity, -0f, float.NaN,
                                           true, float.NaN, null);
            foreach (float v in new[] { s.Percent01, s.Composition01, s.Framing01, s.PeakCoverage01, s.PeakBeforeClip01,
                                        s.PeakAfterClip01, s.InShot01, s.GradedSeconds })
            {
                Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v));
                Assert.That(v, Is.GreaterThanOrEqualTo(0f));
                Assert.IsFalse(v == 0f && float.IsNegative(v));
            }
            Assert.AreEqual(5, s.Stars);
            Assert.AreEqual(string.Empty, s.SubjectId);

            ClipGrade m = ClipGrade.Missed(GradeMiss.None, 1f);
            Assert.IsFalse(m.Counted, "Missed(None) must never build a counted clip");
            Assert.AreEqual(GradeMiss.Unevaluated, m.MissReason);
            Assert.That(ClipGrade.Missed(GradeMiss.Occluded, 1f, "TownDrunk").ToString(), Does.Not.Contain("%"));
        }

        [Test]
        public void RecordedClip_CarriesTheGradeItWasBuiltWith()
        {
            ClipGrade g = ClipGrade.Missed(GradeMiss.TooSmall, 2f, "TownDrunk");
            var clip = new RecordedClip("clip_0001", null, DateTime.UtcNow, 2f, StopReason.CameraLowered, g);
            Assert.AreEqual(GradeMiss.TooSmall, clip.Grade.MissReason);
            Assert.AreEqual("TownDrunk", clip.Grade.SubjectId);
            Assert.AreEqual(2f, clip.Grade.GradedSeconds);
        }

        // --- The readout's words ---------------------------------------------------------------------

        [TestCase("fr-FR")]
        [TestCase("de-DE")]
        [TestCase("tr-TR")]
        public void ClipAxes_PrintInTheInvariantCulture(string culture)
        {
            // Non-midpoint values (loop lesson 152: Mono rounds midpoints, which would pin the rounding instead).
            ClipGrade g = ClipGrade.Scored(0.613f, 3, 0.923f, 0.923f, 0.376f, 0.4f, 0.1f, 0.637f, true, 4f, "TownDrunk");
            string invariant = GradeText.ClipAxes(g);
            CultureInfo previous = Thread.CurrentThread.CurrentCulture;
            try
            {
                Thread.CurrentThread.CurrentCulture = new CultureInfo(culture);
                Assert.AreEqual(invariant, GradeText.ClipAxes(g));
                Assert.That(GradeText.ClipAxes(g), Does.Not.Contain("٪"));
                Assert.AreEqual(string.Empty, GradeText.PeakAdvice(g).Trim('a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i',
                    'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z', ' ', '—', '-'),
                    "the advice carries no number to localise");
                Assert.That(g.ToString(), Does.Not.Contain("٪"));
            }
            finally
            {
                Thread.CurrentThread.CurrentCulture = previous;
            }
            Assert.AreEqual("framing 92 %   ·   peak 38 %   ·   in shot 64 %", invariant);
        }

        [Test]
        public void PeakAdvice_NamesTheBiggestLoss_AndPraisesOnlyAClipThatPrints100()
        {
            ClipGrade Make(float cov, float before, float after) =>
                ClipGrade.Scored(0.5f, 3, 0.9f, 0.9f, cov, before, after, 1f, true, 5f, "TownDrunk");

            Assert.AreEqual("caught the whole moment", GradeText.PeakAdvice(Make(1f, 0f, 0f)));
            Assert.AreEqual("caught the whole moment", GradeText.PeakAdvice(Make(0.996f, 0.004f, 0f)), "prints 100 %");
            Assert.AreNotEqual("caught the whole moment", GradeText.PeakAdvice(Make(0.99f, 0.01f, 0f)), "prints 99 %");
            Assert.AreEqual("started too late — record sooner", GradeText.PeakAdvice(Make(0.4f, 0.6f, 0f)));
            Assert.AreEqual("stopped too soon — keep rolling", GradeText.PeakAdvice(Make(0.4f, 0f, 0.6f)));
            Assert.AreEqual("stopped too soon — keep rolling", GradeText.PeakAdvice(Make(0f, 0f, 1f)));
            Assert.AreEqual("started too late — record sooner", GradeText.PeakAdvice(Make(0f, 1f, 0f)));
            Assert.AreEqual("it happened out of shot — keep him framed", GradeText.PeakAdvice(Make(0.3f, 0.1f, 0.1f)));
        }

        [Test]
        public void ClipMissLong_UsesAClipsWordsWhereAPhotosWouldBeWrong_AndTheSameWordsElsewhere()
        {
            Assert.AreEqual("this clip was never graded", GradeText.ClipMissLong(GradeMiss.Unevaluated));
            foreach (GradeMiss m in Enum.GetValues(typeof(GradeMiss)))
            {
                string words = GradeText.ClipMissLong(m);
                Assert.That(words, Does.Not.Contain("shot was"), m.ToString());
                if (m != GradeMiss.Unevaluated && m != GradeMiss.NoCamera)
                    Assert.AreEqual(GradeText.MissLong(m), words, m.ToString());
            }
        }

        // --- GradingConfig: the clip fields and the all-problems validator ---------------------------

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void ClipFields_NonFinite_FallBackToThePristineValue(float bad)
        {
            var pristine = NewConfig();
            var corrupt = NewConfig();
            corrupt.clipPeakFloor = bad;
            corrupt.clipAbsenceWeight = bad;
            Assert.AreEqual(pristine.SafeClipPeakFloor, corrupt.SafeClipPeakFloor);
            Assert.AreEqual(pristine.SafeClipAbsenceWeight, corrupt.SafeClipAbsenceWeight);
            Assert.IsTrue(corrupt.TryGetConfigProblem(out string problem));
            Assert.That(problem, Does.Contain(nameof(GradingConfig.clipPeakFloor)));
            Assert.That(problem, Does.Contain(nameof(GradingConfig.clipAbsenceWeight)));
        }

        [Test]
        public void ClipFields_OutOfRange_AreClampedAndReported()
        {
            var c = NewConfig();
            c.clipPeakFloor = 1.7f;
            c.clipAbsenceWeight = -0.2f;
            Assert.AreEqual(1f, c.SafeClipPeakFloor);
            Assert.AreEqual(0f, c.SafeClipAbsenceWeight);
            Assert.IsTrue(c.TryGetConfigProblem(out string problem));
            Assert.That(problem, Does.Contain("clipPeakFloor is 1.7"));
            Assert.That(problem, Does.Contain("clipAbsenceWeight is -0.2"));
            Assert.That(problem, Does.Contain("peak coverage no longer changes"), "the clamped floor of 1 is reported too");
        }

        [TestCase(0f, "too extreme")]
        [TestCase(1f, "no longer changes")]
        [TestCase(0.75f, "four-star boundary")]
        public void ClipPeakFloor_TheSilentShapes_AreReported(float floor, string words)
        {
            var c = NewConfig();
            c.clipPeakFloor = floor;
            Assert.IsTrue(c.TryGetConfigProblem(out string problem));
            Assert.That(problem, Does.Contain(words));
        }

        [Test]
        public void Validator_ReportsEveryProblemInOneMessage()
        {
            var c = NewConfig();
            c.occluderMask = 0;
            c.minSubjectHeight = float.NaN;
            c.timingFullSeconds = 70f;
            c.timingZeroSeconds = 90f;
            c.starThreshold4 = 0.95f;
            Assert.IsTrue(c.TryGetConfigProblem(out string problem));
            foreach (string part in new[] { "minSubjectHeight is NaN", "timingFullSeconds is 70", "timingZeroSeconds is 90",
                                            "occluderMask is empty", "is not greater than", "not in descending order" })
                Assert.That(problem, Does.Contain(part));
        }

        [Test]
        public void Validator_StructuralChecksUseTheValuesTheGraderUses_SoAClampedFieldRaisesNoFalseAlarm()
        {
            // The 2026-07-28 false alarm: idealMax 2.0 resolves to 1.0, whose curve is fine.
            var c = NewConfig();
            c.prominenceIdealMax = 2f;
            Assert.IsTrue(c.TryGetConfigProblem(out string problem));
            Assert.That(problem, Does.Contain("prominenceIdealMax is 2"));
            Assert.That(problem, Does.Not.Contain("is not above prominenceIdealMax"));
        }

        [Test]
        public void ShippedAsset_PassesItsOwnValidator()
        {
            GradingConfig asset = Load();
            Assert.IsFalse(asset.TryGetConfigProblem(out string problem), problem);
        }

        private static GradingConfig Load()
        {
            var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<GradingConfig>("Assets/Data/Grading/GradingConfig.asset");
            Assert.IsNotNull(asset, "the shipped GradingConfig lives at Assets/Data/Grading/GradingConfig.asset");
            return asset;
        }
    }
}
