// THROWAWAY SPIKE CODE (Story 1.14). Never referenced by game code. See README.md in this folder.
using System;
using System.IO;
using System.Threading;
using UnityEngine;

namespace CameraGame.Spikes.RecordingSpike
{
    /// <summary>
    /// The live game-audio tap: OnAudioFilterRead on the AudioListener's GameObject sees the final mix the
    /// player hears. Runs on the audio thread, so it touches no Unity API and allocates nothing; it copies
    /// into a preallocated buffer under a short lock. Shared by all three candidates (Tasks 3.2, 4.3, 5.1).
    /// </summary>
    public sealed class AudioTap : MonoBehaviour
    {
        private float[] _buf = new float[0];
        private int _len;                  // floats written into _buf
        private volatile bool _capturing;
        private readonly object _lock = new object();
        private long _callbacks;
        private long _totalFrames;         // sample frames seen since the tap was attached
        public int Channels { get; private set; } = 2;
        public int SampleRate { get; private set; }
        public long Callbacks => Interlocked.Read(ref _callbacks);
        public long TotalFrames => Interlocked.Read(ref _totalFrames);
        public int BufferedFloats { get { lock (_lock) return _len; } }
        public bool Overflowed { get; private set; }
        /// <summary>Peak absolute sample seen in the current capture (cheap non-silence check).</summary>
        public float Peak { get; private set; }

        public void Allocate(float seconds)
        {
            SampleRate = AudioSettings.outputSampleRate;
            int ch = AudioSettings.speakerMode == AudioSpeakerMode.Mono ? 1 : 2;
            if (AudioSettings.speakerMode != AudioSpeakerMode.Mono && AudioSettings.speakerMode != AudioSpeakerMode.Stereo)
                ch = 8; // generous; downmixed when written
            lock (_lock) { _buf = new float[(int)(seconds * SampleRate * ch)]; _len = 0; }
        }

        public void Begin() { lock (_lock) { _len = 0; Overflowed = false; Peak = 0; } _capturing = true; }
        public void End() { _capturing = false; }

        /// <summary>Copy out the captured interleaved samples (main thread, after End).</summary>
        public float[] Snapshot()
        {
            lock (_lock) { var r = new float[_len]; Array.Copy(_buf, r, _len); return r; }
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            Channels = channels;
            Interlocked.Increment(ref _callbacks);
            Interlocked.Add(ref _totalFrames, data.Length / channels);
            if (!_capturing) return;
            lock (_lock)
            {
                int n = Math.Min(data.Length, _buf.Length - _len);
                if (n < data.Length) Overflowed = true;
                Array.Copy(data, 0, _buf, _len, n);
                float pk = Peak;
                for (int i = 0; i < n; i++) { float a = data[i] < 0 ? -data[i] : data[i]; if (a > pk) pk = a; }
                Peak = pk;
                _len += n;
            }
        }

        /// <summary>16-bit PCM WAV, downmixed to stereo when the device is surround.</summary>
        public static void WriteWav(string path, float[] samples, int channels, int rate)
        {
            int outCh = Math.Min(channels, 2);
            int frames = samples.Length / Math.Max(1, channels);
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var w = new BinaryWriter(fs))
            {
                int dataBytes = frames * outCh * 2;
                w.Write(new[] { 'R', 'I', 'F', 'F' }); w.Write(36 + dataBytes);
                w.Write(new[] { 'W', 'A', 'V', 'E', 'f', 'm', 't', ' ' }); w.Write(16);
                w.Write((short)1); w.Write((short)outCh); w.Write(rate); w.Write(rate * outCh * 2);
                w.Write((short)(outCh * 2)); w.Write((short)16);
                w.Write(new[] { 'd', 'a', 't', 'a' }); w.Write(dataBytes);
                for (int f = 0; f < frames; f++)
                    for (int c = 0; c < outCh; c++)
                    {
                        float v = samples[f * channels + c];
                        if (channels > 2 && c < 2) v = (v + samples[f * channels + 2] * 0.707f) ; // centre into L/R
                        w.Write((short)Mathf.Clamp(Mathf.RoundToInt(v * 32767f), -32768, 32767));
                    }
            }
        }
    }
}
