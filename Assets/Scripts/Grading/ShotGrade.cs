using UnityEngine;

namespace CameraGame.Grading
{
    /// <summary>
    /// Where the 1–5 star boundaries sit on the 0–1 grade. Designer-facing, so the real values live in
    /// <see cref="GradingConfig"/> — this struct is just the resolved, guaranteed-descending set the
    /// grader is handed, kept as pure data so <see cref="ShotGrade"/> stays free of Unity object refs.
    ///
    /// ⚠️ WHY THIS EXISTS. Stars used to be <c>CeilToInt(Percent01 × 5)</c>, which silently hardcodes
    /// "80% is a perfect photograph" — a *designer's* decision sitting in code as a magic number, which is
    /// exactly what this project's rules say must not happen. It had a visible consequence: the
    /// 2026-07-28 shoot produced two 5★ photographs, `z3_money_shot` at 100% and `x_time_peak_start` at
    /// 85%, and Alexv judged the first clearly the better picture. The scoring was right all along — it
    /// ranked them 100 vs 85 — but the quantizer collapsed a fifteen-point gap into one rating.
    /// </summary>
    public readonly struct StarScale
    {
        /// <summary>Minimum grade for 5★, 4★, 3★, 2★. Anything below <see cref="Two"/> is 1★ — the GDD's
        /// scale starts at one star and there is no 0★.</summary>
        public readonly float Five, Four, Three, Two;

        /// <summary>The shipped tuning, and the fallback when a config cannot supply one. Set against the
        /// 2026-07-28 shoot: it moves `x_time_peak_start` from 5★ to 4★ and leaves all twenty-eight other
        /// photographs exactly where they were.</summary>
        public static StarScale Default => new StarScale(0.90f, 0.70f, 0.45f, 0.20f);

        public StarScale(float five, float four, float three, float two)
        {
            Five = five; Four = four; Three = three; Two = two;
        }

        /// <summary>The star rating for a normalized grade. NaN reads as the floor rather than throwing or
        /// propagating — every comparison against NaN is false, so the fall-through would give 1★ anyway;
        /// this just makes the intent explicit.</summary>
        public int StarsFor(float percent01)
        {
            if (float.IsNaN(percent01)) return 1;
            if (percent01 >= Five) return 5;
            if (percent01 >= Four) return 4;
            if (percent01 >= Three) return 3;
            if (percent01 >= Two) return 2;
            return 1;
        }
    }

