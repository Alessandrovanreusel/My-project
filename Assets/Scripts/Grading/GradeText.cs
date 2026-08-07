using UnityEngine;

namespace CameraGame.Grading
{
    /// <summary>
    /// How a grade is WORDED for a person. One home for the star glyphs and the miss vocabulary, shared by
    /// the gallery (Story 1.11) and the grade-feedback HUD (Story 1.12).
    ///
    /// ⚠️ WHY THIS IS NOT TWO PRIVATE COPIES. Both readouts show the same <see cref="ShotGrade"/> and both
    /// need the same two things: five star glyphs, and plain words for a <see cref="GradeMiss"/>. Story
    /// 1.11 shipped them as private statics on <c>GalleryView</c>; a second copy on the HUD would mean the
    /// gallery and the HUD could disagree about what <see cref="GradeMiss.Occluded"/> is called — the same
    /// photograph described two ways in one game. The wording is a property of the GRADE, not of either
    /// view, so it lives beside the grade.
    ///
    /// It sits in <c>Grading</c> rather than <c>UI</c> because both <c>Gallery</c> and <c>UI</c> are
    /// allowed to depend on <c>Grading</c> and neither may depend on the other
    /// (game-architecture.md §Architectural Boundaries). Pure static string work — no Unity lifecycle, no
    /// allocation beyond the string it returns, nothing that runs in a per-frame path.
    /// </summary>
    public static class GradeText
    {
        /// <summary>Five glyphs, always — filled up to the rating. A fixed width so the ratings line up down
        /// the gallery grid and can be compared at a glance, which three characters of "3/5" cannot.
        ///
        /// Verified to render: the ★/☆ glyphs came back correctly through uGUI's built-in
        /// <c>LegacyRuntime.ttf</c> in Story 1.11's run. A font missing them draws empty boxes, which is why
        /// the HUD's evidence is a photograph of the screen rather than a readout of <c>Text.text</c>.</summary>
        public static string Stars(int stars)
        {
            int filled = Mathf.Clamp(stars, 0, 5);
            return new string('★', filled) + new string('☆', 5 - filled);
        }

        /// <summary>
        /// Plain words for a miss reason, SHORT — the gallery's wording, unchanged.
        ///
        /// ⚠️ KEEP THESE SHORT AND DO NOT "IMPROVE" THEM. They are printed inside a thumbnail cell, and the
        /// cell gets narrower the more photographs the player has taken. "something in the way" came back
        /// from Story 1.11's verification run truncated to "missed — something in the", which tells the
        /// player less than nothing — it looks like the gallery itself is broken. Every string here was
        /// chosen against that evidence. The HUD has a full-width line and uses <see cref="MissLong"/>.
        /// </summary>
        public static string MissShort(GradeMiss miss)
        {
            switch (miss)
            {
                case GradeMiss.NoSubject:        return "nobody there";
                case GradeMiss.TooSmall:         return "too far away";
                case GradeMiss.Occluded:         return "blocked";
                case GradeMiss.OutsideFrustum:   return "out of frame";
                case GradeMiss.BehindCamera:     return "behind you";
                case GradeMiss.DegenerateBounds: return "nothing there";
                case GradeMiss.NoCamera:         return "no camera";
                case GradeMiss.NoConfig:         return "grading not set up";
                case GradeMiss.NoViewport:       return "no viewport";
                case GradeMiss.Unevaluated:      return "not graded";
                default:                         return miss.ToString();
            }
        }

        /// <summary>
        /// Plain words for a miss reason, FULLER — the HUD's line, which has the whole width of the screen.
        ///
        /// The extra room buys the actionable half. NFR10 asks the player to understand WHY, and "blocked"
        /// says what happened while "something was in the way — move so you can see him" says what to do
        /// about it. Where a miss is a setup problem rather than a shot the player took badly
        /// (NoCamera / NoConfig / NoViewport), it says so plainly instead of blaming them.
        ///
        /// ⚠️ EVERY <see cref="GradeMiss"/> MEMBER IS NAMED HERE ON PURPOSE. Falling through to
        /// <c>miss.ToString()</c> would put a developer's enum name — "DegenerateBounds" — on the player's
        /// screen. The <c>default</c> arm is the backstop for an enum member added later, not a wording
        /// choice; a new member should get a sentence here in the same commit that adds it.
        /// </summary>
        public static string MissLong(GradeMiss miss)
        {
            switch (miss)
            {
                case GradeMiss.NoSubject:        return "there was nobody in the frame";
                case GradeMiss.TooSmall:         return "too far away — get closer, or zoom in";
                case GradeMiss.Occluded:         return "something was in the way — move so you can see him";
                case GradeMiss.OutsideFrustum:   return "he was outside the frame";
                case GradeMiss.BehindCamera:     return "he was behind you";
                case GradeMiss.DegenerateBounds: return "there was nothing there to photograph";
                case GradeMiss.NoCamera:         return "no camera — the shot could not be taken";
                case GradeMiss.NoConfig:         return "grading is not set up";
                case GradeMiss.NoViewport:       return "the view has no size — nothing could be measured";
                case GradeMiss.Unevaluated:      return "this shot was never graded";
                default:                         return miss.ToString();
            }
        }

        /// <summary>
        /// The actionable half of the timing axis, in a player's words: were they early or late, and by how
        /// much. Empty string when there is no measurement to report — the caller then prints nothing at
        /// all rather than a placeholder.
        ///
        /// ⚠️ <see cref="ShotGrade.Timing01"/> NAMES THE AXIS THAT COST THE SHOT; IT DOES NOT SAY WHICH WAY.
        /// "timing 20%" is true and useless: 20% is the same number two seconds early and two seconds late,
        /// and "click sooner" is the only half a player can act on. That is why
        /// <see cref="ShotGrade.PeakOffset"/> is carried at all.
        ///
        /// Guarded through <see cref="ShotGrade.TimingMeasured"/>, so a shot rejected before timing was ever
        /// read — or a zeroed <c>default(ShotGrade)</c>, whose raw offset is 0 — can never be reported as
        /// "dead on the moment". Same discipline as <c>GradeDetail.PeakOffsetText</c>.
        /// </summary>
        public static string TimingAdvice(ShotGrade grade)
        {
            if (!grade.TimingMeasured) return string.Empty;

            float offset = grade.PeakOffset;

            // Inside a tenth of a second of the peak window there is nothing useful to say — and rounding
            // would print "0.0s early", which reads as a criticism of a shot that was perfectly timed.
            if (Mathf.Abs(offset) < 0.1f) return "right on the moment";

            // Positive is EARLY and negative is LATE (ISubject.PeakOffset). Stated as the player's mistake
            // rather than as a signed number: "-1.4s" is a debug value, "1.4s late" is feedback.
            return offset > 0f
                ? $"{offset:0.0}s early — wait for it"
                : $"{-offset:0.0}s late — shoot sooner";
        }
    }
}
