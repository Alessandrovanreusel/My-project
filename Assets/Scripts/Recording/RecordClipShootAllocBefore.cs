#if UNITY_EDITOR
using System;
using UnityEngine;

namespace CameraGame.Recording
{
    /// <summary>Rig-only (Story 1.15, Phase E): reads the thread's allocated-bytes counter just BEFORE the recorder's
    /// LateUpdate (order 10000).</summary>
    [DefaultExecutionOrder(ClipRecorder.ExecutionOrder - 1)]
    public class RecordClipShootAllocBefore : MonoBehaviour
    {
        public static long Mark;
        private void LateUpdate() => Mark = GC.GetAllocatedBytesForCurrentThread();
    }
}
#endif
