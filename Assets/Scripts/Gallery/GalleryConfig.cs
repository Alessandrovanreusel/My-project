using UnityEngine;

namespace CameraGame.Gallery
{
    /// <summary>
    /// Designer-facing tunables for the gallery (Story 1.11): how big a stored thumbnail is, and how many
    /// the player keeps. Lives as a ScriptableObject asset (Assets/Data/Gallery/GalleryConfig.asset)
    /// assigned to <see cref="GalleryService"/> in the Inspector, so both can be re-tuned WITHOUT a
    /// recompile (architecture §Configuration). Mirrors <c>CaptureConfig</c> / <c>GradingConfig</c> — no
    /// gallery magic numbers live in code.
    ///
    /// These two numbers are the ONLY thing standing between this game and an unbounded native-memory leak
    /// (NFR3), because a stored thumbnail is a <c>Texture2D</c> and the garbage collector never reclaims
    /// one. That is why they are validated as hard as the grading thresholds are: see
    /// <see cref="TryGetConfigProblem"/>.
    ///
    /// ⚠️ EVERY VALUE IS READ THROUGH ITS <c>Safe*</c> ACCESSOR, never raw. <c>[Range]</c>/<c>[Min]</c> and
    /// <c>OnValidate</c> are BOTH editor-only, and this project hand-authors these assets as YAML (Unity MCP
    /// cannot create custom ScriptableObject assets), which passes through neither. A zero typed into the
    /// .asset file therefore reaches the gallery intact — and a zero <see cref="maxStoredShots"/> is a
    /// gallery that silently stores nothing while the console stays perfectly clean, which is this
    /// project's single most repeated failure mode (cueRadius = 0 in 1.8; minCoverage = NaN and
    /// minVisibleSamples = 0 in 1.9).
    /// </summary>
    [CreateAssetMenu(menuName = "CameraGame/Gallery/Gallery Config", fileName = "GalleryConfig")]
    public class GalleryConfig : ScriptableObject
    {
        [Header("Thumbnail")]

        [Tooltip("Height in pixels of the picture stored for each shot. THIS IS THE QUALITY DIAL — the " +
                 "width is not authored, it is derived from the game window's aspect ratio at the moment " +
                 "the shutter fires, so a stored photograph always frames exactly what the player framed. " +
                 "Raise this for sharper thumbnails and a bigger memory bill; the bill is width x height x " +
                 "3 bytes per shot, and the width moves with the window.")]
        [Range(MinThumbnailSize, MaxThumbnailSize)] public int thumbnailHeight = DefaultThumbnailHeight;

        [Tooltip("CEILING on the derived width, in pixels — a memory guard, not a size. The width normally " +
                 "comes from the window: at this height, a 16:9 window gives 480, 16:10 gives 432, and an " +
                 "ultrawide 21:9 gives 630. The default 640 covers everything up to 21:9 without clamping.\n\n" +
                 "If a window is wider than this allows, the width is clamped and the stored picture stops " +
                 "matching the graded frame — GalleryService says so once when that actually happens, " +
                 "rather than staying quiet about a photograph that is subtly not the shot you took.")]
        [UnityEngine.Serialization.FormerlySerializedAs("thumbnailWidth")]
        [Range(MinThumbnailSize, MaxThumbnailSize)] public int maxThumbnailWidth = DefaultMaxThumbnailWidth;

        [Header("Capacity")]

        [Tooltip("How many shots the gallery keeps. When the player takes one more, the OLDEST is dropped " +
                 "and its picture is destroyed — which is the only thing that frees the native memory it " +
                 "holds (NFR3). Raising this raises the worst-case memory bill linearly: at the default " +
                 "270px tall with a 640px ceiling each shot costs at most about 506 KB, so 50 shots is " +
                 "roughly 24.7 MB worst case — less in practice, since the width follows the window.")]
        [Range(MinStoredShots, MaxStoredShots)] public int maxStoredShots = DefaultMaxStoredShots;

        // --- Design defaults and clamp bounds ---------------------------------------------------------
        //
        // Named rather than repeated as literals inside each accessor. Story 1.10 shipped accessors whose
        // fallback duplicated the field initializer as a bare number in a second place, so retuning a field
        // left the corrupt-asset fallback silently restoring the OLD value (review 2026-07-28). One
        // constant, referenced by both the initializer and the accessor, cannot drift.

        private const int DefaultThumbnailHeight   = 270;
        private const int DefaultMaxThumbnailWidth = 640;   // 270 x 21:9 = 630, so nothing normal clamps
        private const int DefaultMaxStoredShots    = 50;

        /// <summary>Aspect used when the live camera reports something unusable (NaN, infinity, zero or a
        /// negative). 16:9 rather than 1:1 because it is what this game is authored against, and a square
        /// thumbnail would be a far stranger thing to find in a gallery than a slightly wrong one.</summary>
        public const float FallbackAspect = 16f / 9f;

