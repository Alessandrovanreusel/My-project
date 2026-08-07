using NUnit.Framework;
using UnityEngine;
using CameraGame.UI;

namespace CameraGame.Tests
{
    /// <summary>
    /// Regression pins for <see cref="GradeHudConfig"/> (Story 1.12, Task 3).
    ///
    /// ⚠️ WHAT THESE ARE FOR. This project's single most repeated failure mode is a hand-authored number
    /// that disables a feature SILENTLY — <c>cueRadius = 0</c>, <c>minCoverage = NaN</c>,
    /// <c>minVisibleSamples = 0</c>, <c>timingFullSeconds = 0</c>, <c>cellSize.x = 0</c>, five times over
    /// five stories, each with a completely clean console. The assets are hand-written YAML, so
    /// <c>[Range]</c> never runs on them. Everything below is a pin on the two defences that remain: the
    /// <c>Safe*</c> accessors, and a validator that describes what the designer actually typed.
    ///
    /// Nothing here asserts that 2.2 seconds is the RIGHT hold. That is a question for eyes and it goes to
    /// Alexv with photographs (AC3).
    ///
    /// Configs are built with <c>CreateInstance</c> and never loaded from the shipped asset, so a test run
    /// can never mutate <c>Assets/Data/UI/GradeHudConfig.asset</c>.
    /// </summary>
    public class GradeHudConfigTests
    {
        private GradeHudConfig _cfg;

        [SetUp]
        public void SetUp() => _cfg = ScriptableObject.CreateInstance<GradeHudConfig>();

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_cfg);

        // Contract: "Safe*, guaranteed usable however the asset was authored." The zero is the one that
        // matters — a zero hold is a readout that appears for a single frame and is never read.
        [TestCase(0f)]
        [TestCase(-5f)]
        [TestCase(0.001f)]
        [TestCase(9999f)]
        public void SafeHoldSeconds_IsAlwaysUsable(float authored)
        {
            _cfg.holdSeconds = authored;

            Assert.That(_cfg.SafeHoldSeconds,
                Is.InRange(GradeHudConfig.MinHoldSeconds, GradeHudConfig.MaxHoldSeconds));
        }

        [TestCase(-1f)]
        [TestCase(9999f)]
        public void SafeFadeSeconds_IsAlwaysUsable(float authored)
        {
            _cfg.fadeSeconds = authored;

            Assert.That(_cfg.SafeFadeSeconds,
                Is.InRange(GradeHudConfig.MinFadeSeconds, GradeHudConfig.MaxFadeSeconds));
        }

        // Contract: a deliberate in-range value must SURVIVE. A clamp that quietly snapped everything back
        // to the design default would make the config a decoration.
        [Test]
        public void SafeAccessors_LeaveDeliberateInRangeValuesAlone()
        {
            _cfg.holdSeconds = 5f;
            _cfg.fadeSeconds = 1.25f;

            Assert.That(_cfg.SafeHoldSeconds, Is.EqualTo(5f).Within(1e-5f));
            Assert.That(_cfg.SafeFadeSeconds, Is.EqualTo(1.25f).Within(1e-5f));
        }

        // ⚠️ THE DOCUMENTED TRAP: "Mathf.Clamp(NaN, a, b) RETURNS NaN — every comparison against NaN is
        // false, so both branches fall through." A NaN hold makes the countdown NaN forever, so the readout
        // never leaves the screen, at an alpha of NaN. This is exactly the minCoverage = NaN bug from Story
        // 1.9 wearing a different hat, and ClampFinite is the only thing standing in its way.
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void SafeAccessors_NeverReturnANonFiniteNumber(float authored)
        {
            _cfg.holdSeconds = authored;
            _cfg.fadeSeconds = authored;

            Assert.IsFalse(float.IsNaN(_cfg.SafeHoldSeconds), "a NaN hold never counts down");
            Assert.IsFalse(float.IsInfinity(_cfg.SafeHoldSeconds));
            Assert.IsFalse(float.IsNaN(_cfg.SafeFadeSeconds));
            Assert.IsFalse(float.IsInfinity(_cfg.SafeFadeSeconds));
            Assert.IsFalse(float.IsNaN(_cfg.SafeVisibleSeconds));
        }

