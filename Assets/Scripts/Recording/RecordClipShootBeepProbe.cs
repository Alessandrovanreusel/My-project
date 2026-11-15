#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;

namespace CameraGame.Recording
{
    /// <summary>Rig-only: listens to the REC beep's own AudioSource (a filter on that GameObject hears only that
    /// source), stamps the first non-zero block and keeps the samples for a WAV.</summary>
    public class RecordClipShootBeepProbe : MonoBehaviour
    {
        private volatile bool _armed;
        private long _firstTs;
        private readonly float[] _buf = new float[48000 * 2 * 2];
        private int _n;
        private int _channels = 2;
        private float _peak;

        public long FirstTs => Interlocked.Read(ref _firstTs);
        public float Peak => _peak;

        public void Arm() { _n = 0; _peak = 0; Interlocked.Exchange(ref _firstTs, 0); _armed = true; }
        public void Disarm() { _armed = false; }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            if (!_armed) return;
            _channels = channels;
            bool any = false;
            for (int i = 0; i < data.Length; i++) { float a = Math.Abs(data[i]); if (a > 1e-4f) any = true; if (a > _peak) _peak = a; }
            if (any && Interlocked.Read(ref _firstTs) == 0) Interlocked.Exchange(ref _firstTs, Stopwatch.GetTimestamp());
            if (_firstTs != 0)
                for (int i = 0; i < data.Length && _n < _buf.Length; i++) _buf[_n++] = data[i];
        }

        public int CountBursts() => CountBurstsIn(_buf, _n, _channels);

        public static int CountBurstsIn(string wav)
        {
            if (!File.Exists(wav)) return -1;
            byte[] b = File.ReadAllBytes(wav);
            int ch = BitConverter.ToInt16(b, 22);
            int n = (b.Length - 44) / 2;
            var s = new float[n];
            for (int i = 0; i < n; i++) s[i] = BitConverter.ToInt16(b, 44 + i * 2) / 32767f;
            return CountBurstsIn(s, n, ch);
        }

        /// <summary>Tone bursts: runs of 5-ms windows whose RMS is above a tenth of the loudest window.</summary>
        private static int CountBurstsIn(float[] s, int n, int ch)
        {
            int win = 240 * ch; var rms = new List<double>();
            for (int i = 0; i + win <= n; i += win) { double q = 0; for (int j = 0; j < win; j++) q += s[i + j] * s[i + j]; rms.Add(Math.Sqrt(q / win)); }
            double max = 0; foreach (double r in rms) max = Math.Max(max, r);
            int bursts = 0; bool on = false;
            foreach (double r in rms) { bool now = r > max * 0.1; if (now && !on) bursts++; on = now; }
            return bursts;
        }

        public void SaveWav(string path)
        {
            int n = _n, ch = _channels, rate = AudioSettings.outputSampleRate;
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write(Encoding.ASCII.GetBytes("RIFF")); w.Write(36 + n * 2); w.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                w.Write(16); w.Write((short)1); w.Write((short)ch); w.Write(rate); w.Write(rate * ch * 2); w.Write((short)(ch * 2)); w.Write((short)16);
                w.Write(Encoding.ASCII.GetBytes("data")); w.Write(n * 2);
                for (int i = 0; i < n; i++) w.Write(FootageFormat.ToPcm16(_buf[i]));
            }
        }
    }
}
#endif
