#if UNITY_EDITOR
using System;
using UnityEngine;

namespace CameraGame.Recording
{
    /// <summary>Rig-only (Story 1.15, Phase E): reads the counter just AFTER the recorder's LateUpdate and keeps each
    /// frame's delta in a preallocated array.</summary>
    [DefaultExecutionOrder(ClipRecorder.ExecutionOrder + 1)]
    public class RecordClipShootAllocAfter : MonoBehaviour
    {
        private long[] _d = new long[0];
        public int Count { get; private set; }

        public void Arm(int frames) { _d = new long[frames]; Count = 0; }

        private void LateUpdate()
        {
            long now = GC.GetAllocatedBytesForCurrentThread();
            if (Count < _d.Length) _d[Count++] = now - RecordClipShootAllocBefore.Mark;
        }

        /// <summary>Positive control: the bytes the counter reports around one new byte[1000].</summary>
        public static long Control()
        {
            long a = GC.GetAllocatedBytesForCurrentThread();
            var b = new byte[1000];
            long c = GC.GetAllocatedBytesForCurrentThread();
            return b.Length > 0 ? c - a : -1;
        }

        public string Summary()
        {
            long sum = 0, max = 0;
            int nz = 0;
            for (int i = 0; i < Count; i++) { sum += _d[i]; if (_d[i] > max) max = _d[i]; if (_d[i] != 0) nz++; }
            return $"{Count} frames, {nz} with any allocation, total {sum} B, max {max} B/frame";
        }
    }
}
#endif