        /// <summary>Smallest usable thumbnail edge. Below about this the picture stops being recognisable
        /// as a photograph, which defeats the point of a gallery.</summary>
        public const int MinThumbnailSize = 16;

        /// <summary>Largest thumbnail edge. Not an arbitrary ceiling: the memory bill is width x height x
        /// maxStoredShots, so this is what stops a single mistyped digit from reserving gigabytes.</summary>
        public const int MaxThumbnailSize = 1920;

        /// <summary>A gallery must be able to hold at least one shot, or it is not a gallery.</summary>
        public const int MinStoredShots = 1;

        /// <summary>Ceiling on stored shots. The MVP slice's gallery is a roll of film, not an archive;
        /// Epic 5's disk persistence is what makes "keep everything" possible.</summary>
        public const int MaxStoredShots = 500;

        /// <summary>Bytes per pixel of the stored image. RGB24 — no alpha, because a photograph has no
        /// transparency and the fourth channel would cost a third more memory for nothing.</summary>
        public const int BytesPerPixel = 3;

        /// <summary>The worst case this project is willing to spend on the gallery, in bytes. Purely an
        /// authoring guard-rail: exceeding it warns, it does not clamp, because a deliberate high-fidelity
        /// setting is a legitimate designer choice and NFR3 is about leaks, not about a chosen budget.</summary>
        public const long MemoryWarnBytes = 128L * 1024L * 1024L;   // 128 MB

        // --- Safe accessors ---------------------------------------------------------------------------

        /// <summary>Width ceiling, guaranteed usable however the asset was authored.</summary>
        public int SafeMaxThumbnailWidth => Mathf.Clamp(maxThumbnailWidth, MinThumbnailSize, MaxThumbnailSize);

        /// <summary>Thumbnail height, guaranteed usable however the asset was authored.</summary>
        public int SafeThumbnailHeight => Mathf.Clamp(thumbnailHeight, MinThumbnailSize, MaxThumbnailSize);

        /// <summary>
        /// The width to store a shot at, for the aspect the camera is ACTUALLY rendering. This is what
        /// makes a stored photograph frame what the player framed, instead of a fixed 16:9 slice of it.
        ///
        /// NaN and infinity are handled explicitly rather than clamped: Mathf.Clamp(NaN) is NaN and
        /// Mathf.RoundToInt(NaN) is 0, which would reach the Texture2D constructor as a zero width and
        /// throw inside the shutter. Same discipline as GradingConfig's Clamp01Finite.
        /// </summary>
        public int ThumbnailWidthFor(float aspect)
        {
            if (float.IsNaN(aspect) || float.IsInfinity(aspect) || aspect <= 0f) aspect = FallbackAspect;
            return Mathf.Clamp(Mathf.RoundToInt(SafeThumbnailHeight * aspect),
                               MinThumbnailSize, SafeMaxThumbnailWidth);
        }

        /// <summary>True when this aspect is wider than <see cref="SafeMaxThumbnailWidth"/> allows, i.e.
        /// the stored picture will NOT match the graded frame. The only case worth warning about now that
        /// the width follows the window.</summary>
        public bool WouldClampWidth(float aspect)
        {
            if (float.IsNaN(aspect) || float.IsInfinity(aspect) || aspect <= 0f) aspect = FallbackAspect;
            return Mathf.RoundToInt(SafeThumbnailHeight * aspect) > SafeMaxThumbnailWidth;
        }

        /// <summary>Stored-shot cap, guaranteed at least 1. Falls back through a clamp rather than to the
        /// design default so a deliberate 500 survives, while a hand-authored 0 fails CLOSED (one shot
        /// kept) instead of open (unbounded, i.e. the leak).</summary>
        public int SafeMaxStoredShots => Mathf.Clamp(maxStoredShots, MinStoredShots, MaxStoredShots);

        /// <summary>Native bytes a stored thumbnail costs AT ITS WORST, i.e. at the width ceiling. Any
        /// given shot costs less whenever the window is narrower than the ceiling allows, but a memory
        /// budget has to be stated against the worst case rather than against today's window.</summary>
        public long BytesPerShot => (long)SafeMaxThumbnailWidth * SafeThumbnailHeight * BytesPerPixel;

        /// <summary>Native bytes a shot costs at a specific aspect, i.e. what it actually costs today.</summary>
        public long BytesPerShotAt(float aspect) =>
            (long)ThumbnailWidthFor(aspect) * SafeThumbnailHeight * BytesPerPixel;

        /// <summary>Native bytes the gallery costs with every slot full — the number NFR3 is about.</summary>
        public long WorstCaseBytes => BytesPerShot * SafeMaxStoredShots;

