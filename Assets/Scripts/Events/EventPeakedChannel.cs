using UnityEngine;
using CameraGame.Core;

namespace CameraGame.Events
{
    /// <summary>
    /// Cross-system signal raised once when an event-actor reaches its <see cref="EventPhase.Peak"/>.
    /// The payload is the <see cref="ISubject"/> that peaked, so listeners read it through the decoupling
    /// seam without referencing any concrete event type. Story 1.7's drunk and future audio subscribe
    /// here; Story 1.6 just provides the seam (the actor's reference is optional/fail-soft).
    ///
    /// All raise/subscribe plumbing — handler snapshotting, per-handler exception isolation, and
    /// subscriber clearing on domain reload — comes from the generic <see cref="EventChannel{T}"/> in
    /// Core; this is just the typed, asset-creatable subclass (same pattern as ShotCapturedChannel, 1.5).
    /// </summary>
    [CreateAssetMenu(menuName = "CameraGame/Events/Event Peaked Channel", fileName = "EventPeakedChannel")]
    public class EventPeakedChannel : EventChannel<ISubject> { }
}
