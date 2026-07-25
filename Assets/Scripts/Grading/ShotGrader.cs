using UnityEngine;
using CameraGame.Events;

namespace CameraGame.Grading
{
    /// <summary>Why a shot scored the way it did — for logging and the editor debug overlay. A silent 0%
    /// is the most confusing thing you can hand a designer, so every miss says which gate rejected it.</summary>
    public enum GradeMiss
    {
        /// <summary>No grade was ever computed. **Deliberately the zero value** so that a
        /// <c>default(GradeDetail)</c> can never masquerade as a successful shot. It did: with
        /// <c>None = 0</c>, pressing the shutter in an empty world returned an untouched detail struct
        /// that every reader — the overlay, the logs — reported as "counted", while the grade itself was
        /// correctly 0%. Photographing nothing scored as a hit.</summary>
        Unevaluated = 0,

        /// <summary>The shot passed every gate.</summary>
        None,

        NoCamera,
        NoSubject,
        NoConfig,
        DegenerateBounds,
        OutsideFrustum,
        BehindCamera,
        TooSmall,
        Occluded,
    }

    /// <summary>Diagnostic detail from a single grade. Always produced (the values are computed anyway),
    /// so logging and the debug overlay cost nothing extra.</summary>
    public readonly struct GradeDetail
    {
        /// <summary>Sentinel for <see cref="VisibleFraction"/> when the occlusion test never ran. Grading
        /// early-outs at the first failed gate, so a shot rejected on frustum or coverage has NO
        /// line-of-sight measurement — reporting that as 0% would read as "completely hidden" when the
        /// subject was simply never checked.</summary>
        public const float NotEvaluated = -1f;

        public readonly GradeMiss Miss;
        public readonly Rect ScreenRect;        // pixel-space, clamped to the camera viewport
        public readonly float Coverage01;       // fraction of the camera's pixel area the subject fills
        public readonly float VisibleFraction;  // clear-line-of-sight samples, or NotEvaluated

        public bool OcclusionTested => VisibleFraction >= 0f;

        /// <summary>Line-of-sight for display: a percentage, or "n/a" when the test never ran.</summary>
        public string VisibleText => OcclusionTested ? VisibleFraction.ToString("P0") : "n/a";

        public GradeDetail(GradeMiss miss, Rect screenRect, float coverage01, float visibleFraction)
        {
            Miss = miss;
            ScreenRect = screenRect;
            Coverage01 = coverage01;
            VisibleFraction = visibleFraction;
        }

        public override string ToString() =>
            Miss == GradeMiss.None
                ? $"hit (coverage {Coverage01:P1}, visible {VisibleText})"
                : $"miss:{Miss} (coverage {Coverage01:P1}, visible {VisibleText})";
    }

    /// <summary>
    /// Scores a captured photo (Story 1.9 — the subject-capture GATE). Pure static logic with no Unity
    /// lifecycle and no state, exactly as the architecture specifies
    /// (<c>Grade(camera, subject, config) → ShotGrade</c>).
    ///
    /// **No GPU readback** (AR3). Everything here is arithmetic on <see cref="Bounds"/> plus a handful of
    /// linecasts — no ReadPixels, no RenderTexture, no replacement shaders. A readback would stall the
    /// pipeline for milliseconds and blow the 0.2 s capture-to-feedback budget (NFR2) on its own.
    ///
    /// Story 1.9 answers only "does this shot count?". Story 1.10 replaces the SCORE (currently raw
    /// coverage) with the composition × timing blend; the gates below stay.
    /// </summary>
    public static class ShotGrader
    {
        // Reused across calls: the allocating CalculateFrustumPlanes overload returns a fresh Plane[6] every
        // time. Safe as static state because grading is synchronous, single-threaded, and stateless between
        // calls — nothing survives past the end of Grade().
        private static readonly Plane[] FrustumPlanes = new Plane[6];

        // The 8 AABB corners, indexed by bit pattern: bit0 = +x, bit1 = +y, bit2 = +z, and their depths
        // along the camera's forward axis. Reused for the same reason as FrustumPlanes above.
        private static readonly Vector3[] Corners = new Vector3[8];
        private static readonly float[] CornerDepths = new float[8];

        /// <summary>Grades a shot. Returns <see cref="ShotGrade.Miss"/> if any gate rejects it.</summary>
        public static ShotGrade Grade(Camera cam, ISubject subject, GradingConfig cfg) =>
            Grade(cam, subject, cfg, out _);

