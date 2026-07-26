using UnityEngine;

namespace CameraGame.Events
{
    /// <summary>
    /// The decoupling seam between events and grading. A subject is anything the camera can photograph
    /// and score — the grader (Stories 1.9–1.10) reads subjects ONLY through this interface, never
    /// through a concrete event type (architecture §Architectural Boundaries). Keep this dependency-free.
    ///
    /// LIVENESS CONTRACT (decided in the Story 1.6 review): subjects are POOLED and reused, so a reference
    /// does not become null when the subject despawns. Consumers must read a subject LIVE — at the moment
    /// the EventPeaked channel delivers it (e.g. on capture) — and must NOT cache the reference across a
    /// despawn, or they will read a recycled instance's stale Bounds/state.
    /// </summary>
    public interface ISubject
    {
        /// <summary>World-space bounds covering the whole subject (all child renderers), for framing/grading.</summary>
        Bounds Bounds { get; }

        /// <summary>True while the subject is at its photogenic peak moment.</summary>
        bool IsAtPeak { get; }

        /// <summary>Seconds until the peak; counts down continuously and goes negative after the peak,
        /// so timing graders can use Mathf.Abs(TimeToPeak) for a symmetric ± window.</summary>
        float TimeToPeak { get; }

        /// <summary>
        /// Seconds away from the peak WINDOW: positive before it, exactly 0 anywhere inside it, negative
        /// after it. This — not <see cref="TimeToPeak"/> — is what a timing grader should take Mathf.Abs of.
        ///
        /// ⚠️ WHY BOTH EXIST. The peak is an INTERVAL, not an instant (the Town Drunk's is 1.5 s), and
        /// <see cref="TimeToPeak"/> is 0 at that interval's START and −1.5 at its END. Taking
        /// <c>Mathf.Abs(TimeToPeak)</c> therefore scores the LAST frame of the money shot — the drunk
        /// mid-stagger, the exact photograph the whole event exists to produce — as though it were 1.5 s
        /// early. The GDD's "full marks within ±0.5 s of the peak" was written for a point-like peak; this
        /// property is what makes that sentence mean what it says: half a second either side of the money
        /// shot, with every frame of the money shot itself at full marks.
        ///
        /// <see cref="TimeToPeak"/> keeps its original contract untouched (Story 1.6's review decided it,
        /// and telegraphing/anticipation reads it) — this is an addition alongside, not a redefinition.
        /// </summary>
        float PeakOffset { get; }

        /// <summary>Stable identifier for this kind of subject (e.g. "TownDrunk"), used by grading.</summary>
        string SubjectId { get; }
    }
}
