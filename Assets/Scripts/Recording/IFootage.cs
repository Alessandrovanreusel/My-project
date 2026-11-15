namespace CameraGame.Recording
{
    /// <summary>
    /// The footage handle of a recorded clip: ADR-4's <c>&lt;footage handle&gt;</c> inside
    /// <c>CapturedClip</c>. Kept free of replay specifics on purpose, so that a veto of ADR-4 swaps the
    /// backend and not the design (game-architecture.md, ADR-4 "What Story 1.15 builds").
    ///
    /// <see cref="ReplayFootage"/> is the first implementation: a 30 Hz track plus the listener's PCM.
    /// </summary>
    public interface IFootage
    {
        /// <summary>How long the clip plays for, in seconds. 0 once disposed.</summary>
        float Seconds { get; }

        /// <summary>The memory the footage holds, in bytes. 0 once disposed.</summary>
        long Bytes { get; }

        /// <summary>True once <see cref="Dispose"/> has run. A disposed footage reads as empty.</summary>
        bool IsDisposed { get; }

        /// <summary>Releases the footage's memory. Idempotent: a second call does nothing. MAIN THREAD ONLY: the
        /// replay footage hands its storage back to the recorder's pool, which is not synchronised (a future owner
        /// writing a clip to disk on a worker must dispose it back on the main thread).</summary>
        void Dispose();
    }
}
