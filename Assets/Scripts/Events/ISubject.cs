using UnityEngine;

namespace CameraGame.Events
{
    /// <summary>
    /// The decoupling seam between events and grading. A subject is anything the camera can photograph
    /// and score — the grader (Stories 1.9–1.10) reads subjects ONLY through this interface, never
    /// through a concrete event type (architecture §Architectural Boundaries). Keep this dependency-free.
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

        /// <summary>Stable identifier for this kind of subject (e.g. "TownDrunk"), used by grading.</summary>
        string SubjectId { get; }
    }
}
