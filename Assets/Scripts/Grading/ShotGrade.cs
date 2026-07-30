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

        /// <summary>True when the shot passed every gate and the score is meaningful.</summary>
        public bool Counted => MissReason == GradeMiss.None && !IsPlaceholder;

        /// <summary>True when the shutter caught nothing gradeable. Distinct from a low
        /// <see cref="Stars"/>: a miss and a bad shot both read 1★.</summary>
        public bool IsMiss => MissReason != GradeMiss.None && MissReason != GradeMiss.Unevaluated;

        private ShotGrade(float percent01, bool isPlaceholder, GradeMiss missReason,
                          float subject01, float composition01, float timing01, int stars)
        {
            Percent01 = Sane(percent01);
            IsPlaceholder = isPlaceholder;
            MissReason = missReason;
            Subject01 = Sane(subject01);
            Composition01 = Sane(composition01);
            Timing01 = Sane(timing01);
            Stars = Mathf.Clamp(stars, 1, 5);
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
        public static ShotGrade Scored(float subject01, float composition01, float timing01, StarScale scale)
        {
            float percent = Sane(composition01) * Sane(timing01);
            return new ShotGrade(percent, isPlaceholder: false, GradeMiss.None,
                                 subject01, composition01, timing01, scale.StarsFor(percent));
        }

        /// <summary>A real grade from a normalized score in [0,1], with no breakdown. Retained for callers
        /// that genuinely have only a total; prefer <see cref="Scored"/>, which fills in the axes the HUD
        /// needs.</summary>
        public static ShotGrade FromPercent(float p01) =>
            new ShotGrade(p01, isPlaceholder: false, GradeMiss.None, 0f, 0f, 0f,
                          StarScale.Default.StarsFor(Sane(p01)));

        /// <summary>A rejected shot (0%), carrying the gate that rejected it. Always 1★ — the floor of the
        /// GDD's scale — whatever the thresholds are, which is why it needs no <see cref="StarScale"/>.</summary>
        public static ShotGrade Missed(GradeMiss reason) =>
            new ShotGrade(0f, isPlaceholder: false, reason, 0f, 0f, 0f, 1);

        /// <summary>A clearly-temporary grade used by Story 1.5 when grading is not configured.</summary>
        public static ShotGrade Placeholder =>
            new ShotGrade(0f, isPlaceholder: true, GradeMiss.Unevaluated, 0f, 0f, 0f, 1);

        /// <summary>One line carrying the total AND the breakdown. A bare "62%" tells a designer nothing
        /// about which axis cost them the shot, which is the whole reason the axes are on this struct.</summary>
        public override string ToString()
        {
            if (IsPlaceholder) return "ShotGrade(placeholder)";
            if (IsMiss) return $"ShotGrade(miss:{MissReason})";

            return $"ShotGrade({Percent01:P0}, {Stars}★ — composition {Composition01:P0} × " +
                   $"timing {Timing01:P0}; subject seen {Subject01:P0})";
        }
    }
}
