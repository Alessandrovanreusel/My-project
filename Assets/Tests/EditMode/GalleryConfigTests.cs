using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using CameraGame.Gallery;

namespace CameraGame.Tests
{
    /// <summary>
    /// Regression pins for <see cref="GalleryConfig"/>'s pure sizing logic — the half of Story 1.11 that
    /// can be checked without rendering anything.
    ///
    /// The gallery rig already proved the visible behaviour (the thumbnail follows the window, eviction
    /// destroys textures, the cap holds). What it cannot do is notice that <c>ThumbnailWidthFor</c> stopped
    /// defending itself against a NaN aspect — because a rig runs with a real camera, which reports a real
    /// aspect, so the defensive path never executes. That is the gap these fill.
    /// </summary>
    public class GalleryConfigTests
    {
        private readonly List<GalleryConfig> _made = new List<GalleryConfig>();

        private GalleryConfig NewConfig()
        {
            var c = ScriptableObject.CreateInstance<GalleryConfig>();
            _made.Add(c);
            return c;
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var c in _made)
                if (c != null) Object.DestroyImmediate(c);
            _made.Clear();
        }

        // Contract: "NaN and infinity are handled explicitly rather than clamped: Mathf.Clamp(NaN) is NaN
        // and Mathf.RoundToInt(NaN) is 0, which would reach the Texture2D constructor as a zero width and
        // throw inside the shutter."
        //
        // A zero or negative width is the failure that matters — it throws in the one place NFR8 says
        // nothing may throw. So the assertion is on usability, not on a specific pixel count.
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(0f)]
        [TestCase(-1.777f)]
        public void ThumbnailWidthFor_UnusableAspect_StillProducesAUsableWidth(float aspect)
        {
            var c = NewConfig();

            int w = c.ThumbnailWidthFor(aspect);

            Assert.GreaterOrEqual(w, GalleryConfig.MinThumbnailSize,
                "a width below the minimum would not be a recognisable photograph");
            Assert.LessOrEqual(w, c.SafeMaxThumbnailWidth);
            Assert.AreEqual(c.ThumbnailWidthFor(GalleryConfig.FallbackAspect), w,
                "documented: an unusable aspect falls back to FallbackAspect");
        }

        // Contract: "The height is the authored quality dial; the WIDTH FOLLOWS THE WINDOW, so a stored
        // photograph frames what the player actually framed." This is the whole point of Story 1.11's
        // final change, so the relationship is pinned: wider window => wider (or equal, at the ceiling)
        // thumbnail, never narrower.
        [Test]
        public void ThumbnailWidthFor_IsMonotonicInAspect()
        {
            var c = NewConfig();

            int previous = 0;
            for (float aspect = 0.5f; aspect <= 3f; aspect += 0.05f)
            {
                int w = c.ThumbnailWidthFor(aspect);
                Assert.GreaterOrEqual(w, previous,
                    $"a wider window produced a NARROWER thumbnail at aspect {aspect:0.###}");
                previous = w;
            }
        }

        // Contract: WouldClampWidth is "true when this aspect is wider than SafeMaxThumbnailWidth allows,
        // i.e. the stored picture will NOT match the graded frame". The two methods must agree — a warning
        // that fires when nothing was clamped, or stays silent when something was, is worse than neither.
        [Test]
        public void WouldClampWidth_AgreesWithThumbnailWidthFor()
        {
            var c = NewConfig();

            for (float aspect = 0.5f; aspect <= 4f; aspect += 0.05f)
            {
                bool clamped = c.ThumbnailWidthFor(aspect) >= c.SafeMaxThumbnailWidth
                               && Mathf.RoundToInt(c.SafeThumbnailHeight * aspect) > c.SafeMaxThumbnailWidth;

                Assert.AreEqual(clamped, c.WouldClampWidth(aspect),
                    $"the clamp warning disagrees with the actual width at aspect {aspect:0.###}");
            }
        }

        // Contract: "a hand-authored 0 fails CLOSED (one shot kept) instead of open (unbounded, i.e. the
        // leak)." Storing without a bound is the NFR3 leak the whole config exists to prevent.
        [TestCase(0)]
        [TestCase(-100)]
        [TestCase(999999)]
        public void SafeMaxStoredShots_IsAlwaysABoundedPositiveCap(int authored)
        {
            var c = NewConfig();
            c.maxStoredShots = authored;

            Assert.That(c.SafeMaxStoredShots,
                Is.InRange(GalleryConfig.MinStoredShots, GalleryConfig.MaxStoredShots));
            Assert.GreaterOrEqual(c.SafeMaxStoredShots, 1, "a gallery must hold at least one shot");
        }

        // Contract: thumbnail dimensions are clamped however the asset was authored — the guard that stops
        // "a single mistyped digit from reserving gigabytes".
        [TestCase(0)]
        [TestCase(-270)]
        [TestCase(100000)]
        public void SafeThumbnailDimensions_StayWithinBounds(int authored)
        {
            var c = NewConfig();
            c.thumbnailHeight = authored;
            c.maxThumbnailWidth = authored;

            Assert.That(c.SafeThumbnailHeight,
                Is.InRange(GalleryConfig.MinThumbnailSize, GalleryConfig.MaxThumbnailSize));
            Assert.That(c.SafeMaxThumbnailWidth,
                Is.InRange(GalleryConfig.MinThumbnailSize, GalleryConfig.MaxThumbnailSize));
        }

        // The memory budget NFR3 is about must always be a finite, positive, bounded number — including
        // when every field was authored as nonsense.
        [Test]
        public void WorstCaseBytes_IsAlwaysPositiveAndBounded()
        {
            var c = NewConfig();
            c.thumbnailHeight = -5;
            c.maxThumbnailWidth = int.MaxValue;
            c.maxStoredShots = int.MaxValue;

            long worst = c.WorstCaseBytes;

            Assert.Greater(worst, 0L, "a corrupt config must not report a zero or negative memory budget");
            Assert.LessOrEqual(worst,
                (long)GalleryConfig.MaxThumbnailSize * GalleryConfig.MaxThumbnailSize
                * GalleryConfig.BytesPerPixel * GalleryConfig.MaxStoredShots,
                "the budget must stay inside the ceilings the constants define");
        }

        // BytesPerShotAt is what a shot costs today; BytesPerShot is the worst case. The first must never
        // exceed the second, or the budget printed at Awake understates what is actually being spent.
        [Test]
        public void BytesPerShotAtAnyAspect_NeverExceedsTheWorstCase()
        {
            var c = NewConfig();

            for (float aspect = 0.5f; aspect <= 4f; aspect += 0.1f)
            {
                Assert.LessOrEqual(c.BytesPerShotAt(aspect), c.BytesPerShot,
                    $"today's cost exceeded the stated worst case at aspect {aspect:0.###}");
            }
        }

        [Test]
        public void ShippedDefaults_ReportNoProblem()
        {
            var c = NewConfig();

            bool hasProblem = c.TryGetConfigProblem(out string problem);

            Assert.IsFalse(hasProblem, $"the shipped defaults should be self-consistent, but: {problem}");
        }
    }
}