        /// <summary>Human-readable memory budget, for the one-line log the service writes at Awake. Stating
        /// the number beats asserting the bound: AC4 asks for both figures, and a budget nobody ever prints
        /// is a budget nobody notices being blown.</summary>
        public string DescribeBudget() =>
            $"{SafeThumbnailHeight}px tall, width follows the window (ceiling {SafeMaxThumbnailWidth}) - " +
            $"up to {BytesPerShot / 1024f:0} KB per shot, x{SafeMaxStoredShots} shots = " +
            $"{WorstCaseBytes / (1024f * 1024f):0.0} MB worst case";

        /// <summary>
        /// Reports authoring mistakes that would break the gallery SILENTLY rather than loudly — the
        /// project's standing "fail-soft must not mean invisible" rule, added after a <c>cueRadius</c> of 0
        /// produced total silence with a completely clean console (Story 1.8's review). Called once at
        /// <c>Awake</c> by <see cref="GalleryService"/>; returns false when everything is sane.
        /// </summary>
        public bool TryGetConfigProblem(out string problem)
        {
            // Out of range, i.e. SILENTLY CLAMPED. Checked first and reported with BOTH numbers, because the
            // value the designer typed is not the value being used and nothing else in the pipeline would
            // ever say so — [Range] and OnValidate are editor-only, and these assets are hand-written YAML.
            if (thumbnailHeight != SafeThumbnailHeight)
            {
                problem = $"thumbnailHeight is {thumbnailHeight}, outside the usable range {MinThumbnailSize} " +
                          $"to {MaxThumbnailSize} — the gallery will store {SafeThumbnailHeight}px tall instead.";
                return true;
            }

            if (maxThumbnailWidth != SafeMaxThumbnailWidth)
            {
                problem = $"maxThumbnailWidth is {maxThumbnailWidth}, outside the usable range " +
                          $"{MinThumbnailSize} to {MaxThumbnailSize} - the gallery will cap widths at " +
                          $"{SafeMaxThumbnailWidth}px instead. A non-positive ceiling would have clamped " +
                          "every thumbnail to a sliver, or thrown inside the Texture2D constructor.";
                return true;
            }

            // The silent one in this shape: a ceiling narrower than the HEIGHT clamps every window wider
            // than a square, which is every window this game will ever run in - so no stored photograph
            // would ever match the frame it was graded in, and the per-capture warning would fire forever
            // without pointing at anything the reader could act on.
            if (SafeMaxThumbnailWidth < SafeThumbnailHeight)
            {
                problem = $"maxThumbnailWidth ({SafeMaxThumbnailWidth}) is below thumbnailHeight " +
                          $"({SafeThumbnailHeight}), so the derived width is clamped for any window wider " +
                          "than a square. Stored pictures would never frame what was graded. Raise it: " +
                          $"16:9 needs {Mathf.RoundToInt(SafeThumbnailHeight * 16f / 9f)}, 21:9 needs " +
                          $"{Mathf.RoundToInt(SafeThumbnailHeight * 21f / 9f)}.";
                return true;
            }

            if (maxStoredShots != SafeMaxStoredShots)
            {
                problem = $"maxStoredShots is {maxStoredShots}, outside the usable range {MinStoredShots} to " +
                          $"{MaxStoredShots} — the gallery will keep {SafeMaxStoredShots}. A zero or negative " +
                          "cap would have evicted every shot the instant it was stored, so the player would " +
                          "photograph the whole town and find an empty gallery, with a clean console.";
                return true;
            }

            // The silent one, and the reason MemoryWarnBytes exists: nothing here crashes, nothing looks
            // wrong, and a single mistyped digit quietly reserves hundreds of megabytes of native memory
            // that the garbage collector will never touch. 1920x1080 x 500 shots is 3 GB.
            if (WorstCaseBytes > MemoryWarnBytes)
            {
                problem = $"the gallery is authored to hold {DescribeBudget()}, which is over the " +
                          $"{MemoryWarnBytes / (1024 * 1024)} MB this project budgets for it. These are " +
                          "Texture2D bytes — NATIVE memory the garbage collector never reclaims — so this " +
                          "is a real reservation, not a high-water mark. Lower thumbnail size or maxStoredShots.";
                return true;
            }

            problem = null;
            return false;
        }

        private void OnValidate()
        {
            // Through the Safe* accessors, exactly as GradingConfig does.
            //
            // ⚠️ This REWRITES HAND-AUTHORED YAML the moment the asset is selected in the Inspector — the
            // out-of-range value is destroyed by the act of looking at it. That is the same repair [Range]
            // would apply anyway; what matters is that TryGetConfigProblem reports it at Awake FIRST, so the
            // warning arrives before the Inspector silently rewrites the evidence.
            maxThumbnailWidth = SafeMaxThumbnailWidth;
            thumbnailHeight = SafeThumbnailHeight;
            maxStoredShots = SafeMaxStoredShots;
        }
    }
}
