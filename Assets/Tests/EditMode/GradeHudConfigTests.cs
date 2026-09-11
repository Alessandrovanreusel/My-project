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

        // ⚠️ THE ONE AUTHORING MISTAKE THAT DEFEATED BOTH THE REPAIR AND THE VALIDATOR (2026-08-07 review).
        //
        // `Visible` tested `c.a < MinVisibleAlpha`, and `NaN < 0.05f` is FALSE — so a non-finite alpha was
        // waved through unrepaired while `ReportInvisible`, using the identical comparison, stayed silent.
        // Color32 converts NaN to 0, so the text rendered invisible or black with a completely clean
        // console: the silent-nothing shape this file exists to prevent, in the one member that had not
        // been given the `ClampFinite` treatment.
        //
        // This pins the BEHAVIOUR, not the spelling — but the only way to pass it is a comparison that is
        // true for NaN, i.e. the negated form. Reverting to `c.a < MinVisibleAlpha` fails here.
        [Test]
        public void SafeColours_RepairANonFiniteChannel()
        {
            _cfg.countedColor = new Color(1f, 1f, 1f, float.NaN);
            _cfg.missColor = new Color(float.NaN, 0.5f, 0.4f, 1f);
            _cfg.placeholderColor = new Color(float.NaN, float.NaN, float.NaN, float.NaN);

            foreach (var (name, c) in new[]
                     {
                         ("countedColor", _cfg.SafeCountedColor),
                         ("missColor", _cfg.SafeMissColor),
                         ("placeholderColor", _cfg.SafePlaceholderColor),
                     })
            {
                Assert.IsFalse(float.IsNaN(c.r), $"{name}.r is NaN — Color32 renders that as 0");
                Assert.IsFalse(float.IsNaN(c.g), $"{name}.g is NaN — Color32 renders that as 0");
                Assert.IsFalse(float.IsNaN(c.b), $"{name}.b is NaN — Color32 renders that as 0");
                Assert.IsFalse(float.IsNaN(c.a), $"{name}.a is NaN — the text would not be drawn at all");
                Assert.That(c.a, Is.GreaterThanOrEqualTo(GradeHudConfig.MinVisibleAlpha),
                    $"{name} is still invisible after repair");
            }

            // A finite channel beside a broken one keeps the hue the designer chose, exactly as the
            // alpha-0 repair above does.
            Assert.That(_cfg.SafeMissColor.g, Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(_cfg.SafeMissColor.b, Is.EqualTo(0.4f).Within(1e-5f));
        }

        // ⚠️ AC3, PINNED AT THE COLOUR LAYER (2026-09-11). Shown the three readouts with the world cropped
        // away, Alexv called the MISS "obvious" but said of the 5★ and the 0% pair: "it's not obvious which
        // one is which". Both counted shots were one cream colour. Colour was already what made the miss
        // unmistakable; banding gives the grade the same help.
        //
        // What is pinned is that the three bands are actually TELLABLE APART — from each other and from a
        // miss. A banding whose colours are near-identical would pass a "does it band?" test and fail the
        // player, which is the failure this whole check exists to catch.
        [Test]
        public void CountedColour_IsBandedByGrade_AndEveryBandIsDistinguishable()
        {
            Color strong = _cfg.CountedColorFor(5);
            Color mid    = _cfg.CountedColorFor(3);
            Color weak   = _cfg.CountedColorFor(1);

            Assert.AreEqual(strong, _cfg.CountedColorFor(4), "4 and 5 stars share the strong band");
            Assert.AreEqual(weak, _cfg.CountedColorFor(2), "1 and 2 stars share the weak band");
            Assert.AreEqual(_cfg.SafeCountedColor, mid, "3 stars keeps the original neutral colour");

            // Far enough apart to read at a glance on a translucent panel, not merely non-equal.
            const float MinSeparation = 0.25f;
            AssertApart(strong, weak, MinSeparation, "strong", "weak");
            AssertApart(strong, mid, MinSeparation, "strong", "mid");
            AssertApart(weak, mid, MinSeparation, "weak", "mid");

            // And a weak COUNTED shot must not read as a MISS — they are different outcomes, and that
            // distinction is the one Alexv confirmed already works.
            AssertApart(weak, _cfg.SafeMissColor, MinSeparation, "weak", "miss");
        }

        private static void AssertApart(Color a, Color b, float min, string an, string bn)
        {
            float d = Mathf.Sqrt((a.r - b.r) * (a.r - b.r) +
                                 (a.g - b.g) * (a.g - b.g) +
                                 (a.b - b.b) * (a.b - b.b));
            Assert.That(d, Is.GreaterThan(min),
                $"{an} {a} and {bn} {b} are only {d:0.00} apart — too close to tell at a glance");
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

        // The validator half of the same defect: the repair was silent because the report was silent. A
        // designer who types a non-finite channel must be told which field, whichever channel it landed in.
        [Test]
        public void TryGetConfigProblem_ReportsANonFiniteTextColour()
        {
            _cfg.missColor = new Color(1f, 0.5f, 0.4f, float.NaN);

            Assert.IsTrue(_cfg.TryGetConfigProblem(out string nanAlpha),
                "a NaN alpha is invisible text and the validator said nothing about it");
            StringAssert.Contains("missColor", nanAlpha);

            _cfg.missColor = new Color(float.NaN, 0.5f, 0.4f, 1f);

            Assert.IsTrue(_cfg.TryGetConfigProblem(out string nanChannel),
                "a NaN colour channel renders as 0 and the validator said nothing about it");
            StringAssert.Contains("missColor", nanChannel);
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
