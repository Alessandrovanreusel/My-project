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

        /// <summary>The camera has no drawable viewport (a collapsed Game View, a degenerate
        /// <c>Camera.rect</c>). Distinct from <see cref="TooSmall"/>: without this, a zero-area viewport
        /// silently rejected EVERY capture as "too small", which is indistinguishable from a real
        /// coverage problem and is exactly the "every case fails identically ⇒ suspect the rig" trap.</summary>
        NoViewport,

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

        /// <summary>A detail for a shot that was never graded at all. Use this instead of
        /// <c>default(GradeDetail)</c>: the default leaves <see cref="VisibleFraction"/> at 0, which reads
        /// as "completely hidden" — the exact misreading <see cref="NotEvaluated"/> exists to prevent.
        /// Same class of mistake <see cref="GradeMiss.Unevaluated"/> fixed, one field over.</summary>
        public static GradeDetail Unevaluated =>
            new GradeDetail(GradeMiss.Unevaluated, default, 0f, NotEvaluated);

        /// <summary>True only when the occlusion test actually ran. Checks the miss reason as well as the
        /// sentinel so a default-constructed struct can never claim a measurement it does not have.</summary>
        public bool OcclusionTested => VisibleFraction >= 0f && Miss != GradeMiss.Unevaluated;

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
    /// Measured 2026-07-26 against the real town (16,739 active colliders): **0.0072 ms per call**
    /// including the linecasts, i.e. ~28,000x margin on the budget.
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

        // The 8 AABB corners, indexed by bit pattern: bit0 = +x, bit1 = +y, bit2 = +z, together with the
        // screen point each one projects to (kept whole so the edge pass genuinely does not re-project —
        // the previous version cached only the depth and then projected every in-front corner a second time).
        private static readonly Vector3[] Corners = new Vector3[8];
        private static readonly Vector3[] CornerScreen = new Vector3[8];

        // Nudge past the near plane so a point sitting exactly on it stays well defined.
        private const float NearPlaneEpsilon = 0.001f;

        // How far in from the AABB surface the occlusion samples sit, as a fraction of extents. Probing the
        // body rather than the silhouette edge: a point exactly on the bounding box is as likely to be in
        // thin air as on the subject. An implementation detail of the sampling scheme, not a designer
        // threshold, which is why it lives here rather than in GradingConfig.
        private const float SampleInset = 0.5f;

        /// <summary>
        /// Occlusion sample offsets in units of (extents × <see cref="SampleInset"/>), in order of use.
        ///
        /// ⚠️ THE ORDER IS LOAD-BEARING. The previous scheme derived offsets from <c>(index - 1) &amp; 7</c>,
        /// which had two measured faults (review probe, 2026-07-26): at the shipped 5 samples every
        /// peripheral point had its z bit clear, so ALL of them sat on the subject's −Z face and the +Z half
        /// was never probed — the same occluder scored differently depending on which side you shot from;
        /// and above 9 samples the <c>&amp; 7</c> wrap re-sampled corners already taken, so raising the
        /// setting bought linecasts and re-weighting rather than precision (the reading oscillated 54–64%
        /// for a subject physically 50% occluded).
        ///
        /// These 15 offsets are all DISTINCT, the set is symmetric in every axis, and the first five
        /// deliberately span both signs of all three axes so the shipped default of 5 is unbiased. 15 rather
        /// than 16 precisely so the set stays symmetric — a 16th point would have to favour one direction,
        /// reintroducing the directional bias in miniature.
        /// </summary>
        private static readonly Vector3[] SampleOffsets =
        {
            new Vector3( 0f,  0f,  0f),      // 0 — centre of mass
            new Vector3(-1f, -1f, -1f),      // 1..4 — four corners covering ±x, ±y AND ±z
            new Vector3( 1f,  1f,  1f),
            new Vector3(-1f,  1f,  1f),
            new Vector3( 1f, -1f, -1f),
            new Vector3( 1f, -1f,  1f),      // 5..8 — the remaining four corners
            new Vector3(-1f,  1f, -1f),
            new Vector3( 1f,  1f, -1f),
            new Vector3(-1f, -1f,  1f),
            new Vector3(-1f,  0f,  0f),      // 9..14 — the six face centres
            new Vector3( 1f,  0f,  0f),
            new Vector3( 0f, -1f,  0f),
            new Vector3( 0f,  1f,  0f),
            new Vector3( 0f,  0f, -1f),
            new Vector3( 0f,  0f,  1f),
        };

        /// <summary>How many distinct occlusion sample points exist — the ceiling for
        /// <see cref="GradingConfig.occlusionSamples"/>. Asking for more cannot buy more information.</summary>
        public const int MaxOcclusionSamples = 15;

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

            // NOT `subject == null`. ISubject is an INTERFACE, so == is plain reference equality and
            // UnityEngine.Object's destroyed-object overload never runs — a destroyed EventActor would sail
            // past the guard and throw MissingReferenceException on .Bounds below. Today's only caller
            // happens to check on the concrete type first, but this is public API with a documented
            // architecture signature, so the guard has to actually hold.
            if (IsNullOrDestroyed(subject)) { detail = Fail(GradeMiss.NoSubject); return ShotGrade.Miss; }

            if (cfg == null) { detail = Fail(GradeMiss.NoConfig); return ShotGrade.Miss; }

            // No drawable viewport means every measurement below is meaningless. Reported as its own reason
            // rather than falling through to TooSmall, which looked identical to a real coverage failure.
            Rect view = cam.pixelRect;
            if (view.width <= 0f || view.height <= 0f)
            {
                detail = Fail(GradeMiss.NoViewport);
                return ShotGrade.Miss;
            }

            Bounds bounds = subject.Bounds;

            // EventActor.Bounds fails soft to a zero-size point when it has no renderers. A zero-size box
            // would sail through the frustum test and then project to an empty rect, reading as "in frame
            // but too small" — technically a miss, but for the wrong reason and impossible to debug.
            //
            // The finiteness check is not paranoia: NaN fails every comparison, so `NaN <= Mathf.Epsilon` is
            // false and non-finite bounds used to reach GeometryUtility.TestPlanesAABB, which logs an
            // engine-level "Invalid AABB" error carrying no category anyone would think to grep for.
            if (!IsFinite(bounds.center) || !IsFinite(bounds.extents)
                || bounds.extents.sqrMagnitude <= Mathf.Epsilon)
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

            // --- Gate 1b: the subject is actually in FRONT of the lens ---------------------------------
            // TestPlanesAABB passes for any box that is outside no single plane — including one that
            // ENCLOSES the camera. The near-plane clipping below then finds the forward-side corners,
            // projects them to enormous coordinates, and the viewport clamp turns that into a full-screen
            // rect: coverage 100%, five stars, for a photograph taken facing AWAY from the subject.
            // Reproduced by the review probe (2026-07-26); reachable in play because the drunk prefab has
            // no colliders, so the player walks straight through him.
            //
            // Testing the CENTRE keeps genuine close-ups working — standing a metre in front of him still
            // has his centre well in front of the lens — while rejecting "I am inside him facing away".
            if (cam.WorldToScreenPoint(bounds.center).z <= cam.nearClipPlane)
            {
                detail = Fail(GradeMiss.BehindCamera);
                return ShotGrade.Miss;
            }

            // --- Gate 2: fills enough of the frame -----------------------------------------------------
            if (!TryGetScreenRect(cam, view, bounds, out Rect rect, out bool fullyOffscreen))
            {
                // Every corner sat behind the near plane. TestPlanesAABB can pass here because it tests the
                // whole box against the planes, and a box can intersect the frustum's side planes while
                // lying entirely behind the camera.
                detail = Fail(GradeMiss.BehindCamera);
                return ShotGrade.Miss;
            }

            if (fullyOffscreen)
            {
                // The box projected wholly past one edge, so the clamp collapsed it to zero width/height.
                // That is "not in frame", not "too small" — reporting TooSmall here sent a designer looking
                // at the coverage threshold for a framing problem.
                detail = Fail(GradeMiss.OutsideFrustum);
                return ShotGrade.Miss;
            }

            float screenArea = view.width * view.height;
            float coverage = (rect.width * rect.height) / screenArea;

            // SafeMinCoverage, not the raw field: [Range] and OnValidate are editor-only, and this project
            // authors ScriptableObject assets as hand-written YAML, which passes through neither. A NaN
            // threshold makes `coverage < NaN` false forever — the gate silently disabled.
            if (coverage < cfg.SafeMinCoverage)
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
            //
            // ⚠️ Story 1.12's HUD must NOT gate on ShotGrade.Stars until 1.10 lands: with raw coverage as the
            // score, a real hit measures 16–21% and maps to 1–2 stars, and 5 stars needs ~80% of the screen.
            return ShotGrade.FromPercent(coverage);
        }

        // Every early-out above the occlusion gate reports NotEvaluated rather than 0 line-of-sight.
        private static GradeDetail Fail(GradeMiss miss) =>
            new GradeDetail(miss, default, 0f, GradeDetail.NotEvaluated);

        /// <summary>Null check that also catches a DESTROYED Unity object behind an interface reference.</summary>
        private static bool IsNullOrDestroyed(ISubject subject) =>
            subject is UnityEngine.Object o ? o == null : subject is null;

        private static bool IsFinite(Vector3 v) =>
            !float.IsNaN(v.x) && !float.IsInfinity(v.x)
            && !float.IsNaN(v.y) && !float.IsInfinity(v.y)
            && !float.IsNaN(v.z) && !float.IsInfinity(v.z);

        /// <summary>
        /// Projects a world AABB to a pixel-space <see cref="Rect"/>, clamped to the camera's viewport.
        /// Returns false when the box lies entirely behind the near plane, and sets
        /// <paramref name="fullyOffscreen"/> when it projects wholly past one edge of the viewport.
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
        /// Verified correct by the review probe (2026-07-26): a 1u straddle reads a sane 100%, and boxes
        /// 3u/20u behind the lens are rejected rather than credited. Note the clip alone is NOT sufficient —
        /// see Gate 1b in <see cref="Grade"/> for the enclosing-camera case it does not cover.
        /// </summary>
        private static bool TryGetScreenRect(Camera cam, Rect view, Bounds bounds,
                                             out Rect rect, out bool fullyOffscreen)
        {
            fullyOffscreen = false;

            Vector3 c = bounds.center, e = bounds.extents;
            for (int i = 0; i < 8; i++)
            {
                Corners[i] = new Vector3(
                    c.x + ((i & 1) != 0 ? e.x : -e.x),
                    c.y + ((i & 2) != 0 ? e.y : -e.y),
                    c.z + ((i & 4) != 0 ? e.z : -e.z));
            }

            // WorldToScreenPoint's z is distance along the camera's forward axis, so it doubles as the
            // in-front test.
            float near = cam.nearClipPlane + NearPlaneEpsilon;

            bool any = false;
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            void IncludeScreen(Vector3 sp)
            {
                if (sp.x < minX) minX = sp.x;
                if (sp.y < minY) minY = sp.y;
                if (sp.x > maxX) maxX = sp.x;
                if (sp.y > maxY) maxY = sp.y;
                any = true;
            }

            // Project each corner ONCE and keep the whole screen point, so the edge pass below can read the
            // depth without re-projecting and the in-front corners are not projected twice.
            for (int i = 0; i < 8; i++)
            {
                CornerScreen[i] = cam.WorldToScreenPoint(Corners[i]);
                if (CornerScreen[i].z >= near) IncludeScreen(CornerScreen[i]);
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

                    float di = CornerScreen[i].z, dj = CornerScreen[j].z;
                    bool iFront = di >= near, jFront = dj >= near;
                    if (iFront == jFront) continue;    // both sides same — nothing to clip

                    float t = (near - di) / (dj - di);
                    IncludeScreen(cam.WorldToScreenPoint(Vector3.Lerp(Corners[i], Corners[j], t)));
                }
            }

            if (!any) { rect = default; return false; }

            // Entirely past one edge of the viewport? Then the clamp below would collapse it to zero and the
            // shot would be misreported as TooSmall rather than out of frame.
            if (maxX < view.xMin || minX > view.xMax || maxY < view.yMin || minY > view.yMax)
            {
                fullyOffscreen = true;
                rect = default;
                return true;
            }

            // Clamp to the viewport: a subject half out of frame must not be credited for the pixels that
            // fell off the screen. Clamped to cam.pixelRect, NOT [0, pixelWidth]: an OFFSET viewport (split
            // screen, a picture-in-picture viewfinder) has a pixelRect that does not start at zero, and
            // clamping to [0, width] collapsed a perfectly framed subject to zero width — reproduced by the
            // review probe with cam.rect = (0.5, 0, 0.5, 1).
            float x0 = Mathf.Clamp(minX, view.xMin, view.xMax);
            float x1 = Mathf.Clamp(maxX, view.xMin, view.xMax);
            float y0 = Mathf.Clamp(minY, view.yMin, view.yMax);
            float y1 = Mathf.Clamp(maxY, view.yMin, view.yMax);

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
            float near = cam.nearClipPlane + NearPlaneEpsilon;
            int samples = cfg.SafeOcclusionSamples;
            int tested = 0, clear = 0;

            for (int i = 0; i < samples; i++)
            {
                Vector3 target = SamplePoint(bounds, i);

                // A sample behind the lens is not in the photograph, and a linecast to it would run
                // BACKWARDS from the eye — letting whatever stands behind the player decide whether the
                // subject is occluded. Skip it rather than let it vote. (The projection path clips against
                // the near plane for the same reason; the two gates used to disagree about where the camera
                // begins.)
                if (cam.WorldToScreenPoint(target).z < near) continue;

                tested++;
                if (!Physics.Linecast(eye, target, cfg.occluderMask, QueryTriggerInteraction.Ignore))
                    clear++;
            }

            // Nothing testable means nothing visible — and never a divide by zero.
            return tested > 0 ? clear / (float)tested : 0f;
        }

        /// <summary>
        /// Sample 0 is the centre of mass; the rest spread across the body via <see cref="SampleOffsets"/>,
        /// pulled in by <see cref="SampleInset"/> so we probe the body rather than the silhouette edge.
        /// All 16 offsets are distinct and the first five span both signs of all three axes.
        /// </summary>
        private static Vector3 SamplePoint(Bounds bounds, int index)
        {
            Vector3 offset = SampleOffsets[Mathf.Clamp(index, 0, SampleOffsets.Length - 1)];
            return bounds.center + Vector3.Scale(offset, bounds.extents * SampleInset);
        }
    }
}
