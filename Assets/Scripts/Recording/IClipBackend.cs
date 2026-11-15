namespace CameraGame.Recording
{
    /// <summary>
    /// The narrow seam between <see cref="ClipRecorder"/> and the thing that actually captures footage
    /// (ADR-4, "What Story 1.15 builds"). The recorder owns WHEN a clip runs (start, stop, the reasons, the
    /// channel, the ownership rule); a backend owns WHAT is captured and how it is stored.
    /// <see cref="ReplayBackend"/> is the first implementation.
    ///
    /// All members are called on the main thread.
    /// </summary>
    public interface IClipBackend
    {
        /// <summary>Starts a clip. <paramref name="startDspTime"/> is <c>AudioSettings.dspTime</c> read at the
        /// press: the clip's time zero on the audio sample clock, which the picture is stamped against too
        /// (decision 1d). <paramref name="maxSeconds"/> bounds the clip. False when the backend cannot record
        /// (nothing to film through, no storage); it then holds nothing.</summary>
        bool Begin(double startDspTime, float maxSeconds);

        /// <summary>Called once per game frame while recording, after the camera's final write, with the clip
        /// time in seconds since <see cref="Begin"/>. The backend decides whether a 30 Hz tick is due.
        /// Returns the repeat count of the tick it took (how many 1/30-s slots it stands for), or 0 when no tick
        /// was taken on this frame (none due, the clip full, or no clip running). Story 1.16 grades exactly the
        /// ticks this reports, so the grade describes exactly the frames the footage holds.</summary>
        int Sample(double clipSeconds);

        /// <summary>Seconds of footage so far (what the clip would play for if it stopped now).</summary>
        double RecordedSeconds { get; }

        /// <summary>True when the backend needs the clip stopped (it is full, or its sound can no longer share
        /// the timeline). The recorder stops with the given reason and keeps the clip.</summary>
        bool TryGetStopRequest(out StopReason reason);

        /// <summary>Ends the clip and hands back its footage. Never waits on another thread for longer than
        /// one audio block's copy.</summary>
        IFootage End();

        /// <summary>Ends the clip and throws its footage away. Only for teardown paths that cannot keep it.</summary>
        void Abort();
    }
}
