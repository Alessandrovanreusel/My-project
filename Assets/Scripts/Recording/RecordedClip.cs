using System;
using System.Globalization;

namespace CameraGame.Recording
{
    /// <summary>
    /// One finished clip, as <see cref="ClipRecordedChannel"/> announces it (Story 1.15, decision 1c). Story
    /// 1.16 adds the grade to it, and Story 1.17's <c>CapturedClip</c> is built FROM it by the memory card.
    ///
    /// ================================================================================================
    /// WHO OWNS THE FOOTAGE: AN EXPLICIT CLAIM
    /// ================================================================================================
    /// The footage is megabytes of memory handed to an unknown number of listeners, which is the question
    /// <c>GalleryService</c> answered for photographs (GalleryService.cs:16-41): a resource with no owner is a
    /// leak or a double free. The rule:
    ///
    ///   1. During the synchronous <c>Raise</c>, a listener that KEEPS the footage calls <see cref="Claim"/>.
    ///      It then owns it, and must <see cref="IFootage.Dispose"/> it when it lets go.
    ///   2. After <c>Raise</c> returns, the recorder claims every clip nobody claimed and keeps it as its single
    ///      <see cref="ClipRecorder.LastClip"/>. It disposes the previous one it held, and the last one in
    ///      <c>OnDestroy</c>. So, before a memory card exists, at most two clips' storage is ever live: the one
    ///      being recorded and the one held.
    ///   3. The claim window closes there: a second claim is refused, so a clip never has two owners.
    ///
    /// Story 1.17's card claims every clip and the recorder then holds nothing: an additive change.
    /// </summary>
    public sealed class RecordedClip
    {
        /// <summary>File-name-safe and unique across sessions: <c>clip_0001_20261115T153000Z</c> (the same
        /// rules as a photograph's id; see <see cref="MakeId"/>).</summary>
        public string Id { get; }

        /// <summary>The footage (ADR-4: <see cref="ReplayFootage"/>). Disposed by whoever claimed the clip.</summary>
        public IFootage Footage { get; }

        /// <summary>The wall-clock instant recording started, in UTC.</summary>
        public DateTime StartedAtUtc { get; }

        /// <summary>How long the clip plays for: the sum of its ticks' repeats ÷ 30.</summary>
        public float DurationSeconds { get; }

        /// <summary>Why it stopped. Every reason keeps the clip.</summary>
        public StopReason StopReason { get; }

        /// <summary>Whoever claimed the clip, or null while it is unclaimed.</summary>
        public object Owner { get; private set; }

        /// <summary>True once someone has claimed the clip.</summary>
        public bool IsClaimed => Owner != null;

        public RecordedClip(string id, IFootage footage, DateTime startedAtUtc, float durationSeconds, StopReason reason)
        {
            Id = id;
            Footage = footage;
            StartedAtUtc = startedAtUtc;
            DurationSeconds = durationSeconds;
            StopReason = reason;
        }

        /// <summary>Takes ownership of the footage. True for the first non-null owner only: a second claim
        /// is refused, so the footage never has two owners and is never disposed twice by design.</summary>
        public bool Claim(object owner)
        {
            if (owner == null || Owner != null) return false;
            Owner = owner;
            return true;
        }

        /// <summary>
        /// A file-name-safe, unique clip id. Same reasons as <c>CapturedShot.MakeId</c>: ASCII, no separators
        /// and no colons, and the UTC timestamp makes it unique across sessions (a bare counter restarts at 1
        /// every session and would overwrite last night's clips once 1.17 writes them to disk).
        /// </summary>
        public static string MakeId(int index, DateTime utc) =>
            string.Format(CultureInfo.InvariantCulture, "clip_{0:D4}_{1:yyyyMMdd}T{1:HHmmss}Z", index, utc);

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "{0}  {1:0.000} s  {2}  {3}", Id, DurationSeconds, StopReason,
                Footage == null ? "no footage" : Footage.IsDisposed ? "disposed" : (Footage.Bytes / 1024) + " KB");
    }
}
