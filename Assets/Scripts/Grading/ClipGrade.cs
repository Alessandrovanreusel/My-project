using System.Globalization;
using UnityEngine;

namespace CameraGame.Grading
{
    /// <summary>
    /// The grade of one recorded clip (Story 1.16, FR4 and FR7): how well the subject was framed while the
    /// clip recorded him, and how much of his peak window the clip holds with him framed. Carried on
    /// <c>RecordedClip.Grade</c>, so it travels on <c>ClipRecordedChannel</c> with the clip itself.
    ///
    /// Built by <see cref="ClipGradeBuilder.Finish(GradingConfig, double)"/> from the per-tick samples of the
    /// UNCHANGED per-frame grader (<see cref="ShotGrader.Grade(Camera, CameraGame.Events.ISubject, GradingConfig, out GradeDetail)"/>).
    /// Every rule of <see cref="ShotGrade"/> applies here too, for the same reasons:
    ///   - pure data, no Unity object reference, so Story 1.17's card and Epic 9 can map it into a JSON DTO;
    ///   - <see cref="Stars"/> is a stored field: a card entry must keep the rating the player was shown, even
    ///     after someone retunes the star thresholds;
    ///   - the zero value of <see cref="MissReason"/> is <see cref="GradeMiss.Unevaluated"/>, so
    ///     <c>default(ClipGrade)</c> is never <see cref="Counted"/> and never <see cref="IsMiss"/>: it is the
    ///     "not graded" state, worded as such;
    ///   - every normalized float goes through <see cref="Sane"/> (NaN → 0, then clamped to [0, 1]);
    ///   - an empty <see cref="SubjectId"/> means nobody, never a sentinel string. Use <see cref="HasSubject"/>.
    ///
    /// ================================================================================================
    /// THE PEAK WINDOW, PARTITIONED
    /// ================================================================================================
    /// For the lifecycle the clip is graded on, the peak window splits into four shares that add up to at most
    /// 1 (less only when the subject was switched off mid-clip, a rig-only case):
    ///   - <see cref="PeakBeforeClip01"/>: the part that had already happened when the clip started;
    ///   - <see cref="PeakAfterClip01"/>: the part still to come when the clip stopped;
    ///   - <see cref="PeakCoverage01"/>: the part the clip holds WITH HIM FRAMED (passing the gate);
    ///   - the rest (<see cref="PeakOutOfShot01"/>): the part the clip holds while he was not framed.
    /// The advice line names the biggest loss (<see cref="GradeText.PeakAdvice"/>), so it tells the player the
    /// one thing that actually cost the clip.
    /// </summary>
    public readonly struct ClipGrade
    {
        /// <summary>The clip's quality in [0, 1]: 0 for a miss or a clip nobody graded. See
        /// <see cref="ClipScoring.Percent"/> for the blend.</summary>
        public readonly float Percent01;

        /// <summary>Star rating 1–5 from <see cref="Percent01"/> against the config's <see cref="StarScale"/>, at the
        /// moment of grading. A miss is 1★ (the GDD's scale starts at one), which is why
        /// <see cref="MissReason"/> exists.</summary>
        public readonly int Stars;

        /// <summary><see cref="GradeMiss.None"/> for a clip in which the subject passed the gate on at least one
        /// tick; a real reason for a miss; <see cref="GradeMiss.Unevaluated"/> for a clip nobody graded.</summary>
        public readonly GradeMiss MissReason;

        /// <summary>Mean composition (<see cref="ShotGrade.Composition01"/>) over the ticks where this lifecycle
        /// passed the gate, weighted by each tick's length. 0 on a miss.</summary>
        public readonly float Composition01;

        /// <summary>The framing factor the blend uses: <see cref="Composition01"/> times the presence term
        /// (<c>1 − absenceWeight × (1 − InShot01)</c>). Equal to <see cref="Composition01"/> when footage without
        /// him costs nothing (absence weight 0).</summary>
        public readonly float Framing01;

        /// <summary>Share of the peak window the clip holds with the subject framed (FR7). 1 only for the whole
        /// window, framed throughout. Stored TRUE, never floored: the floor is in the blend, not in this number
        /// (Story 1.13's rule, "Timing01 stays honest").</summary>
        public readonly float PeakCoverage01;

        /// <summary>Share of the peak window that had already passed when the clip started.</summary>
        public readonly float PeakBeforeClip01;

        /// <summary>Share of the peak window still to come when the clip stopped.</summary>
        public readonly float PeakAfterClip01;

        /// <summary>Share of the clip's footage that was "in shot" for the presence term: by default the ticks on which
        /// ANY subject passed the gate (<see cref="ClipPresence.AnySubject"/>), so a clip filmed continuously across a
        /// respawn is not charged for the earlier lifecycle it framed well (Story 1.16, measured on S8).</summary>
        public readonly float InShot01;

        /// <summary>True when the subject had a peak window to cover (<c>ISubject.PeakDuration</c> &gt; 0). When
        /// false, every peak share is 0 and means "not measured", and the advice line says nothing about it.</summary>
        public readonly bool HasPeak;

        /// <summary>Seconds of footage the grader saw (the sum of the graded ticks' repeats ÷ 30). For a clip it
        /// graded, this equals <c>IFootage.Seconds</c>: the grade describes exactly the frames the clip holds.
        /// Stored raw (not a share).</summary>
        public readonly float GradedSeconds;

        /// <summary>Who the clip is OF (<c>ISubject.SubjectId</c>). Empty for nobody. Never null on a factory-built
        /// grade, but <c>default(ClipGrade)</c> runs no constructor: read <see cref="HasSubject"/>.</summary>
        public readonly string SubjectId;

        /// <summary>True when the subject passed the gate on at least one tick and the score is meaningful.</summary>
        public bool Counted => MissReason == GradeMiss.None;

        /// <summary>True when the clip was graded and never caught anyone gradeable.</summary>
        public bool IsMiss => MissReason != GradeMiss.None && MissReason != GradeMiss.Unevaluated;

        /// <summary>True when this grade names who it is of.</summary>
        public bool HasSubject => !string.IsNullOrEmpty(SubjectId);

        /// <summary>The share of the peak window the clip HELD while he was not framed: whatever is left of the
        /// window after the framed, before-the-clip and after-the-clip shares. 0 without a peak.</summary>
        public float PeakOutOfShot01 =>
            HasPeak ? Sane(1f - PeakCoverage01 - PeakBeforeClip01 - PeakAfterClip01) : 0f;

        private ClipGrade(float percent01, int stars, GradeMiss miss, float composition01, float framing01,
                          float coverage01, float before01, float after01, float inShot01, bool hasPeak,
                          float gradedSeconds, string subjectId)
        {
            Percent01 = Sane(percent01);
            Stars = Mathf.Clamp(stars, 1, 5);
            MissReason = miss;
            Composition01 = Sane(composition01);
            Framing01 = Sane(framing01);
            HasPeak = hasPeak;
            PeakCoverage01 = hasPeak ? Sane(coverage01) : 0f;
            PeakBeforeClip01 = hasPeak ? Sane(before01) : 0f;
            PeakAfterClip01 = hasPeak ? Sane(after01) : 0f;
            InShot01 = Sane(inShot01);

            // RAW, not Sane(): a length in seconds, not a share. Only made finite and non-negative.
            GradedSeconds = float.IsNaN(gradedSeconds) || float.IsInfinity(gradedSeconds) || gradedSeconds < 0f
                ? 0f : gradedSeconds;
            SubjectId = subjectId ?? string.Empty;
        }

        /// <summary>NaN → 0, then clamped to [0, 1]. <c>Mathf.Clamp01(NaN)</c> is NaN, so clamping alone is not
        /// enough. −0 keeps its sign through Clamp01 (loop lesson 138), so it is mapped to +0 too: a share is
        /// never negative, not even in its sign bit.</summary>
        internal static float Sane(float v) => float.IsNaN(v) || v <= 0f ? 0f : Mathf.Clamp01(v);

        /// <summary>A clip in which the subject passed the gate on at least one tick. Every share is the measured
        /// one; <paramref name="percent01"/> is the blended score and <paramref name="stars"/> its rating.</summary>
        public static ClipGrade Scored(float percent01, int stars, float composition01, float framing01,
                                       float coverage01, float before01, float after01, float inShot01,
                                       bool hasPeak, float gradedSeconds, string subjectId) =>
            new ClipGrade(percent01, stars, GradeMiss.None, composition01, framing01, coverage01, before01,
                          after01, inShot01, hasPeak, gradedSeconds, subjectId);

        /// <summary>A clip that was graded and never caught anyone gradeable: 0 %, 1★, with the gate that held.
        /// <see cref="GradeMiss.None"/> is not a rejection and is mapped to <see cref="GradeMiss.Unevaluated"/>, the
        /// same caller-bug guard <see cref="ShotGrade.Missed"/> has: a "miss" with reason None would be a counted
        /// clip with an all-zero breakdown, the shape the HUD must never show.</summary>
        public static ClipGrade Missed(GradeMiss reason, float gradedSeconds, string subjectId = null) =>
            new ClipGrade(0f, 1, reason == GradeMiss.None ? GradeMiss.Unevaluated : reason, 0f, 0f, 0f, 0f, 0f, 0f,
                          false, gradedSeconds, subjectId);

        /// <summary>A clip nobody graded (no grading config, no event manager, or the grader failed). Neither
        /// counted nor a miss; the HUD words it "not graded".</summary>
        public static ClipGrade NotGraded(float gradedSeconds = 0f) =>
            new ClipGrade(0f, 1, GradeMiss.Unevaluated, 0f, 0f, 0f, 0f, 0f, 0f, false, gradedSeconds, string.Empty);

        /// <summary>One line with the total AND the breakdown, in the invariant culture. A miss and a clip nobody
        /// graded print no numbers they never measured.</summary>
        public override string ToString()
        {
            string who = HasSubject ? "of " + SubjectId : "of nobody";
            CultureInfo inv = CultureInfo.InvariantCulture;
            if (!Counted && !IsMiss)
                return string.Format(inv, "ClipGrade(not graded, {0:0.000} s)", GradedSeconds);
            if (IsMiss)
                return string.Format(inv, "ClipGrade(miss:{0} {1}, {2:0.000} s)", MissReason, who, GradedSeconds);

            string peak = HasPeak
                ? string.Format(inv, "peak {0} (before {1}, after {2}, out of shot {3})",
                    GradeText.Percent(PeakCoverage01), GradeText.Percent(PeakBeforeClip01),
                    GradeText.Percent(PeakAfterClip01), GradeText.Percent(PeakOutOfShot01))
                : "peak n/a";
            return string.Format(inv, "ClipGrade({0}, {1}★ {2} — framing {3} (composition {4}, in shot {5}) · {6}; {7:0.000} s)",
                GradeText.Percent(Percent01), Stars, who, GradeText.Percent(Framing01),
                GradeText.Percent(Composition01), GradeText.Percent(InShot01), peak, GradedSeconds);
        }
    }

    /// <summary>
    /// "Which of several grades is the better one", shared by the photo shutter
    /// (<c>PhotoModeController.GradeBestSubject</c>) and the clip grader (<see cref="ClipGradeBuilder"/>), so the
    /// two cannot drift (Story 1.16 lifted it here unchanged). A COUNTED grade beats any miss whatever the
    /// percentages say (a counted grade can be 0 % and a miss is always 0 %), then the higher percent wins.
    /// Strictly greater, so the FIRST graded candidate keeps a tie.
    /// </summary>
    public static class GradeRanking
    {
        /// <summary>True when the candidate should replace the best so far.</summary>
        public static bool Beats(bool counted, float percent01, bool bestCounted, float bestPercent01) =>
            (counted && !bestCounted) || (counted == bestCounted && percent01 > bestPercent01);
    }
}
