using System;
using System.Threading;
using UnityEngine;
using CameraGame.Core;

namespace CameraGame.Recording
{
    /// <summary>
    /// Records the sound the player hears into a clip's PCM (Story 1.15, AC5; ADR-4). It must sit on the
    /// <b>AudioListener's GameObject</b> (Main Camera): <c>OnAudioFilterRead</c> there receives the listener's
    /// whole mix just before it goes to the speakers. Anywhere else it hears only its own GameObject's
    /// sources. <c>Awake</c> checks this and warns once (the spike assumed it with no check).
    ///
    /// What it records: everything the listener hears, EXCEPT a source with <c>bypassListenerEffects = true</c>,
    /// which skips the listener's filter chain and so this tap (the Recordability rule in
    /// game-architecture.md; measured by Story 1.14 and again by 1.15 for the REC beeps).
    ///
    /// ================================================================================================
    /// TWO THREADS — THE RULES
    /// ================================================================================================
    /// <c>OnAudioFilterRead</c> runs on the AUDIO thread, for every audio block of the whole session,
    /// recording or not. So:
    ///   - Idle costs one interlocked compare per block and nothing else.
    ///   - While armed it allocates nothing and calls exactly one Unity API, <c>AudioSettings.dspTime</c>,
    ///     which Unity's own <c>dspTime</c> metronome example reads from this callback (and Story 1.15's rig
    ///     logs from here to prove it is the block's own DSP time).
    ///   - The only shared state is the fields set by <see cref="Arm"/> before the state is published, and
    ///     <c>_state</c>, an int moved only by <see cref="Interlocked"/>: Idle → Armed (main), Armed → Writing
    ///     → Armed (audio, around each block's copy), Armed → Idle (main, in <see cref="Disarm"/>). Disarm can
    ///     therefore never complete while a block is half-written, and once it returns the audio thread can
    ///     no longer touch the buffer. The wait lasts at most one block's copy (a few microseconds), so it is
    ///     a <c>SpinWait</c>, not a lock or a Sleep loop. Strictly, <c>SpinWait</c> starts yielding (up to
    ///     <c>Sleep(1)</c>) after about ten spins, which only happens if the audio thread is preempted in the
    ///     middle of a copy; that costs the stop frame a few milliseconds at worst (code review 2026-11-15).
    ///
    /// ================================================================================================
    /// THE CLOCK (decision 1d)
    /// ================================================================================================
    /// The clip's time zero is <c>AudioSettings.dspTime</c> read on the main thread at the press. Each block
    /// knows its own DSP time, so its samples land at <c>(blockDspTime − start) × rate</c> in the clip: a heavy
    /// press frame delays nothing (the spike started its PCM at "the next callback" and was 160–290 ms late on
    /// heavy frames). Gaps are zero-filled, samples before the start are skipped, and the recorder pads or
    /// trims the PCM to the tick timeline at the end.
    /// </summary>
    public sealed class ListenerTap : MonoBehaviour
    {
        private const int Idle = 0, Armed = 1, Writing = 2;

        // Set by Arm on the main thread BEFORE _state is published as Armed; read by the audio thread only
        // while it holds Writing. Never written while Armed or Writing.
        private short[] _buffer;
        private int _channels;
        private int _sampleRate;
        private long _startFrame;      // the clip's time zero, in absolute DSP sample frames
        private long _capacityFrames;

        // Written by the audio thread while Writing; read by the main thread only after Disarm.
        private long _writtenFrames;   // frames from the clip start that hold real samples or zero-filled gaps
        private long _firstBlockFrame = long.MinValue;
        private bool _channelMismatch;

        private int _state;            // Idle / Armed / Writing, Interlocked only

        private volatile bool _sawBlock;   // diagnostic: at least one block arrived while armed

        /// <summary>True when this tap shares its GameObject with an enabled AudioListener (checked in Awake).</summary>
        public bool OnListener { get; private set; }

        /// <summary>True while a clip's buffer is armed.</summary>
        public bool IsArmed => Volatile.Read(ref _state) != Idle;

        /// <summary>Set by the audio thread when a block arrived with a channel count other than the clip's
        /// (the speaker mode changed). The clip's sound stops there: what was written before is kept and the rest
        /// of the clip is padded with silence; the recorder stops the clip.</summary>
        public bool ChannelMismatch => Volatile.Read(ref _channelMismatch);

