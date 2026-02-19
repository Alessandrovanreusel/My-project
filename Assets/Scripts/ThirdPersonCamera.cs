using UnityEngine;

/// <summary>
/// A chase camera that follows behind the player character.
/// It stays at a set distance behind and height above the target,
/// smoothly interpolating so it doesn't feel jerky.
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    // The Transform we want the camera to follow. You can drag the player
    // into this field in the Inspector. If left empty, Start() auto-finds it.
    [SerializeField] private Transform target;

    [Header("Offset")]
    // How far behind the character the camera sits (in units).
    // Bigger = more zoomed out, smaller = closer to the character.
    [SerializeField] private float distance = 8f;

    // How high above the character the camera sits (in units).
    // This creates the "over the shoulder" perspective.
    [SerializeField] private float height = 4f;

    [Header("Smoothing")]
    // How quickly the camera catches up to its desired position.
    // Higher = snappier, lower = more floaty/cinematic.
    [SerializeField] private float positionSmooth = 5f;

    // How quickly the camera rotates to look at the target.
    // Higher = instant tracking, lower = slow lazy turn.
    [SerializeField] private float rotationSmooth = 5f;

    private void Start()
    {
        // If no target was assigned in the Inspector, try to find the player by name.
        // GameObject.Find searches the entire scene for an object with this exact name.
        // It's slow, which is why we only call it once in Start(), not every frame.
        if (target == null)
        {
            GameObject player = GameObject.Find("main characters");
            if (player != null)
                target = player.transform;
        }

        // On the very first frame, snap the camera to the correct position instantly
        // (no smoothing). Without this, the camera would start at (0,0,0) and slowly
        // drift to the player, which looks bad.
        if (target != null)
        {
            // Calculate position: start at the player, go backward by 'distance',
            // then go up by 'height'. target.forward is the direction the player faces.
            Vector3 pos = target.position - target.forward * distance + Vector3.up * height;
            transform.position = pos;

            // Point the camera at a spot slightly above the player's feet.
            // The +4f offset aims at roughly chest/head level instead of the ground.
            transform.LookAt(target.position + Vector3.up * 4f);
        }
    }

    // LateUpdate runs AFTER all Update() calls are done.
    // This is important: the player moves in Update(), and the camera follows in LateUpdate().
    // If both used Update(), the camera might move BEFORE the player, causing jittery visuals.
    private void LateUpdate()
    {
        // If there's no target, do nothing (avoids NullReferenceException).
        if (target == null) return;

        // Calculate where we WANT the camera to be:
        // Behind the player (- forward * distance) and above them (+ up * height).
        Vector3 desiredPosition = target.position
            - target.forward * distance
            + Vector3.up * height;

        // Vector3.Lerp smoothly blends between current position and desired position.
        // The third parameter controls how fast: positionSmooth * Time.deltaTime makes it
        // frame-rate independent. Without Time.deltaTime, the camera would move faster
        // at higher FPS and slower at lower FPS.
        transform.position = Vector3.Lerp(transform.position, desiredPosition, positionSmooth * Time.deltaTime);

        // Calculate the direction from camera to a point above the player (chest level).
        Vector3 lookTarget = target.position + Vector3.up * 4f;

        // Quaternion.LookRotation creates a rotation that "faces" from our position
        // toward the look target. Think of it as "which way should I point to see that spot?"
        Quaternion desiredRotation = Quaternion.LookRotation(lookTarget - transform.position);

        // Quaternion.Slerp (Spherical Linear Interpolation) smoothly rotates from our
        // current rotation toward the desired one. This prevents jarring instant snapping
        // when the player turns quickly.
        transform.rotation = Quaternion.Slerp(transform.rotation, desiredRotation, rotationSmooth * Time.deltaTime);
    }
}