        /// <summary>Grades a shot and reports why, for logging and the debug overlay.</summary>
        public static ShotGrade Grade(Camera cam, ISubject subject, GradingConfig cfg, out GradeDetail detail)
        {
            // Cheapest checks first, each with an early-out. The occlusion linecasts are by far the most
            // expensive step (this world carries 16,321 MeshColliders), so they must never run for a shot
            // that already failed the frustum or the coverage gate.
            if (cam == null) { detail = Fail(GradeMiss.NoCamera); return ShotGrade.Miss; }
            if (subject == null) { detail = Fail(GradeMiss.NoSubject); return ShotGrade.Miss; }
            if (cfg == null) { detail = Fail(GradeMiss.NoConfig); return ShotGrade.Miss; }

            Bounds bounds = subject.Bounds;

            // EventActor.Bounds fails soft to a zero-size point when it has no renderers. A zero-size box
            // would sail through the frustum test and then project to an empty rect, reading as "in frame
            // but too small" — technically a miss, but for the wrong reason and impossible to debug.
            if (bounds.extents.sqrMagnitude <= Mathf.Epsilon)
            {
                detail = Fail(GradeMiss.DegenerateBounds);
                return ShotGrade.Miss;
            }

            // --- Gate 1: inside the view frustum -------------------------------------------------------
            GeometryUtility.CalculateFrustumPlanes(cam, FrustumPlanes);
            if (!GeometryUtility.TestPlanesAABB(FrustumPlanes, bounds))
            {
                detail = Fail(GradeMiss.OutsideFrustum);
                return ShotGrade.Miss;
            }

            // --- Gate 2: fills enough of the frame -----------------------------------------------------
            if (!TryGetScreenRect(cam, bounds, out Rect rect))
            {
                // Every corner sat behind the near plane. TestPlanesAABB can pass here because it tests the
                // whole box against the planes, and a box can intersect the frustum's side planes while
                // lying entirely behind the camera.
                detail = Fail(GradeMiss.BehindCamera);
                return ShotGrade.Miss;
            }

            float screenArea = cam.pixelWidth * (float)cam.pixelHeight;
            float coverage = screenArea > 0f ? (rect.width * rect.height) / screenArea : 0f;

            if (coverage < cfg.minCoverage)
            {
                // NotEvaluated, not 0: we early-out here, so line of sight was never measured.
                detail = new GradeDetail(GradeMiss.TooSmall, rect, coverage, GradeDetail.NotEvaluated);
                return ShotGrade.Miss;
            }

            // --- Gate 3: actually visible, not hidden behind the scenery -------------------------------
            float visible = VisibleFraction(cam, bounds, cfg);
            if (visible < cfg.SafeMinVisibleSamples)
            {
                detail = new GradeDetail(GradeMiss.Occluded, rect, coverage, visible);
                return ShotGrade.Miss;
            }

            detail = new GradeDetail(GradeMiss.None, rect, coverage, visible);

            // The shot counts. STORY 1.10 REPLACES THIS LINE with composition × timing; for now the score is
            // simply how much of the frame the subject fills, which is an honest, meaningful number rather
            // than an invented constant.
            return ShotGrade.FromPercent(coverage);
        }

        // Every early-out above the occlusion gate reports NotEvaluated rather than 0 line-of-sight.
        private static GradeDetail Fail(GradeMiss miss) =>
            new GradeDetail(miss, default, 0f, GradeDetail.NotEvaluated);

