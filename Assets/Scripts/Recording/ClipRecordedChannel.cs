using UnityEngine;
using CameraGame.Core;

namespace CameraGame.Recording
{
    /// <summary>
    /// Raised once per finished clip, synchronously, by <see cref="ClipRecorder"/> (AR4, Story 1.15 AC4). The
    /// memory card (1.17), the clip grade HUD (1.16) and later reputation listen here, and the recorder never
    /// knows who they are.
    ///
    /// A listener that keeps the footage must <see cref="RecordedClip.Claim"/> it DURING the raise; see
    /// <see cref="RecordedClip"/> for the ownership rule. Listeners subscribe in <c>OnEnable</c> and
    /// unsubscribe in <c>OnDisable</c>; <see cref="EventChannel{T}"/> isolates a throwing listener.
    /// </summary>
    [CreateAssetMenu(menuName = "CameraGame/Channels/Clip Recorded Channel", fileName = "ClipRecordedChannel")]
    public class ClipRecordedChannel : EventChannel<RecordedClip>
    {
    }
}
