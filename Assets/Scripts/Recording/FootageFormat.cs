using System;
using System.Globalization;

namespace CameraGame.Recording
{
    /// <summary>
    /// The invariants of ADR-4's footage format, and the pure arithmetic every clip shares (Story 1.15).
    ///
    /// ⚠️ THESE ARE NOT TUNABLES. The tick rate and the sample format define what a clip IS. Story 1.17 will
    /// persist clips and Epic 9 will edit them by tick ranges, so changing either breaks every clip already
    /// on a card. That is why they are constants with a <see cref="Version"/> rather than fields on
    /// <see cref="RecordingConfig"/>: a change here must bump the version, and a loader must refuse (or
    /// convert) an older one.
    ///
    /// Everything below is static, allocation-free and Unity-free, so the EditMode suite tests it with no
    /// scene (Task 3.3, Task 8.1).
    /// </summary>
    public static class FootageFormat
    {
        /// <summary>Format version of the footage this build records. Bump it whenever a constant below, or
        /// the meaning of a recorded field, changes.</summary>
        public const int Version = 1;

        /// <summary>Ticks per second of the track (ADR-4). One tick stands for one 1/30-s slot of playback.</summary>
        public const int TickRate = 30;

        /// <summary>The PCM is stored as signed 16-bit (ADR-4): ~192 KB/s for 48 kHz stereo.</summary>
        public const int BytesPerSample = 2;

        /// <summary>
        /// How many 1/30-s boundaries of the clip's timeline lie at or before <paramref name="clipSeconds"/>.
        /// The boundaries are at 0, 1/30, 2/30, … so the start of the clip (0 s) already counts one: tick 0 is
        /// taken on the start frame. Negative or non-finite times count none.
        ///
        /// A tick's repeat count is the difference between this and the boundaries already emitted, so a game
        /// running below 30 fps still yields a clip of the true duration (each tick stands for several slots).
        /// </summary>
        public static long BoundariesThrough(double clipSeconds)
        {
            if (!(clipSeconds >= 0d) || double.IsInfinity(clipSeconds)) return 0;
            double slots = Math.Floor(clipSeconds * TickRate);
            // Saturate: an unchecked (long) of a double past ~9.2e18 wraps to long.MinValue (code review 2026-11-15).
            return slots >= MaxSlots ? long.MaxValue : (long)slots + 1;
        }

        /// <summary>
        /// The repeat count of a tick taken at <paramref name="clipSeconds"/>, given how many boundaries earlier
        /// ticks already covered and the most a clip may hold. 0 means no tick is due on this frame.
        /// </summary>
        public static int RepeatsDue(double clipSeconds, long emittedBoundaries, long maxBoundaries)
        {
            long due = BoundariesThrough(clipSeconds);
            if (due > maxBoundaries) due = maxBoundaries;
            long repeats = due - emittedBoundaries;
            if (repeats <= 0) return 0;
            return repeats > int.MaxValue ? int.MaxValue : (int)repeats;
        }

        /// <summary>Seconds of playback that <paramref name="boundaries"/> tick slots stand for.</summary>
        public static double SecondsFor(long boundaries) => boundaries <= 0 ? 0d : (double)boundaries / TickRate;

        /// <summary>The most boundaries a clip of <paramref name="maxSeconds"/> may hold (at least 1, so a clip
        /// always has its start tick).</summary>
        public static long MaxBoundariesFor(double maxSeconds)
        {
            if (!(maxSeconds > 0d) || double.IsInfinity(maxSeconds)) return 1;
            double slots = Math.Floor(maxSeconds * TickRate + 1e-6);
            if (slots >= MaxSlots) return long.MaxValue;
            long n = (long)slots;
            return n < 1 ? 1 : n;
        }

        // Below long.MaxValue with room for the +1, and exactly representable as a double.
        private const double MaxSlots = 9.0e18;

        /// <summary>
        /// The exact number of PCM frames (samples per channel) that a clip of <paramref name="boundaries"/>
        /// slots must hold at <paramref name="sampleRate"/>. The PCM is padded with silence or trimmed to this
        /// at the end of every clip, so picture and sound always last the same time. (The spike's takes were up
        /// to two DSP blocks off, and ffmpeg's <c>-shortest</c> once cut a 301-frame export to 298.)
        /// Rounded to the nearest frame; at 48 kHz every tick is exactly 1600 frames.
        /// </summary>
        public static long PcmFramesFor(long boundaries, int sampleRate)
        {
            if (boundaries <= 0 || sampleRate <= 0) return 0;
            return (boundaries * sampleRate + TickRate / 2) / TickRate;
        }

        /// <summary>
        /// One float sample of the listener's mix as 16-bit PCM: clamped to ±1 (a hot mix clips rather than
        /// wrapping round), NaN written as silence. Symmetric: +1 → 32767 and −1 → −32767.
        /// </summary>
        public static short ToPcm16(float sample)
        {
            if (float.IsNaN(sample)) return 0;
            if (sample >= 1f) return short.MaxValue;
            if (sample <= -1f) return -short.MaxValue;
            return (short)Math.Round(sample * short.MaxValue);
        }

        /// <summary>The 16-bit sample back as a float in [−1, 1] (for readers such as the verification rig).</summary>
        public static float FromPcm16(short sample) => sample / (float)short.MaxValue;
    }

    /// <summary>
    /// The camcorder's running timecode, "H:MM:SS" (Story 1.15, decision 1g).
    ///
    /// ⚠️ THE CULTURE IS PINNED TO INVARIANT (loop lessons, 1-19): a player-facing number must not follow the
    /// player's locale, whose digits, separators or signs the HUD font may not even contain.
    ///
    /// The time shown is the FOOTAGE's (<see cref="ClipRecorder.ElapsedSeconds"/>), not <c>Time.time</c>, so
    /// the number on screen is the length of the clip being recorded. It floors: a clip of 59.999 s reads
    /// 0:00:59, as a camcorder's counter does. NaN, infinities and negative values read 0:00:00.
    /// </summary>
    public static class ClipTimecode
    {
        /// <summary>The whole seconds the timecode would show. The HUD rebuilds its string only when this
        /// changes, so there is no string per frame.</summary>
        public static long WholeSeconds(double seconds)
        {
            if (!(seconds > 0d) || double.IsInfinity(seconds)) return 0;
            double f = Math.Floor(seconds);
            return f >= long.MaxValue ? long.MaxValue : (long)f;
        }

        /// <summary>"H:MM:SS" for <paramref name="seconds"/>, in the invariant culture.</summary>
        public static string Format(double seconds)
        {
            long s = WholeSeconds(seconds);
            long h = s / 3600;
            int m = (int)(s / 60 % 60);
            int sec = (int)(s % 60);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}", h, m, sec);
        }
    }
}