        // Contract: an alpha-0 colour "would be invisible on screen while reading as a perfectly valid
        // colour in the Inspector" — the silent-nothing class, one channel over from the numbers.
        [Test]
        public void SafeColours_AreNeverInvisible()
        {
            _cfg.countedColor = new Color(1f, 1f, 1f, 0f);
            _cfg.missColor = new Color(1f, 0f, 0f, 0.001f);
            _cfg.placeholderColor = new Color(0.5f, 0.5f, 0.5f, 0f);

            Assert.That(_cfg.SafeCountedColor.a, Is.GreaterThanOrEqualTo(GradeHudConfig.MinVisibleAlpha));
            Assert.That(_cfg.SafeMissColor.a, Is.GreaterThanOrEqualTo(GradeHudConfig.MinVisibleAlpha));
            Assert.That(_cfg.SafePlaceholderColor.a, Is.GreaterThanOrEqualTo(GradeHudConfig.MinVisibleAlpha));

            // ...and the HUE the designer chose is preserved. Forcing alpha must not also repaint the text.
            Assert.That(_cfg.SafeMissColor.r, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(_cfg.SafeMissColor.g, Is.EqualTo(0f).Within(1e-5f));
        }

        // Contract: the shipped defaults are sane, so a freshly created asset warns about nothing. If this
        // fails, every play session starts with a warning nobody can act on — which is how a console full
        // of noise begins (NFR5).
        [Test]
        public void FreshConfig_ReportsNoProblem()
        {
            Assert.IsFalse(_cfg.TryGetConfigProblem(out string problem), problem);
            Assert.IsNull(problem);
        }

        [TestCase(0f)]
        [TestCase(-2f)]
        [TestCase(float.NaN)]
        public void TryGetConfigProblem_ReportsAnUnusableHold(float authored)
        {
            _cfg.holdSeconds = authored;

            Assert.IsTrue(_cfg.TryGetConfigProblem(out string problem),
                "an unusable hold must not reach a designer as a clean console");
            StringAssert.Contains("holdSeconds", problem);
        }

        [Test]
        public void TryGetConfigProblem_ReportsANegativeFade()
        {
            _cfg.fadeSeconds = -1f;

            Assert.IsTrue(_cfg.TryGetConfigProblem(out string problem));
            StringAssert.Contains("fadeSeconds", problem);
        }

        // Contract: the SILENT one — "everything in range, nothing looks wrong, and the readout for one
        // capture is still on screen several captures later."
        [Test]
        public void TryGetConfigProblem_ReportsAReadoutThatOutlivesTheNextShot()
        {
            _cfg.holdSeconds = GradeHudConfig.MaxHoldSeconds;
            _cfg.fadeSeconds = GradeHudConfig.MaxFadeSeconds;

            Assert.That(_cfg.SafeVisibleSeconds, Is.GreaterThan(GradeHudConfig.LingerWarnSeconds),
                "fixture precondition: both values in range but the total over budget");
            Assert.IsTrue(_cfg.TryGetConfigProblem(out string problem));
            StringAssert.Contains("on screen", problem);
        }

        // ⚠️ FOUND BY RUNNING IT (2026-08-07). The explanation used to be one fixed sentence per field, so
        // the verification run printed "a hold at or below zero is a readout that appears for a single
        // frame" underneath a hold of 9999, and again underneath a hold of NaN. The number was right and
        // the advice described neither mistake. A validator whose explanation is wrong is worse than one
        // that only states the number, because the reader stops trusting the part that was right.
        [Test]
        public void TryGetConfigProblem_ExplainsTheMistakeThatWasActuallyMade()
        {
            _cfg.holdSeconds = 0f;
            _cfg.TryGetConfigProblem(out string tooShort);
            StringAssert.Contains("barely a frame", tooShort);

            _cfg.holdSeconds = 9999f;
            _cfg.TryGetConfigProblem(out string tooLong);
            StringAssert.Contains("several captures later", tooLong);
            Assert.IsFalse(tooLong.Contains("barely a frame"),
                "a hold of 9999 is not a readout that appears for barely a frame");

            _cfg.holdSeconds = float.NaN;
            _cfg.TryGetConfigProblem(out string notFinite);
            StringAssert.Contains("never counts down", notFinite);
            Assert.IsFalse(notFinite.Contains("barely a frame"));

            _cfg.holdSeconds = 2.2f;
            _cfg.fadeSeconds = 9999f;
            _cfg.TryGetConfigProblem(out string slowFade);
            Assert.IsFalse(slowFade.Contains("backwards"),
                "a fade of 9999 does not drive the alpha ramp backwards");
        }

        [Test]
        public void TryGetConfigProblem_ReportsAnInvisibleTextColour()
        {
            _cfg.missColor = new Color(1f, 0.5f, 0.4f, 0f);

            Assert.IsTrue(_cfg.TryGetConfigProblem(out string problem));
            StringAssert.Contains("missColor", problem);
        }

        // ⚠️ THE 1.11 LESSON, PINNED. GalleryConfig's validator returned on the FIRST problem, so a designer
        // with three mistakes needed three play-mode cycles to find them — and OnValidate could destroy the
        // evidence for the later ones in between. That is a standing deferred item against two configs; a
        // new validator has no reason to inherit it.
        [Test]
        public void TryGetConfigProblem_ReportsEveryProblemAtOnceNotJustTheFirst()
        {
            _cfg.holdSeconds = 0f;
            _cfg.fadeSeconds = -1f;
            _cfg.countedColor = new Color(1f, 1f, 1f, 0f);

            Assert.IsTrue(_cfg.TryGetConfigProblem(out string problem));
            StringAssert.Contains("holdSeconds", problem);
            StringAssert.Contains("fadeSeconds", problem);
            StringAssert.Contains("countedColor", problem);
        }

        // ⚠️ THE OnValidate TRAP, PINNED STRUCTURALLY. GalleryConfig repaired its raw fields in OnValidate,
        // which Unity runs on asset load — BEFORE any Awake — so the branch that compares raw against Safe*
        // could never fire again and the warning was silently disabled exactly where designers author. This
        // pins that the raw field survives untouched, which is the property the whole validator rests on.
        // (Unity does not call OnValidate on a CreateInstance object, so this cannot catch the editor path
        // directly — what it does catch is a reader that "helpfully" writes back through a Safe* accessor.)
        [Test]
        public void ReadingSafeAccessors_DoesNotRewriteTheAuthoredFields()
        {
            _cfg.holdSeconds = 0f;
            _cfg.fadeSeconds = -1f;

            _ = _cfg.SafeHoldSeconds;
            _ = _cfg.SafeFadeSeconds;
            _ = _cfg.SafeVisibleSeconds;
            _cfg.TryGetConfigProblem(out _);

            Assert.That(_cfg.holdSeconds, Is.EqualTo(0f),
                "the authored value must still be there for the warning to describe");
            Assert.That(_cfg.fadeSeconds, Is.EqualTo(-1f));
        }
    }
}
