using UnityEngine;

namespace CameraGame.Grading
{
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

        /// <summary>Composition axis in [0,1]: the prominence sweet spot, rule-of-thirds placement and the
        /// frame-edge cut-off penalty, combined (FR6).</summary>
        public readonly float Composition01;

        /// <summary>Timing axis in [0,1]: how close to the peak WINDOW the shutter fell (FR7).</summary>
        public readonly float Timing01;

        /// <summary>Star rating 1–5, derived from <see cref="Percent01"/>. A miss (0%) still reads as 1
        /// star — the GDD's scale starts at one — which is exactly why <see cref="MissReason"/> exists.</summary>
        public int Stars => Mathf.Clamp(Mathf.CeilToInt(Percent01 * 5f), 1, 5);

        /// <summary>True when the shot passed every gate and the score is meaningful.</summary>
        public bool Counted => MissReason == GradeMiss.None && !IsPlaceholder;

        /// <summary>True when the shutter caught nothing gradeable. Distinct from a low
        /// <see cref="Stars"/>: a miss and a bad shot both read 1★.</summary>
        public bool IsMiss => MissReason != GradeMiss.None && MissReason != GradeMiss.Unevaluated;

        private ShotGrade(float percent01, bool isPlaceholder, GradeMiss missReason,
                          float subject01, float composition01, float timing01)
        {
            Percent01 = Sane(percent01);
            IsPlaceholder = isPlaceholder;
            MissReason = missReason;
            Subject01 = Sane(subject01);
            Composition01 = Sane(composition01);
            Timing01 = Sane(timing01);
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
        public static ShotGrade Scored(float subject01, float composition01, float timing01) =>
            new ShotGrade(Sane(composition01) * Sane(timing01), isPlaceholder: false,
                          GradeMiss.None, subject01, composition01, timing01);

        /// <summary>A real grade from a normalized score in [0,1], with no breakdown. Retained for callers
        /// that genuinely have only a total; prefer <see cref="Scored"/>, which fills in the axes the HUD
        /// needs.</summary>
        public static ShotGrade FromPercent(float p01) =>
            new ShotGrade(p01, isPlaceholder: false, GradeMiss.None, 0f, 0f, 0f);

        /// <summary>A rejected shot (0%), carrying the gate that rejected it.</summary>
        public static ShotGrade Missed(GradeMiss reason) =>
            new ShotGrade(0f, isPlaceholder: false, reason, 0f, 0f, 0f);

        /// <summary>A clearly-temporary grade used by Story 1.5 when grading is not configured.</summary>
        public static ShotGrade Placeholder =>
            new ShotGrade(0f, isPlaceholder: true, GradeMiss.Unevaluated, 0f, 0f, 0f);

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
