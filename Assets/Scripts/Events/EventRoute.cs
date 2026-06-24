using UnityEngine;

namespace CameraGame.Events
{
    /// <summary>
    /// An ordered list of scene waypoints an <see cref="EventActor"/> walks along (Story 1.7). This is the
    /// representation Story 1.6 deliberately left out of <see cref="EventDefinition"/>: a route is made of
    /// world-space scene Transforms, which a project-asset ScriptableObject cannot reference. So the route
    /// lives in the scene as a MonoBehaviour, and the manager passes it to the actor's <c>Begin(route)</c>.
    ///
    /// Fail-soft by design: a null or empty waypoint list is valid — the actor's own NavMesh guards
    /// (<c>NavUsable</c> / <see cref="HasWaypoints"/>) handle it by running the lifecycle in place.
    /// </summary>
    public class EventRoute : MonoBehaviour
    {
        [SerializeField, Tooltip("Ordered waypoints from spawn (pub) to despawn (alley). Place each ON the baked NavMesh.")]
        private Transform[] waypoints;

        /// <summary>True only when there is at least one usable waypoint.</summary>
        public bool HasWaypoints => waypoints != null && waypoints.Length > 0;

        /// <summary>Number of waypoints (0 when none).</summary>
        public int Count => waypoints == null ? 0 : waypoints.Length;

        /// <summary>
        /// World position of waypoint <paramref name="i"/>, with <paramref name="i"/> clamped to
        /// [0, Count-1]. Falls back to this route's own position if there are no waypoints (so callers that
        /// slipped past <see cref="HasWaypoints"/> still get a finite, non-throwing value).
        /// </summary>
        public Vector3 GetWaypoint(int i)
        {
            if (!HasWaypoints) return transform.position;
            int clamped = Mathf.Clamp(i, 0, waypoints.Length - 1);
            Transform wp = waypoints[clamped];
            return wp != null ? wp.position : transform.position;
        }

#if UNITY_EDITOR
        // Scene-view route gizmo (architecture §Debug Tools — "NavMesh route"): spheres at each waypoint and
        // lines between them, so the pub→peak→alley path is visible and tunable. Stripped from release builds.
        private void OnDrawGizmos()
        {
            if (!HasWaypoints) return;

            Gizmos.color = Color.cyan;
            Vector3? prev = null;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null) continue;
                Vector3 p = waypoints[i].position;
                Gizmos.DrawSphere(p, 0.3f);
                if (prev.HasValue) Gizmos.DrawLine(prev.Value, p);
                prev = p;
            }
        }
#endif
    }
}
