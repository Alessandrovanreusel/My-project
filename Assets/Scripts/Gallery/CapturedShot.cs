using System;
using System.Globalization;
using UnityEngine;
using CameraGame.Grading;

namespace CameraGame.Gallery
{
    /// <summary>
    /// One photograph the player has taken and kept: the picture, the score it was given, who it is of, and
    /// when it was taken (Story 1.11, FR8). Held in memory only — <see cref="GalleryService"/> owns a plain
    /// <c>List&lt;CapturedShot&gt;</c> and there is no disk I/O anywhere in this story (AR6).
    ///
    /// A <c>readonly struct</c> of primitives, an enum, a plain struct and one texture reference, matching
    /// <see cref="ShotGrade"/> and <c>GradeDetail</c>. Nothing here is a live scene reference, which is what
    /// makes the shape below writable to disk unchanged.
    ///
    /// ================================================================================================
    /// HOW EPIC 5 TURNS ONE OF THESE INTO A PNG + JSON PAIR (AC3 — this comment IS the deliverable)
    /// ================================================================================================
    /// The point of writing this down now is that Epic 5 must be able to add persistence WITHOUT
    /// reshaping this struct. Concretely:
    ///
    ///   <see cref="Id"/>          → THE FILENAME, for both halves of the pair: "shot_0007_20260730T142233Z.png"
    ///                               and "shot_0007_20260730T142233Z.json". It is built to be file-name-safe
    ///                               (ASCII, no separators, no colons — see <see cref="MakeId"/>) and unique,
    ///                               so Epic 5 needs to invent no key of its own. A bare counter would not
    ///                               do: it restarts at 1 every session and would overwrite last night's
    ///                               photographs the first time the game wrote into a folder that already
    ///                               had files in it.
    ///
    ///   <see cref="Image"/>       → THE PNG BODY, via <c>Image.EncodeToPNG()</c>, called with no conversion
    ///                               and no import settings involved. That works because the texture is
    ///                               created at RUNTIME by <c>ReadPixels</c>, which yields an uncompressed,
    ///                               CPU-readable RGB24 texture by construction. (An IMPORTED texture would
    ///                               need "Read/Write Enabled" ticked and would usually be DXT-compressed;
    ///                               neither applies here.) Keeping it readable is therefore not an
    ///                               optimisation choice to revisit — it is the thing that makes AC3 true.
    ///                               May be null when the gallery had no camera to photograph with; the
    ///                               entry is still a valid record and Epic 5 should simply write no PNG.
    ///
    ///   <see cref="Grade"/>       → THE JSON BODY: Percent01, Stars, Subject01, Composition01, Timing01,
    ///                               MissReason (as its name, not its ordinal — reordering the enum must not
    ///                               silently re-label old saves), IsPlaceholder. Stars is a STORED field on
    ///                               ShotGrade precisely so a re-tuned GradingConfig cannot retroactively
    ///                               re-rate a photograph the player was already shown (ShotGrade.cs:98-100).
    ///
    ///   <see cref="SubjectId"/>   → a JSON string. Empty means "there was no subject", never "unknown".
    ///
    ///   <see cref="CapturedAtUtc"/> → a JSON string in ISO-8601 ROUND-TRIP form, i.e. <c>ToString("o")</c>,
    ///                               parsed back with <c>DateTime.Parse(s, null, DateTimeStyles.RoundtripKind)</c>.
    ///
    /// ⚠️ <c>JsonUtility</c> CANNOT DO THIS ON ITS OWN. It serializes neither <c>DateTime</c> nor
    /// <c>Texture2D</c> — a <c>JsonUtility.ToJson(shot)</c> would silently emit an object missing the time
    /// entirely. Epic 5 must map to a small serializable DTO (strings for the time and the miss reason)
    /// rather than reflecting over this struct. Writing that here is the whole point: it is a thing you
    /// otherwise discover by losing data.
    /// </summary>
    public readonly struct CapturedShot
    {
        /// <summary>Stable, unique, file-name-safe identifier — see the class comment. Never null or empty
        /// for a shot the service created.</summary>
        public readonly string Id;

        /// <summary>The picture, as a readable uncompressed <c>Texture2D</c> the GALLERY OWNS.
        ///
        /// ⚠️ NATIVE MEMORY. Dropping this reference does not free it; only <c>Object.Destroy</c> does. The
        /// gallery is the single owner precisely so there is exactly one place that rule has to be obeyed
        /// (<see cref="GalleryService"/>, on eviction and on destroy). Null when the gallery had no camera.
        /// </summary>
        public readonly Texture2D Image;

        /// <summary>The score the player was shown for this shot, exactly as graded at the shutter.</summary>
        public readonly ShotGrade Grade;

        /// <summary>Who the shot is of (<c>ISubject.SubjectId</c>), or empty when there was no subject.
        /// Copied off <see cref="Grade"/> so a reader never has to know where it came from.</summary>
        public readonly string SubjectId;

        /// <summary>When the shutter was pulled, in UTC WALL-CLOCK time.
        ///
        /// ⚠️ NOT <c>Time.time</c>. A session-relative float is meaningless in a gallery that outlives the
        /// session, which is exactly what Epic 5 turns this into (AC3). UTC rather than local so the
        /// filename and the JSON stay stable across time zones and daylight saving.</summary>
        public readonly DateTime CapturedAtUtc;

        public CapturedShot(string id, Texture2D image, ShotGrade grade, string subjectId, DateTime capturedAtUtc)
        {
            Id = id ?? string.Empty;
            Image = image;
            Grade = grade;
            SubjectId = subjectId ?? string.Empty;
            CapturedAtUtc = capturedAtUtc;
        }

        /// <summary>True when this entry actually has a picture. False is a supported, fail-soft state (no
        /// camera was assigned), not a bug — the grade, subject and time are still a real record.</summary>
        public bool HasImage => Image != null;

        /// <summary>True when the shot names a subject. See <see cref="ShotGrade.SubjectId"/> for why empty
        /// is used rather than a "None" sentinel.</summary>
        public bool HasSubject => !string.IsNullOrEmpty(SubjectId);

        /// <summary>
        /// Builds the identifier described in the class comment: a monotonic per-session index AND the UTC
        /// instant, e.g. <c>shot_0007_20260730T142233Z</c>.
        ///
        /// Both halves are needed. The index alone restarts every session (it would collide with yesterday's
        /// files); the timestamp alone has one-second resolution, and this game's shutter can fire several
        /// times inside one second. Together, a collision needs two sessions to reach the same index within
        /// the same second, which is not a case worth defending against in an in-memory gallery — and Epic 5
        /// gets a naturally chronological sort for free.
        ///
        /// <c>InvariantCulture</c> is not decoration: a Dutch or Arabic-locale machine would otherwise emit
        /// different digits or separators and produce filenames that do not sort or, worse, do not open.
        /// </summary>
        public static string MakeId(int index, DateTime utc) =>
            string.Format(CultureInfo.InvariantCulture, "shot_{0:D4}_{1:yyyyMMdd}T{1:HHmmss}Z", index, utc);

        /// <summary>One line for logs and the verification rig. Reads the grade through
        /// <see cref="ShotGrade.ToString"/>, so a miss says which gate rejected it rather than showing a
        /// bare "0%, 1★" that a late-but-counted shot would print identically.</summary>
        public override string ToString() =>
            $"{Id}  {(HasImage ? $"{Image.width}x{Image.height}" : "NO IMAGE")}  {Grade}  " +
            $"at {CapturedAtUtc.ToString("o", CultureInfo.InvariantCulture)}";
    }
}
