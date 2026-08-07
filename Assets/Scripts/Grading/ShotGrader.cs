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
        /// <summary>Sentinel for a measurement that never ran. Grading early-outs at the first failed
        /// gate, so a shot rejected on frustum or size has NO line-of-sight measurement — reporting that
        /// as 0% would read as "completely hidden" when the subject was simply never checked.
        ///
        /// Used by <see cref="VisibleFraction"/>, <see cref="HeightFraction"/> and
        /// <see cref="FramedFraction"/> alike. The last two were added in Story 1.10 defaulting to 0 and 1
        /// respectively, which reproduced the exact misreading this sentinel exists to prevent: a
        /// <c>BehindCamera</c> miss printed "height 0.0%, framed 100%", asserting that the subject was
        /// microscopic and perfectly framed when neither had been measured (review 2026-07-28, reproduced
        /// in <c>g_looking_away</c>).</summary>
        public const float NotEvaluated = -1f;

        public readonly GradeMiss Miss;
        public readonly Rect ScreenRect;        // pixel-space, clamped to the camera viewport
        public readonly float Coverage01;       // fraction of the camera's pixel AREA the subject fills
        public readonly float VisibleFraction;  // clear-line-of-sight samples, or NotEvaluated

        /// <summary>
        /// Fraction of the frame's HEIGHT the subject's projected box fills — the measure the gate and the
        /// prominence curve both run on since Story 1.10.
        ///
        /// <see cref="Coverage01"/> is kept alongside deliberately, even though nothing scores on it any
        /// more: every piece of evidence from Stories 1.9–1.10 (shots.txt, the overlay, two sessions of
        /// recorded photo shoots) is expressed in area coverage, and dropping it would orphan all of it.
        /// It also remains the honest answer to "how much of the picture is him", which height is not.
        /// </summary>
        public readonly float HeightFraction;

        /// <summary>How much of the subject's projected box actually landed inside the frame (clamped area
        /// ÷ unclamped area). 1 = fully framed; 0.5 = half of him is outside the picture. Story 1.9
        /// computed the unclamped rect and then threw this away; composition needs it.</summary>
        public readonly float FramedFraction;

        /// <summary>Seconds from the peak WINDOW at the instant of the shutter — positive early, 0 during
        /// the money shot, negative late. <see cref="float.NaN"/> when timing was never read (the shot
        /// failed a gate first). See <c>ISubject.PeakOffset</c>.</summary>
        public readonly float PeakOffset;

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

        /// <summary>True only when the subject was actually projected and measured. Same discipline as
        /// <see cref="OcclusionTested"/> — a miss above the size gate never got this far.</summary>
        public bool SizeMeasured => HeightFraction >= 0f && Miss != GradeMiss.Unevaluated;

        /// <summary>Subject height as a share of the frame, or "n/a" when it was never measured.</summary>
        public string HeightText => SizeMeasured ? HeightFraction.ToString("P1") : "n/a";

        /// <summary>Share of the subject inside the frame, or "n/a" when it was never measured.</summary>
        public string FramedText => FramedFraction >= 0f && Miss != GradeMiss.Unevaluated
            ? FramedFraction.ToString("P0") : "n/a";

        /// <summary>Area coverage for display, or "n/a" when the subject was never projected. Coverage is
        /// computed on the same line as the height fraction, so the two are measured together or not at
        /// all.</summary>
        public string CoverageText => SizeMeasured ? Coverage01.ToString("P1") : "n/a";

        /// <summary>Peak offset for display, or "n/a" when there is no usable measurement. Same discipline
        /// as <see cref="VisibleText"/>: a shot rejected at the SIZE gate has no timing measurement, and
        /// printing 0.00 s would read as "dead on the peak".
        ///
        /// "n/a" covers two cases and deliberately does not distinguish them, because neither yields a
        /// number worth showing: timing was never read (the shot failed an earlier gate), or it was read
        /// and the subject reported a non-finite offset. <see cref="GradeMiss"/> already says which — a
        /// counted shot showing "n/a" is the second case.</summary>
        public string PeakOffsetText =>
            float.IsNaN(PeakOffset) || float.IsInfinity(PeakOffset)
                ? "n/a"
                : $"{PeakOffset:+0.00;-0.00;0.00}s";

        /// <param name="heightFraction">Frame-height fraction; the measure the gate runs on. Defaults to
        /// <see cref="NotEvaluated"/> — NOT 0, which would assert a subject of zero height was measured.</param>
        /// <param name="framedFraction">Share of the subject's box inside the frame. Defaults to
        /// <see cref="NotEvaluated"/> — NOT 1, which would assert a perfectly framed subject was measured.</param>
        /// <param name="peakOffset">Seconds from the peak window, or NaN when timing was never read.</param>
        public GradeDetail(GradeMiss miss, Rect screenRect, float coverage01, float visibleFraction,
                           float heightFraction = NotEvaluated, float framedFraction = NotEvaluated,
                           float peakOffset = float.NaN)
        {
            Miss = miss;
            ScreenRect = screenRect;
            Coverage01 = coverage01;
            VisibleFraction = visibleFraction;
            HeightFraction = heightFraction;
            FramedFraction = framedFraction;
            PeakOffset = peakOffset;
        }

        // Every number here goes through its *Text accessor, so a miss that early-outed before a
        // measurement prints "n/a" rather than a fabricated 0.0% / 100%.
        public override string ToString() =>
            Miss == GradeMiss.None
                ? $"hit (height {HeightText}, area {CoverageText}, framed {FramedText}, " +
                  $"visible {VisibleText}, peak {PeakOffsetText})"
                : $"miss:{Miss} (height {HeightText}, area {CoverageText}, framed {FramedText}, " +
                  $"visible {VisibleText})";
    }

    /// <summary>
    /// Scores a captured photo: the subject-capture GATE (Story 1.9) and then the composition × timing
    /// SCORE (Story 1.10). Pure static logic with no Unity lifecycle and no state, exactly as the
    /// architecture specifies (<c>Grade(camera, subject, config) → ShotGrade</c>).
    ///
    /// **No GPU readback** (AR3). Everything here is arithmetic on <see cref="Bounds"/> plus a handful of
    /// linecasts — no ReadPixels, no RenderTexture, no replacement shaders. A readback would stall the
    /// pipeline for milliseconds and blow the 0.2 s capture-to-feedback budget (NFR2) on its own.
    /// Measured 2026-07-26 against the real town (16,739 active colliders): **0.0072 ms per call**
    /// including the linecasts, i.e. ~28,000x margin on the budget. Story 1.10 added arithmetic only —
    /// no new linecast, no readback, no allocation — so that measurement still stands.
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

        /// <summary>Distance from the centre of the viewport to a corner, in normalized coordinates
        /// (sqrt(0.5² + 0.5²)) — the largest the off-centre distance can ever be, so dividing by it
        /// normalizes the placement penalty to [0,1]. An invariant of the geometry, not a designer
        /// threshold, which is why it lives here rather than in GradingConfig — the same distinction
        /// <see cref="SampleInset"/> above is drawn on.</summary>
        private const float MaxCentreDistance = 0.70710678f;

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

        /// <summary>Grades a shot. Returns a <see cref="ShotGrade.Missed"/> if any gate rejects it.</summary>
        public static ShotGrade Grade(Camera cam, ISubject subject, GradingConfig cfg) =>
            Grade(cam, subject, cfg, out _);

        /// <summary>Grades a shot and reports why, for logging and the debug overlay.</summary>
        public static ShotGrade Grade(Camera cam, ISubject subject, GradingConfig cfg, out GradeDetail detail)
        {
            // Cheapest checks first, each with an early-out. The occlusion linecasts are by far the most
            // expensive step (this world carries 16,321 MeshColliders), so they must never run for a shot
            // that already failed the frustum or the SIZE gate. ("Coverage gate" was this check's name until
            // Story 1.10 moved it onto the height fraction; Coverage01 still exists as a live, differently
            // defined field, so the old name now points at the wrong number.)
            if (cam == null) { detail = Fail(GradeMiss.NoCamera); return ShotGrade.Missed(GradeMiss.NoCamera); }

            // NOT `subject == null`. ISubject is an INTERFACE, so == is plain reference equality and
            // UnityEngine.Object's destroyed-object overload never runs — a destroyed EventActor would sail
            // past the guard and throw MissingReferenceException on .Bounds below. Today's only caller
            // happens to check on the concrete type first, but this is public API with a documented
            // architecture signature, so the guard has to actually hold.
            if (IsNullOrDestroyed(subject)) { detail = Fail(GradeMiss.NoSubject); return ShotGrade.Missed(GradeMiss.NoSubject); }

            if (cfg == null) { detail = Fail(GradeMiss.NoConfig); return ShotGrade.Missed(GradeMiss.NoConfig); }

            // Read ONCE, here, and carried into every miss below (Story 1.11). Read live off the subject
            // rather than cached anywhere: subjects are pooled, so an id captured a frame early can name a
            // different event entirely (ISubject's liveness contract). Everything above this line rejected
            // BEFORE a subject existed and correctly leaves the id empty.
            string who = subject.SubjectId;

            // No drawable viewport means every measurement below is meaningless. Reported as its own reason
            // rather than falling through to TooSmall, which looked identical to a real size failure.
            //
            // The finiteness check matters for the same reason it does on `bounds` below, and was missing
            // until the 2026-07-28 review: NaN fails every comparison, so `NaN <= 0f` is false and a NaN
            // viewport sailed through this gate. heightFraction then became NaN, `NaN < SafeMinSubjectHeight`
            // was false so the size gate passed too, and ShotGrade.Sane turned the NaN composition into 0 —
            // yielding a COUNTED hit at 0% for a camera that cannot render.
            Rect view = cam.pixelRect;
            if (!IsFinite(view.width) || !IsFinite(view.height)
                || !IsFinite(view.xMin) || !IsFinite(view.yMin)
                || view.width <= 0f || view.height <= 0f)
            {
                detail = Fail(GradeMiss.NoViewport);
                return ShotGrade.Missed(GradeMiss.NoViewport, who);
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
                return ShotGrade.Missed(GradeMiss.DegenerateBounds, who);
            }

            // --- Gate 1: inside the view frustum -------------------------------------------------------
            GeometryUtility.CalculateFrustumPlanes(cam, FrustumPlanes);
            if (!GeometryUtility.TestPlanesAABB(FrustumPlanes, bounds))
            {
                detail = Fail(GradeMiss.OutsideFrustum);
                return ShotGrade.Missed(GradeMiss.OutsideFrustum, who);
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
                return ShotGrade.Missed(GradeMiss.BehindCamera, who);
            }

            // --- Gate 2: fills enough of the frame -----------------------------------------------------
            if (!TryGetScreenRect(cam, view, bounds, out Rect rect, out bool fullyOffscreen,
                                  out float framed, out Vector2 subjectCentre))
            {
                // Every corner sat behind the near plane. TestPlanesAABB can pass here because it tests the
                // whole box against the planes, and a box can intersect the frustum's side planes while
                // lying entirely behind the camera.
                detail = Fail(GradeMiss.BehindCamera);
                return ShotGrade.Missed(GradeMiss.BehindCamera, who);
            }

            if (fullyOffscreen)
            {
                // The box projected wholly past one edge, so the clamp collapsed it to zero width/height.
                // That is "not in frame", not "too small" — reporting TooSmall here sent a designer looking
                // at the coverage threshold for a framing problem.
                detail = Fail(GradeMiss.OutsideFrustum);
                return ShotGrade.Missed(GradeMiss.OutsideFrustum, who);
            }

            float coverage = (rect.width * rect.height) / (view.width * view.height);

            // ⚠️ HEIGHT, NOT AREA — the measurement change Story 1.10 turns on. The projected box around a
            // tall, thin, arms-down figure is mostly empty air, so its AREA reads 4.5% for a full-body
            // portrait that plainly has him as the subject (_bmad-output/verification/photo-shoot/b_mid.png
            // — NOT Temp/, which Unity deletes on editor shutdown) and it BREATHES
            // with the walk cycle — 7.34% and 9.02% on two runs of the same pose, deciding pass/fail by
            // animation frame alone. The same photograph reads 39% of the frame HEIGHT, which is steady
            // across the animation and is what "he fills the frame" actually means to a photographer.
            //
            // The gate and the prominence curve deliberately run on the SAME measure. Gating on one and
            // scoring on the other is how the two end up disagreeing about what "prominent" means.
            float heightFraction = rect.height / view.height;

            // SafeMinSubjectHeight, not the raw field: [Range] and OnValidate are editor-only, and this
            // project authors ScriptableObject assets as hand-written YAML, which passes through neither. A
            // NaN threshold makes `heightFraction < NaN` false forever — the gate silently disabled.
            if (heightFraction < cfg.SafeMinSubjectHeight)
            {
                // NotEvaluated, not 0: we early-out here, so line of sight was never measured.
                detail = new GradeDetail(GradeMiss.TooSmall, rect, coverage, GradeDetail.NotEvaluated,
                                         heightFraction, framed);
                return ShotGrade.Missed(GradeMiss.TooSmall, who);
            }

            // --- Gate 3: actually visible, not hidden behind the scenery -------------------------------
            float visible = VisibleFraction(cam, bounds, cfg);
            if (visible < cfg.SafeMinVisibleSamples)
            {
                detail = new GradeDetail(GradeMiss.Occluded, rect, coverage, visible, heightFraction, framed);
                return ShotGrade.Missed(GradeMiss.Occluded, who);
            }

            // --- The shot counts: score it (Story 1.10) ------------------------------------------------
            float composition = Composition(cfg, view, subjectCentre, heightFraction, framed);
            float timing = Timing(cfg, subject, out float peakOffset);

            detail = new GradeDetail(GradeMiss.None, rect, coverage, visible, heightFraction, framed, peakOffset);

            // The star boundaries come from the config, resolved into a guaranteed-descending set. They are
            // a designer's call ("how good must a photograph be for five stars"), not an implementation
            // detail — the previous CeilToInt(grade x 5) silently hardcoded "80% is perfect".
            // peakOffset is carried onto the SHIPPED grade as well as onto the editor-only detail (Story
            // 1.12): the HUD has to tell the player whether they were early or late, and Timing01 alone
            // cannot — 20% is the same number two seconds either side of the peak.
            return ShotGrade.Scored(visible, composition, timing, cfg.SafeStarScale, who, peakOffset);
        }

        /// <summary>
        /// Composition (FR6): how prominently the subject sits in the frame, where he sits, and how much of
        /// him made it into the picture. Pure arithmetic on numbers <see cref="Grade"/> has already measured
        /// — no allocation, no branch worth counting, nothing that needs the 0.2 s capture budget re-measured.
        /// </summary>
        private static float Composition(GradingConfig cfg, Rect view, Vector2 subjectCentre,
                                         float prominence, float framedFraction)
        {
            // The trapezoid, resolved as a consistent set: falloffBelow < idealMin <= idealMax < falloffAbove
            // is GUARANTEED here however the asset was authored, so the InverseLerps below cannot divide by
            // a zero width.
            cfg.ResolveProminenceCurve(out float falloffBelow, out float idealMin,
                                       out float idealMax, out float falloffAbove);

            // --- Prominence term ----------------------------------------------------------------------
            float prominenceTerm;
            if (prominence < idealMin)
                prominenceTerm = Mathf.InverseLerp(falloffBelow, idealMin, prominence);
            else if (prominence > idealMax)
                // Arguments deliberately reversed: InverseLerp(above, idealMax, x) descends — 1 at the top
                // of the sweet spot, 0 at the far falloff.
                prominenceTerm = Mathf.InverseLerp(falloffAbove, idealMax, prominence);
            else
                prominenceTerm = 1f;

            // --- Placement term (distance from the centre of the frame) --------------------------------
            //
            // ⚠️ CENTRED SCORES HIGHEST. This was built the other way first — a bonus for sitting near a
            // rule-of-thirds line, as GDD FR6 originally specified — and the photographs disproved it.
            // Shown matched pairs (same subject, same instant, same camera position, differing ONLY in
            // where in the frame he sits), Alexv judged the centred shot the better photograph every time:
            // first in the rig's empty world, then again in the real town after that result was re-tested
            // to rule out "the thirds shot only looks lopsided because the rest of the frame is empty".
            // FR6 was rewritten to match the game rather than the code to match FR6.
            //
            // Normalized against cam.pixelRect, NEVER Screen.width/height: an OFFSET viewport (split screen,
            // a picture-in-picture viewfinder) has a pixelRect that does not start at zero, and Story 1.9's
            // review reproduced a subject dead-centre in such a viewport measuring 0% from exactly that
            // confusion. `view` is the pixelRect, already computed in Grade.
            //
            // ⚠️ THE UNCLAMPED CENTRE, not rect.center. `rect` is clamped to the viewport, so for a subject
            // spilling past an edge its centre is dragged back INTO frame: a box projecting to x ∈ [0.4, 1.4]
            // has a true centre of 0.9 but a clamped centre of 0.7. The further out of frame he went, the
            // more centred this term believed he was — a subject half out of the picture earned a centring
            // BONUS (review 2026-07-28). The cut-off term below is the right place to punish spilling out of
            // frame; placement should answer "where is he", which is a question about where he actually is.
            float cx = (subjectCentre.x - view.xMin) / view.width;
            float cy = (subjectCentre.y - view.yMin) / view.height;

            float dx = cx - 0.5f;
            float dy = cy - 0.5f;
            float centreScore = 1f - Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy) / MaxCentreDistance);

            // Weighted so it can only ever shave a fraction off: this is here to punish a subject shoved
            // into a corner, not to micro-manage a shot that is slightly off-centre. Note there is no
            // "relief as the subject grows" term any more — it existed to stop a frame-filling subject
            // being punished for being centred, and centred is now the thing being rewarded.
            float placementTerm = 1f - cfg.SafeCentreWeight * (1f - centreScore);

            // --- Cut-off term -------------------------------------------------------------------------
            // The frame edge slicing through the subject costs composition. This is also what stops a
            // nose-to-nose shot reading as perfectly prominent: its box fills the frame precisely BECAUSE
            // most of him is outside it.
            float framingTerm = 1f - cfg.SafeCutoffWeight * (1f - Mathf.Clamp01(framedFraction));

            return Mathf.Clamp01(prominenceTerm * placementTerm * framingTerm);
        }

        /// <summary>
        /// Timing (FR7): how close to the peak the shutter fell, read from the LIVE event lifecycle.
        ///
        /// Reads <see cref="ISubject.PeakOffset"/>, not <c>Mathf.Abs(TimeToPeak)</c>. The peak is a 1.5 s
        /// interval and TimeToPeak is 0 at its START, so measuring from there scores the last frame of the
        /// money shot as though it were 1.5 s early — see ISubject.PeakOffset for the full account.
        ///
        /// There is no capture timestamp anywhere in this codebase and none is needed: grading runs
        /// synchronously inside <c>Capture()</c>, so "now" IS the moment of the shutter.
        /// </summary>
        private static float Timing(GradingConfig cfg, ISubject subject, out float peakOffset)
        {
            cfg.ResolveTimingWindow(out float fullSeconds, out float zeroSeconds);

            peakOffset = subject.PeakOffset;

            // NaN fails every comparison, so without this an odd subject state would slide past both bounds
            // into InverseLerp and hand a NaN score to ShotGrade, the HUD and the gallery. (ShotGrade guards
            // it again on the way in; this keeps the reported offset honest as well as the score.)
            //
            // Infinity needs no special case and deliberately gets none: it is what ISubject.PeakOffset
            // reports for a subject that is not in a lifecycle at all, and Abs(Inf) >= zeroSeconds falls
            // through to the hard 0 below — which is the correct score for "nowhere near the peak".
            if (float.IsNaN(peakOffset)) return 0f;

            float distance = Mathf.Abs(peakOffset);
            if (distance <= fullSeconds) return 1f;
            if (distance >= zeroSeconds) return 0f;

            // Reversed arguments again: 1 at the edge of the full-marks window, 0 at the zero point.
            return Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(zeroSeconds, fullSeconds, distance));
        }

        // Every early-out above the occlusion gate reports NotEvaluated rather than 0 line-of-sight.
        private static GradeDetail Fail(GradeMiss miss) =>
            new GradeDetail(miss, default, 0f, GradeDetail.NotEvaluated);

        /// <summary>Null check that also catches a DESTROYED Unity object behind an interface reference.</summary>
        private static bool IsNullOrDestroyed(ISubject subject) =>
            subject is UnityEngine.Object o ? o == null : subject is null;

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

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
        /// <param name="unclampedCentre">Pixel-space centre of the projection BEFORE the viewport clamp —
        /// where the subject actually is, including the part that fell outside the frame. The placement
        /// term needs this; measuring from the clamped rect made a subject spilling off-screen read as
        /// better centred the further out he went.</param>
        private static bool TryGetScreenRect(Camera cam, Rect view, Bounds bounds,
                                             out Rect rect, out bool fullyOffscreen, out float framedFraction,
                                             out Vector2 unclampedCentre)
        {
            fullyOffscreen = false;
            framedFraction = 1f;
            unclampedCentre = view.center;   // harmless default; every early-out below is a miss

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
            // Captured BEFORE the clamp below — that is the whole point of it.
            unclampedCentre = new Vector2((minX + maxX) * 0.5f, (minY + maxY) * 0.5f);

            float x0 = Mathf.Clamp(minX, view.xMin, view.xMax);
            float x1 = Mathf.Clamp(maxX, view.xMin, view.xMax);
            float y0 = Mathf.Clamp(minY, view.yMin, view.yMax);
            float y1 = Mathf.Clamp(maxY, view.yMin, view.yMax);

            rect = new Rect(x0, y0, Mathf.Max(0f, x1 - x0), Mathf.Max(0f, y1 - y0));

            // How much of him survived that clamp. Story 1.9 computed the unclamped extent and then threw it
            // away, so "the frame edge is cutting through the subject" — the difference between a portrait
            // and a photograph of somebody's elbow — was invisible to grading. Composition needs it, and it
            // costs one division we already had the operands for.
            //
            // Falls back to 1 (fully framed) rather than 0 when the unclamped box has no area: a
            // zero-extent projection means there is nothing to have cut off, and reporting 0 would apply the
            // maximum cut-off penalty to a subject nothing had clipped.
            float unclampedArea = Mathf.Max(0f, maxX - minX) * Mathf.Max(0f, maxY - minY);
            framedFraction = unclampedArea > 0f
                ? Mathf.Clamp01((rect.width * rect.height) / unclampedArea)
                : 1f;

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