    /// <summary>
    /// The result of grading a captured shot — the SHARED score payload carried by
    /// <c>ShotCapturedChannel</c> (Story 1.5). It is the <em>grade</em>, not the image:
    /// image persistence is the gallery's job (Story 1.11).
    ///
    /// Kept as pure data (a readonly struct, no Unity component deps, no references to other feature
    /// folders) so the <c>Events</c> assembly can reference it without creating a dependency cycle (AR5).
    ///
    /// ⚠️ THE BREAKDOWN IS NOT EDITOR-ONLY. <see cref="Subject01"/>/<see cref="Composition01"/>/
    /// <see cref="Timing01"/> and <see cref="MissReason"/> are plain fields on purpose, outside any
    /// <c>#if UNITY_EDITOR</c>: Story 1.12's HUD has to tell the player WHY a shot scored what it did, and
    /// a breakdown that only exists in the editor would leave the shipped HUD with a bare percentage.
    /// (Story 1.9's only breakdown lived in the editor-only <c>GradeDetail</c>, which is what made this an
    /// explicit acceptance criterion.)
    /// </summary>
    public readonly struct ShotGrade
    {
        /// <summary>Normalized quality in [0,1]. 0 = miss, 1 = perfect. This is
        /// <see cref="Composition01"/> × <see cref="Timing01"/> for a counted shot — see
        /// <see cref="Scored"/> for why the blend is multiplicative.</summary>
        public readonly float Percent01;

        /// <summary>True while this is a temporary stand-in (Story 1.5) until real grading exists.</summary>
        public readonly bool IsPlaceholder;

        /// <summary>Why the shot was rejected, or <see cref="GradeMiss.None"/> when it counted.
        /// Carried on the grade itself — not just on the editor-only <c>GradeDetail</c> — so the HUD and
        /// the gallery can tell a MISS apart from a merely weak shot. Both read 1★ (the GDD's scale starts
        /// at one star and there is no 0★), so without this they are indistinguishable.</summary>
        public readonly GradeMiss MissReason;

        /// <summary>Subject axis: the fraction of line-of-sight samples that were clear, for a shot that
        /// passed the gate. 0 for a miss.
        ///
        /// Note this is a REPORT, not a multiplier: the subject check is a GATE — it either passes or the
        /// whole shot is a miss — so it does not scale <see cref="Percent01"/>. It is here because "how
        /// much of him could the camera actually see" is the number a player needs when a shot they thought
        /// was good came back weak.</summary>
        public readonly float Subject01;

        /// <summary>Composition axis in [0,1]: the prominence sweet spot, centred placement and the
        /// frame-edge cut-off penalty, combined (FR6).</summary>
        public readonly float Composition01;

        /// <summary>Timing axis in [0,1]: how close to the peak WINDOW the shutter fell (FR7).</summary>
        public readonly float Timing01;

        /// <summary>Star rating 1–5, resolved from <see cref="Percent01"/> against the designer's
        /// <see cref="StarScale"/> at the moment of grading. A miss (0%) still reads as 1 star — the GDD's
        /// scale starts at one — which is exactly why <see cref="MissReason"/> exists.
        ///
        /// A stored FIELD, not a computed property: the boundaries are config, and a grade that outlives
        /// the config it was scored against (a gallery entry, Story 1.11) must keep the rating the player
        /// was actually shown rather than silently re-rating itself when someone retunes the thresholds.</summary>
        public readonly int Stars;

        /// <summary>
        /// Which subject this grade is OF — <c>ISubject.SubjectId</c>, e.g. "TownDrunk". Added by Story
        /// 1.11 so the gallery can record what a stored photograph is a photograph of, without the channel
        /// payload growing a Unity object reference and without the gallery reaching back into
        /// <c>PhotoModeController</c> (AR4). A string, so this struct stays pure data.
        ///
        /// ⚠️ EMPTY MEANS "THERE WAS NO SUBJECT", and that is the whole point of it being empty rather than
        /// a <c>"None"</c>/<c>"Unknown"</c> sentinel: a sentinel string is indistinguishable from a real
        /// event id the moment somebody authors an event called "None", and the reader has no way to tell a
        /// missing measurement from a measured one. Same discipline as
        /// <see cref="GradeDetail.NotEvaluated"/> and <see cref="GradeMiss.Unevaluated"/>.
        ///
        /// A MISS may still carry an id, and should: a shot rejected as <see cref="GradeMiss.Occluded"/> or
        /// <see cref="GradeMiss.TooSmall"/> was rejected against a subject that really was there, and "the
        /// drunk, behind a wall" is more truthful than "nobody". Only the gates that run before a subject is
        /// known — NoCamera, NoConfig, NoSubject — leave it empty. <see cref="GradeMiss.NoViewport"/> is
        /// raised AFTER the subject has been resolved and therefore does carry the id (ShotGrader).
        ///
        /// ⚠️ NOT ALWAYS NON-NULL, DESPITE THE CONSTRUCTOR NORMALIZING. This comment used to promise that
        /// <c>default(ShotGrade).SubjectId</c> was <c>""</c> and that callers never needed a null check.
        /// That is false and the 2026-07-30 review proved it by running it: <c>default(T)</c> on a struct
        /// zero-initialises the memory and runs NO constructor, so the field is null. Any zeroed grade
        /// reaches you that way — <c>new ShotGrade[n]</c>, an unassigned struct field, a <c>default</c>
        /// switch arm, a failed <c>TryGetValue</c>.
        ///
        /// Use <see cref="HasSubject"/> (null-safe) rather than touching this string directly. It matters
        /// most for Epic 5: <c>CapturedShot</c>'s class comment instructs the next author to map this field
        /// into a JSON DTO, and <c>.Trim()</c> or <c>.Length</c> on a defaulted grade would throw there.
        /// </summary>
        public readonly string SubjectId;

        /// <summary>
        /// Seconds from the peak WINDOW at the instant of the shutter — POSITIVE EARLY, 0 during the money
        /// shot, NEGATIVE LATE. <see cref="float.NaN"/> when timing was never read. Added by Story 1.12 so
        /// the HUD can say which way the player was wrong.
        ///
        /// ⚠️ WHY <see cref="Timing01"/> IS NOT ENOUGH. It names the axis that cost the shot but not the
        /// direction: 20% is the same number two seconds early and two seconds late, and "click sooner" is
        /// the only half a player can act on. This is the same value <c>GradeDetail.PeakOffset</c> carries
        /// for the editor overlay, promoted onto the shipped payload for the same reason
        /// <see cref="MissReason"/> and the three axes were (see this struct's header).
        ///
        /// ⚠️ DELIBERATELY NOT RUN THROUGH <see cref="Sane"/>, UNLIKE EVERY OTHER FLOAT HERE. That helper
        /// does NaN → 0 then Clamp01, which is right for a normalized score and catastrophic for a SIGNED
        /// VALUE IN SECONDS: it would turn "never measured" into "dead on the peak" — the precise lie AC2
        /// exists to prevent — and clamp a two-second miss to 1. Stored RAW, with NaN kept as the
        /// never-measured sentinel, exactly as <c>GradeDetail.PeakOffset</c> does.
        ///
        /// ⚠️ NEVER READ THIS FIELD DIRECTLY TO PRINT IT. A zeroed <c>default(ShotGrade)</c> — from
        /// <c>new ShotGrade[n]</c>, an unassigned struct field, a failed <c>TryGetValue</c> — has 0 here
        /// and would read as perfect timing. Go through <see cref="TimingMeasured"/> or
        /// <see cref="PeakOffsetText"/>, which check the miss reason as well as the value. Same discipline
        /// as <c>GradeDetail.OcclusionTested</c>.
        ///
        /// It is a plain <c>float</c>, so <c>CapturedShot</c>'s shape is unchanged and Epic 5's JSON DTO
        /// seam is untouched — which the existing <c>DateTime</c> field would not have been.
        /// </summary>
        public readonly float PeakOffset;

        /// <summary>True when this grade names the subject it was measured against. False for a shot that
        /// never had one — see <see cref="SubjectId"/>.</summary>
        public bool HasSubject => !string.IsNullOrEmpty(SubjectId);

        /// <summary>True only when the shutter's distance from the peak was actually measured. Requires a
        /// COUNTED shot as well as a finite value: grading early-outs at the first failed gate, so every
        /// miss reaches the reader with no timing measurement at all, and a zeroed default is not a
        /// measurement either. See <see cref="PeakOffset"/>.</summary>
        public bool TimingMeasured =>
            Counted && !float.IsNaN(PeakOffset) && !float.IsInfinity(PeakOffset);

        /// <summary>Peak offset for display, or "n/a" when there is no usable measurement. Mirrors
        /// <c>GradeDetail.PeakOffsetText</c> so the shipped payload and the editor overlay cannot print the
        /// same number two different ways.</summary>
        public string PeakOffsetText =>
            TimingMeasured ? $"{PeakOffset:+0.00;-0.00;0.00}s" : "n/a";

        /// <summary>True when the shot passed every gate and the score is meaningful.</summary>
        public bool Counted => MissReason == GradeMiss.None && !IsPlaceholder;

        /// <summary>True when the shutter caught nothing gradeable. Distinct from a low
        /// <see cref="Stars"/>: a miss and a bad shot both read 1★.</summary>
        public bool IsMiss => MissReason != GradeMiss.None && MissReason != GradeMiss.Unevaluated;

        private ShotGrade(float percent01, bool isPlaceholder, GradeMiss missReason,
                          float subject01, float composition01, float timing01, int stars,
                          string subjectId, float peakOffset)
        {
            Percent01 = Sane(percent01);
            IsPlaceholder = isPlaceholder;
            MissReason = missReason;
            Subject01 = Sane(subject01);
            Composition01 = Sane(composition01);
            Timing01 = Sane(timing01);
            Stars = Mathf.Clamp(stars, 1, 5);

            // ⚠️ RAW, not Sane(). This is the one float on this struct that is signed, is measured in
            // seconds, and uses NaN as a sentinel — see PeakOffset for why passing it through Sane would
            // turn "never measured" into "perfectly timed".
            PeakOffset = peakOffset;

            // Normalized here and nowhere else, so no reader ever has to defend itself against null. The
            // same reason Sane() sits on this line: a struct that guarantees its own invariants cannot be
            // constructed into a state that surprises the gallery or the HUD.
            SubjectId = subjectId ?? string.Empty;
        }

        /// <summary>Every score that enters this struct passes through here. Mathf.Clamp01(NaN) is NaN, so
        /// clamping alone would let one NaN sub-score propagate into the HUD and the gallery — the same
        /// guard <see cref="Percent01"/> has carried since Story 1.5, now applied to all four axes.</summary>
        private static float Sane(float v) => float.IsNaN(v) ? 0f : Mathf.Clamp01(v);

        /// <summary>
        /// A real grade from the three axes (Story 1.10).
        ///
        /// The blend is MULTIPLICATIVE (composition × timing), following the architecture sketch. That is a
        /// design statement, not an accident: this game's first pillar is being there at the right second,
        /// so a beautifully framed shot two seconds late genuinely is not the shot. It also means five stars
        /// requires BOTH axes to be near 1, which is why the sweet spot has to be authored so it actually
        /// reaches 1.0 — <c>GradingConfig.TryGetConfigProblem</c> warns when it cannot.
        /// </summary>
        /// <param name="subjectId">Who was photographed (<c>ISubject.SubjectId</c>). A counted shot always
        /// has one — it passed every gate, so a subject was certainly measured.</param>
        /// <param name="peakOffset">Seconds from the peak window at the shutter (+ early, − late), or
        /// <see cref="float.NaN"/> if the subject reported no usable timing. REQUIRED rather than
        /// defaulted: a caller that forgets it would silently ship "dead on the peak" on every shot, and
        /// this struct's whole discipline is that a number nobody measured is never stated.</param>
        public static ShotGrade Scored(float subject01, float composition01, float timing01, StarScale scale,
                                       string subjectId, float peakOffset)
        {
            float percent = Sane(composition01) * Sane(timing01);
            return new ShotGrade(percent, isPlaceholder: false, GradeMiss.None,
                                 subject01, composition01, timing01, scale.StarsFor(percent), subjectId,
                                 peakOffset);
        }

        // ⚠️ THERE IS DELIBERATELY NO `FromPercent` HERE ANY MORE, AND IT MUST NOT COME BACK.
        //
        // It built a grade from a bare total with an all-zero breakdown and `Counted == true`, so
        // ToString() printed `ShotGrade(62%, 4★ — composition 0% × timing 0%)` — a grade asserting two
        // measurements it never took, which is the exact failure Story 1.12's AC2 exists to prevent, on the
        // one readout the PLAYER sees. It had zero production callers from the moment `Scored` landed in
        // Story 1.10 (confirmed again across all of Assets/ before removal), and deferred-work.md assigned
        // the decision to Story 1.12: "delete it, or make it non-Counted, when the HUD lands."
        //
        // Deleted rather than made non-Counted, because there is no caller that genuinely has only a total.
        // Anything that scores a shot has the axes — that is what `Scored` takes — and anything that does
        // not has a miss, which is what `Missed` is for. A third factory would only be a way to construct
        // the state AC2 forbids.

        /// <summary>A rejected shot (0%), carrying the gate that rejected it. Always 1★ — the floor of the
        /// GDD's scale — whatever the thresholds are, which is why it needs no <see cref="StarScale"/>.
        ///
        /// <paramref name="subjectId"/> defaults to empty, which is correct for the gates that reject
        /// BEFORE the subject is known (NoCamera, NoConfig, NoSubject). Every gate that runs after it is
        /// resolved should pass the id — the ones that measured the subject (TooSmall, Occluded,
        /// OutsideFrustum, BehindCamera, DegenerateBounds) and NoViewport, which is raised further down
        /// ShotGrader and does carry it. "The drunk, behind a wall" is a more truthful gallery entry than
        /// "nobody". See <see cref="SubjectId"/>.
        ///
        /// (This list said NoViewport was empty while the code passed the id. The 2026-07-30 review caught
        /// the contradiction; the CODE was right, so the doc moved.)</summary>
        ///
        /// The peak offset is NaN for every miss without exception: grading early-outs at the first failed
        /// gate and timing is read LAST, after the size and occlusion gates, so no rejected shot has ever
        /// had its distance from the peak measured.
        public static ShotGrade Missed(GradeMiss reason, string subjectId = null) =>
            new ShotGrade(0f, isPlaceholder: false, reason, 0f, 0f, 0f, 1, subjectId, float.NaN);

        /// <summary>A clearly-temporary grade used by Story 1.5 when grading is not configured. Nothing was
        /// measured, so the peak offset is NaN like every other axis is zero — and
        /// <see cref="IsPlaceholder"/> is what stops any of it being read as a score.</summary>
        public static ShotGrade Placeholder =>
            new ShotGrade(0f, isPlaceholder: true, GradeMiss.Unevaluated, 0f, 0f, 0f, 1, string.Empty,
                          float.NaN);

        /// <summary>One line carrying the total AND the breakdown. A bare "62%" tells a designer nothing
        /// about which axis cost them the shot, which is the whole reason the axes are on this struct.</summary>
        public override string ToString()
        {
            // "of nobody" rather than an empty gap, so a log line never silently omits the fact that there
            // was no subject — the same reason GradeDetail prints "n/a" instead of a fabricated 0.
            string who = HasSubject ? $"of {SubjectId}" : "of nobody";

            if (IsPlaceholder) return "ShotGrade(placeholder)";
            if (IsMiss) return $"ShotGrade(miss:{MissReason} {who})";

            // The peak offset goes through PeakOffsetText, which prints "n/a" rather than a fabricated
            // 0.00s — the same rule every other number on this line already follows.
            return $"ShotGrade({Percent01:P0}, {Stars}★ {who} — composition {Composition01:P0} × " +
                   $"timing {Timing01:P0} @ peak {PeakOffsetText}; subject seen {Subject01:P0})";
        }
    }
}
