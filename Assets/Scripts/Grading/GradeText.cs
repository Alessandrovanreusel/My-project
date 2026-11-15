using System.Globalization;
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
        /// A 0–1 score as the player reads it: "0 %", "37 %", "100 %". The ONE place the readout turns a
        /// number into a percentage (Story 1.19), so the text and anything decided from the text — the red
        /// of "this earned nothing" — cannot disagree.
        ///
        /// ⚠️ THE CULTURE IS PINNED TO INVARIANT, AND THAT IS NOT COSMETIC. <c>{x:P0}</c> follows the
        /// player's locale, and in Unity's Mono the fr-FR culture prints "0 ٪" with U+066A, the ARABIC
        /// percent sign (measured 2026-11-15), a glyph <c>LegacyRuntime.ttf</c> may not even contain. In
        /// this editor (en-US) invariant output is byte-identical to what shipped before: Mono prints the
        /// percent pattern "n %", with a space.
        ///
        /// ⚠️ SANITISED FIRST: NaN, negatives and −0 print "0 %", never "-0 %" or "NaN %". −0 is a real input
        /// here, not a curiosity: <c>Mathf.Clamp01(-0f)</c> keeps the sign bit, so
        /// <c>ShotGrade.Scored(1, 1, -0f, …)</c> carries a <c>Percent01</c> of −0 through the real factory.
        /// <c>!(p > 0f)</c> is true for all three (NaN compares false with everything).
        ///
        /// It ROUNDS, as the readout always has (0.4 % prints "0 %", 0.6 % prints "1 %"). Whether 99.6 %
        /// should print "100 %" is a separate, deferred question (deferred-work.md); it is deliberately not
        /// folded in here, because changing the rounding would also change which shots read red.
        /// </summary>
        public static string Percent(float percent01)
        {
            float p = percent01;
            if (!(p > 0f)) p = 0f;
            else if (p > 1f) p = 1f;
            return p.ToString("P0", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// True when the readout PRINTS "0 %" for this score — the shot earned nothing (Story 1.19, AC2).
        ///
        /// ⚠️ DEFINED BY THE PRINTED STRING, NOT BY A THRESHOLD, because that is literally what AC2 says:
        /// "the choice follows the printed text, not the raw number". A threshold writes the rounding rule
        /// down a second time, and the obvious ones are wrong in this runtime: Mono rounds the float's
        /// shortest form half away from zero, so 0.0049 prints "0 %" while 0.005 prints "1 %" (measured
        /// 2026-11-15), and a rule like <c>percent01 &lt; 0.01f</c> would paint a shot red while it reads
        /// "1 %". Comparing the two strings cannot disagree with the text, whatever the runtime rounds.
        ///
        /// Called once per shutter press (never per frame); the comparison string is built once.
        /// </summary>
        public static bool PrintsZero(float percent01) => Percent(percent01) == ZeroPercent;

        private static readonly string ZeroPercent = Percent(0f);

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

            // ⚠️ THE SCORE DECIDES WHETHER THERE IS ANYTHING TO FIX — NOT THE RAW OFFSET.
            //
            // This used to test `Abs(offset) < 0.1f` first, with a hardcoded tenth of a second that knew
            // nothing about GradingConfig.timingFullSeconds. The shipped config awards a flat Timing01 = 1
            // anywhere inside ±0.5s (ShotGrader.Timing: `if (distance <= fullSeconds) return 1f`), so across
            // 80% of the full-marks band the readout printed "timing 100%" on the axes line and
            // "0.2s early — wait for it" on the line directly beneath it: the player was told to change the
            // one thing that cost them nothing. Found by the 2026-08-07 code review and reproduced against
            // the real config — 7 of 12 sampled offsets contradicted themselves, and the run data had
            // already recorded one (hud.txt:345, `timing 100 % @ peak +0.15s`) without anyone photographing
            // that band.
            //
            // Gating on Timing01 instead ties the advice to the number actually shown, so the two lines
            // cannot disagree however the designer retunes the window — including the mirror case, a
            // timingFullSeconds smaller than the old deadband, where "right on the moment" used to be
            // printed over a timing score of 26%.
            if (grade.Timing01 >= 1f) return "right on the moment";

            float offset = grade.PeakOffset;

            // Below a tenth of a second there is no advice worth printing: `{0.0}` rounds to "0.0s early",
            // which is a criticism with no magnitude behind it. Say NOTHING rather than that — and note
            // this can only be reached when timing scored less than full marks, so "right on the moment"
            // would be a lie here rather than a compliment.
            if (Mathf.Abs(offset) < 0.1f) return string.Empty;

            // Positive is EARLY and negative is LATE (ISubject.PeakOffset). Stated as the player's mistake
            // rather than as a signed number: "-1.4s" is a debug value, "1.4s late" is feedback.
            //
            // ⚠️ INVARIANT CULTURE, LIKE <see cref="Percent"/>. `{x:0.0}` follows the player's locale, so in
            // fr-FR, de-DE or tr-TR this line printed "2,4s late" directly under a breakdown that Percent
            // prints invariantly — one readout in two cultures (reproduced by the Story 1.19 code review).
            // In en-US the text is byte-identical to before.
            string seconds = (offset > 0f ? offset : -offset).ToString("0.0", CultureInfo.InvariantCulture);
            return offset > 0f
                ? seconds + "s early — wait for it"
                : seconds + "s late — shoot sooner";
        }
    }
}