        private void Awake()
        {
            var listener = GetComponent<AudioListener>();
            OnListener = listener != null && listener.enabled;
            if (!OnListener)
                GameLog.Warn("Recording", $"ListenerTap on '{name}' has no enabled AudioListener beside it, so it " +
                                          "hears only this GameObject's own sources and clips will miss the game's sound. " +
                                          "Move it to the AudioListener's GameObject (Main Camera).");
        }

        /// <summary>
        /// Arms the tap to write into <paramref name="buffer"/> from DSP time <paramref name="startDspTime"/>.
        /// Main thread only. False when already armed or the arguments cannot hold a clip.
        /// </summary>
        public bool Arm(short[] buffer, int channels, int sampleRate, double startDspTime, long capacityFrames)
        {
            if (buffer == null || channels <= 0 || sampleRate <= 0 || capacityFrames <= 0) return false;
            if (buffer.LongLength < capacityFrames * channels) return false;
            if (Volatile.Read(ref _state) != Idle) return false;

            _buffer = buffer;
            _channels = channels;
            _sampleRate = sampleRate;
            _startFrame = (long)Math.Round(startDspTime * sampleRate);
            _capacityFrames = capacityFrames;
            _writtenFrames = 0;
            _firstBlockFrame = long.MinValue;
            _channelMismatch = false;
            _sawBlock = false;

            // Publish last: a full fence, so the fields above are visible before the audio thread can see Armed.
            Interlocked.Exchange(ref _state, Armed);
            return true;
        }

        /// <summary>
        /// Disarms the tap and returns how many frames from the clip start it wrote. Main thread only. After it
        /// returns the audio thread will not touch the buffer again. Spins at most one block's copy.
        /// </summary>
        public long Disarm(out bool channelMismatch, out long firstBlockOffsetFrames)
        {
            channelMismatch = false;
            firstBlockOffsetFrames = 0;
            if (Volatile.Read(ref _state) == Idle) return 0;

            var spin = new SpinWait();
            while (Interlocked.CompareExchange(ref _state, Idle, Armed) != Armed)
            {
                if (Volatile.Read(ref _state) == Idle) break;   // already idle (cannot happen with one owner; harmless)
                spin.SpinOnce();
            }

            channelMismatch = _channelMismatch;
            firstBlockOffsetFrames = _firstBlockFrame == long.MinValue ? 0 : _firstBlockFrame - _startFrame;
            long written = _writtenFrames;
            _buffer = null;
            return written;
        }

        /// <summary>True once a block arrived while armed (diagnostic for "is the listener's mix reaching us").</summary>
        public bool SawBlockWhileArmed => _sawBlock;

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (Interlocked.CompareExchange(ref _state, Writing, Armed) != Armed) return;
            try
            {
                _sawBlock = true;
                if (channels != _channels)
                {
                    _channelMismatch = true;
                    return;
                }
                if (_channelMismatch) return;

                int frames = data.Length / channels;
                long blockFrame = (long)Math.Round(AudioSettings.dspTime * _sampleRate);
                if (_firstBlockFrame == long.MinValue) _firstBlockFrame = blockFrame;

                long rel = blockFrame - _startFrame;          // where this block's first frame lands in the clip
                int skip = rel < 0 ? (int)Math.Min(frames, -rel) : 0;
                long dst = rel + skip;
                if (dst >= _capacityFrames) return;           // full: the recorder stops the clip at its limit

                // A block that starts beyond what was written (a skipped or late block) leaves a gap: zero it,
                // because the buffer is reused clip after clip and still holds an older clip's sound.
                if (dst > _writtenFrames)
                    Array.Clear(_buffer, (int)(_writtenFrames * channels), (int)((dst - _writtenFrames) * channels));

                int count = (int)Math.Min(frames - skip, _capacityFrames - dst);
                int src = skip * channels;
                int o = (int)(dst * channels);
                int n = count * channels;
                short[] buf = _buffer;
                for (int i = 0; i < n; i++)
                    buf[o + i] = FootageFormat.ToPcm16(data[src + i]);

                long end = dst + count;
                if (end > _writtenFrames) _writtenFrames = end;
            }
            finally
            {
                // Writing → Armed. Disarm only ever CASes Armed → Idle, so this cannot overwrite an Idle.
                Interlocked.Exchange(ref _state, Armed);
            }
        }

        // No OnDisable: a disabled behaviour gets no OnAudioFilterRead, so an armed clip simply stops getting
        // sound, and the recorder pads the rest with silence at End.
    }
}