        /// <summary>
        /// Projects a world AABB to a pixel-space <see cref="Rect"/>, clamped to the camera's viewport.
        /// Returns false when the box lies entirely behind the near plane.
        ///
        /// ⚠️ THE TRAP THIS METHOD EXISTS TO AVOID. <c>Camera.WorldToScreenPoint</c> does NOT fail for
        /// points behind the camera — it returns a point with NEGATIVE z and MIRRORED x/y. Projecting all
        /// eight corners naively and taking min/max therefore produces, for any subject straddling the
        /// camera plane, a rect that is wildly wrong — usually enormous, which would let a photo taken
        /// facing AWAY from the subject sail through the coverage gate.
        ///
        /// This is not an edge case here: the player character is ~8.86 units tall in a world modelled at
        /// roughly 4× metric scale, so the subject's box is large in world units and straddling the camera
        /// plane happens whenever the player stands next to the subject — exactly when they are trying to
        /// photograph him.
        ///
        /// The fix is to clip against the near plane before projecting: take the corners that are in front
        /// as-is, and for every box edge that crosses the plane, project the crossing point instead.
        /// </summary>
        private static bool TryGetScreenRect(Camera cam, Bounds bounds, out Rect rect)
        {
            Vector3 c = bounds.center, e = bounds.extents;
            for (int i = 0; i < 8; i++)
            {
                Corners[i] = new Vector3(
                    c.x + ((i & 1) != 0 ? e.x : -e.x),
                    c.y + ((i & 2) != 0 ? e.y : -e.y),
                    c.z + ((i & 4) != 0 ? e.z : -e.z));
            }

            // WorldToScreenPoint's z is distance along the camera's forward axis, so it doubles as the
            // in-front test. Nudge past the near plane so a point sitting exactly on it stays well defined.
            float near = cam.nearClipPlane + 0.001f;

            bool any = false;
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            void Include(Vector3 world)
            {
                Vector3 sp = cam.WorldToScreenPoint(world);
                if (sp.x < minX) minX = sp.x;
                if (sp.y < minY) minY = sp.y;
                if (sp.x > maxX) maxX = sp.x;
                if (sp.y > maxY) maxY = sp.y;
                any = true;
            }

            // Depth of each corner, cached so the edge pass doesn't re-project.
            for (int i = 0; i < 8; i++)
            {
                CornerDepths[i] = cam.WorldToScreenPoint(Corners[i]).z;
                if (CornerDepths[i] >= near) Include(Corners[i]);
            }

            // The 12 edges of a box connect corners differing by exactly one bit. Where an edge crosses the
            // near plane, project the crossing point — that is the part of the subject the camera can
            // actually see, and it is what keeps a half-behind subject's rect honest.
            for (int i = 0; i < 8; i++)
            {
                for (int bit = 1; bit <= 4; bit <<= 1)
                {
                    if ((i & bit) != 0) continue;      // visit each edge once, from its low-bit end
                    int j = i | bit;

                    bool iFront = CornerDepths[i] >= near, jFront = CornerDepths[j] >= near;
                    if (iFront == jFront) continue;    // both sides same — nothing to clip

                    float t = (near - CornerDepths[i]) / (CornerDepths[j] - CornerDepths[i]);
                    Include(Vector3.Lerp(Corners[i], Corners[j], t));
                }
            }

            if (!any) { rect = default; return false; }

            // Clamp to the viewport: a subject half out of frame must not be credited for the pixels that
            // fell off the screen.
            float x0 = Mathf.Clamp(minX, 0f, cam.pixelWidth);
            float x1 = Mathf.Clamp(maxX, 0f, cam.pixelWidth);
            float y0 = Mathf.Clamp(minY, 0f, cam.pixelHeight);
            float y1 = Mathf.Clamp(maxY, 0f, cam.pixelHeight);

            rect = new Rect(x0, y0, Mathf.Max(0f, x1 - x0), Mathf.Max(0f, y1 - y0));
            return true;
        }

        /// <summary>
        /// Fraction of sample points across the subject with clear line of sight from the camera.
        ///
        /// ⚠️ <see cref="ISubject"/> deliberately exposes no Transform and no Collider, so we cannot
        /// raycast and ask "did I hit the subject?" — and adding one would re-couple grading to scene
        /// objects, which is precisely the seam that interface exists to protect. Instead we linecast
        /// against a mask that EXCLUDES the subject's own layer, so any hit at all means blocked.
        ///
        /// Linecast, not Raycast: the segment must stop AT the subject, or geometry standing behind him
        /// would count as occluding him.
        /// </summary>
        private static float VisibleFraction(Camera cam, Bounds bounds, GradingConfig cfg)
        {
            Vector3 eye = cam.transform.position;
            int samples = cfg.SafeOcclusionSamples;
            int clear = 0;

            for (int i = 0; i < samples; i++)
            {
                Vector3 target = SamplePoint(bounds, i);
                if (!Physics.Linecast(eye, target, cfg.occluderMask, QueryTriggerInteraction.Ignore))
                    clear++;
            }

            return clear / (float)samples;
        }

        /// <summary>
        /// Sample 0 is the centre of mass; the rest spread toward the corners but pulled halfway in, so we
        /// probe the body rather than the silhouette edge — a point exactly on the bounding box is as
        /// likely to be in thin air as on the subject.
        /// </summary>
        private static Vector3 SamplePoint(Bounds bounds, int index)
        {
            if (index == 0) return bounds.center;

            int i = (index - 1) & 7;
            Vector3 e = bounds.extents * 0.5f;
            return bounds.center + new Vector3(
                (i & 1) != 0 ? e.x : -e.x,
                (i & 2) != 0 ? e.y : -e.y,
                (i & 4) != 0 ? e.z : -e.z);
        }
    }
}
