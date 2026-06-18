using UnityEngine;
using CameraGame.Core;
using CameraGame.Grading;

namespace CameraGame.Events
{
    /// <summary>
    /// Cross-system signal raised whenever the player captures a shot (Story 1.5). The payload is the
    /// <see cref="ShotGrade"/> (the score, NOT the image — image persistence is the gallery's job,
    /// Story 1.11). Decouples the capture handler from its listeners: Story 1.12's grade-feedback HUD
    /// (and later the gallery) subscribe here without <c>PhotoModeController</c> ever knowing about them.
    ///
    /// All the raise/subscribe plumbing — handler snapshotting, per-handler exception isolation, and
    /// subscriber clearing on domain reload — comes from the generic <see cref="EventChannel{T}"/> in
    /// Core; this is just the typed, asset-creatable subclass. Listeners subscribe in OnEnable and
    /// unsubscribe in OnDisable (architecture §Communication Patterns).
    /// </summary>
    [CreateAssetMenu(menuName = "CameraGame/Events/Shot Captured Channel", fileName = "ShotCapturedChannel")]
    public class ShotCapturedChannel : EventChannel<ShotGrade> { }
}
