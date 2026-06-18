using UnityEngine;

namespace CameraGame.Grading
{
    /// <summary>
    /// The result of grading a captured shot — the SHARED score payload carried by
    /// <c>ShotCapturedChannel</c> (Story 1.5). It is the <em>grade</em>, not the image:
    /// image persistence is the gallery's job (Story 1.11).
    ///
    /// Story 1.5 only raises <see cref="Placeholder"/>; the real per-axis breakdown and the
    /// <c>ShotGrader</c> that produces honest grades land in Stories 1.9–1.10. Kept as pure data
    /// (a readonly struct, no Unity component deps, no references to other feature folders) so the
    /// <c>Events</c> assembly can reference it without creating a dependency cycle (AR5).
    /// </summary>
    public readonly struct ShotGrade
    {
        /// <summary>Normalized quality in [0,1]. 0 = miss, 1 = perfect.</summary>
        public readonly float Percent01;

        /// <summary>True while this is a temporary stand-in (Story 1.5) until real grading exists.</summary>
        public readonly bool IsPlaceholder;

        /// <summary>Star rating 1–5, derived from <see cref="Percent01"/>. A miss (0%) still reads as 1 star.</summary>
        public int Stars => Mathf.Clamp(Mathf.CeilToInt(Percent01 * 5f), 1, 5);

        private ShotGrade(float percent01, bool isPlaceholder)
        {
            Percent01 = float.IsNaN(percent01) ? 0f : Mathf.Clamp01(percent01);
            IsPlaceholder = isPlaceholder;
        }

        /// <summary>A real grade from a normalized score in [0,1].</summary>
        public static ShotGrade FromPercent(float p01) => new ShotGrade(p01, isPlaceholder: false);

        /// <summary>A complete miss (0%).</summary>
        public static ShotGrade Miss => FromPercent(0f);

        /// <summary>A clearly-temporary grade used by Story 1.5 until grading lands (1.9–1.10).</summary>
        public static ShotGrade Placeholder => new ShotGrade(0f, isPlaceholder: true);

        public override string ToString() =>
            $"ShotGrade({Percent01:P0}, {Stars}★{(IsPlaceholder ? ", placeholder" : "")})";
    }
}
