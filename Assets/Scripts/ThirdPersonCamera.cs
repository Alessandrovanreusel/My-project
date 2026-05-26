using UnityEngine;

/// <summary>
/// First-person POV camera that sits at the character's head position
/// and looks where the mouse points. The ThirdPersonController handles
/// the mouse input — this script just positions and rotates the camera.
/// </summary>
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    // The player's Transform. The camera reads the character's Y rotation (yaw)
    // from this to know which direction to face horizontally.
    [SerializeField] private Transform target;

    // --- Private references found at runtime ---

    // The Head bone inside the character's skeleton.
    // We position the camera exactly here so you see from the character's eyes.
    private Transform _headBone;

    // Reference to the controller script to read CameraPitch (vertical look angle).
    // The controller handles mouse input and calculates the pitch — we just read it.
    private ThirdPersonController _controller;

    private void Start()
    {
        // Auto-find the player if not assigned in the Inspector.
        if (target == null)
        {
            GameObject player = GameObject.Find("main characters");
            if (player != null)
                target = player.transform;
        }

        if (target != null)
        {
            // Find the Head bone deep inside the skeleton hierarchy.
            // Transform.Find uses a path relative to the parent, with "/" separating children.
            // This is the bone path we mapped from the Blender-exported skeleton.
            _headBone = target.Find("Armature/Hips/Spine/Chest/Neck/Head/Head_end");

            if (_headBone == null)
                Debug.LogWarning("POV Camera: Head bone not found! Falling back to target position + height offset.");

            // Get the controller script so we can read the pitch (up/down look angle).
            _controller = target.GetComponent<ThirdPersonController>();

            // Snap camera to head position on the first frame (no smoothing needed).
            transform.position = GetHeadPosition();
            transform.rotation = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
        }
    }

    // LateUpdate runs AFTER all Update() calls.
    // The character moves and rotates in Update(), then the camera follows in LateUpdate().
    // This order prevents the camera from lagging one frame behind the character.
    private void LateUpdate()
    {
        if (target == null) return;

        // ROTATION first: combine yaw (character body facing) with pitch (up/down look).
        float pitch = _controller != null ? _controller.CameraPitch : 0f;
        transform.rotation = Quaternion.Euler(pitch, target.eulerAngles.y, 0f);

        // POSITION: at the head, then nudged slightly forward so we see past the character mesh.
        // Without this offset, you'd see the inside of the character's head model.
        transform.position = GetHeadPosition() + transform.forward * 1f;
    }

    // Returns the world position of the character's head.
    // If the head bone was found, use its exact position.
    // Otherwise, fall back to a height offset above the character's feet.
    private Vector3 GetHeadPosition()
    {
        if (_headBone != null)
            return _headBone.position;

        // Fallback: CharacterController height is ~8.86, so head is near Y=8.5 above feet.
        return target.position + Vector3.up * 8.5f;
    }
}
