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

        [Tooltip("Width in pixels of the picture stored for each shot. The default 480x270 is 16:9 — the " +
                 "aspect the camera renders — so the thumbnail frames exactly what was graded. If you " +
                 "change one dimension, change the other to match, or the stored picture will show a " +
                 "wider or narrower field of view than the player saw. (GalleryService warns once at the " +
                 "first capture whose camera aspect disagrees with this one.)")]
        [Range(MinThumbnailSize, MaxThumbnailSize)] public int thumbnailWidth = DefaultThumbnailWidth;

        [Tooltip("Height in pixels of the picture stored for each shot. See thumbnailWidth — keep the pair " +
                 "at the camera's aspect ratio.")]
        [Range(MinThumbnailSize, MaxThumbnailSize)] public int thumbnailHeight = DefaultThumbnailHeight;

        [Header("Capacity")]

        [Tooltip("How many shots the gallery keeps. When the player takes one more, the OLDEST is dropped " +
                 "and its picture is destroyed — which is the only thing that frees the native memory it " +
                 "holds (NFR3). Raising this raises the worst-case memory bill linearly: at the default " +
                 "480x270 each shot costs about 380 KB, so 50 shots is roughly 19 MB.")]
        [Range(MinStoredShots, MaxStoredShots)] public int maxStoredShots = DefaultMaxStoredShots;

        // --- Design defaults and clamp bounds ---------------------------------------------------------
        //
        // Named rather than repeated as literals inside each accessor. Story 1.10 shipped accessors whose
        // fallback duplicated the field initializer as a bare number in a second place, so retuning a field
        // left the corrupt-asset fallback silently restoring the OLD value (review 2026-07-28). One
        // constant, referenced by both the initializer and the accessor, cannot drift.

        private const int DefaultThumbnailWidth  = 480;
        private const int DefaultThumbnailHeight = 270;
        private const int DefaultMaxStoredShots  = 50;

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

        /// <summary>Thumbnail width, guaranteed usable however the asset was authored.</summary>
        public int SafeThumbnailWidth => Mathf.Clamp(thumbnailWidth, MinThumbnailSize, MaxThumbnailSize);

        /// <summary>Thumbnail height, guaranteed usable however the asset was authored.</summary>
        public int SafeThumbnailHeight => Mathf.Clamp(thumbnailHeight, MinThumbnailSize, MaxThumbnailSize);

        /// <summary>Stored-shot cap, guaranteed at least 1. Falls back through a clamp rather than to the
        /// design default so a deliberate 500 survives, while a hand-authored 0 fails CLOSED (one shot
        /// kept) instead of open (unbounded, i.e. the leak).</summary>
        public int SafeMaxStoredShots => Mathf.Clamp(maxStoredShots, MinStoredShots, MaxStoredShots);

        /// <summary>Native bytes one stored thumbnail costs, at the resolved (Safe) size.</summary>
        public long BytesPerShot => (long)SafeThumbnailWidth * SafeThumbnailHeight * BytesPerPixel;

        /// <summary>Native bytes the gallery costs with every slot full — the number NFR3 is about.</summary>
        public long WorstCaseBytes => BytesPerShot * SafeMaxStoredShots;

        /// <summary>The thumbnail's aspect ratio, for comparison against the live camera's.</summary>
        public float ThumbnailAspect => SafeThumbnailWidth / (float)SafeThumbnailHeight;

        /// <summary>Human-readable memory budget, for the one-line log the service writes at Awake. Stating
        /// the number beats asserting the bound: AC4 asks for both figures, and a budget nobody ever prints
        /// is a budget nobody notices being blown.</summary>
        public string DescribeBudget() =>
            $"{SafeThumbnailWidth}x{SafeThumbnailHeight} RGB24 = {BytesPerShot / 1024f:0} KB per shot, " +
            $"x{SafeMaxStoredShots} shots = {WorstCaseBytes / (1024f * 1024f):0.0} MB worst case";

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
            if (thumbnailWidth != SafeThumbnailWidth)
            {
                problem = $"thumbnailWidth is {thumbnailWidth}, outside the usable range {MinThumbnailSize} " +
                          $"to {MaxThumbnailSize} — the gallery will store {SafeThumbnailWidth}px wide " +
                          "instead. A non-positive width would have thrown inside the Texture2D constructor " +
                          "on the first capture.";
                return true;
            }

            if (thumbnailHeight != SafeThumbnailHeight)
            {
                problem = $"thumbnailHeight is {thumbnailHeight}, outside the usable range {MinThumbnailSize} " +
                          $"to {MaxThumbnailSize} — the gallery will store {SafeThumbnailHeight}px tall instead.";
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
            thumbnailWidth = SafeThumbnailWidth;
            thumbnailHeight = SafeThumbnailHeight;
            maxStoredShots = SafeMaxStoredShots;
        }
    }
}
