using UnityEngine;
using CameraGame.Player;

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

    [Header("Tuning")]
    // Eye height, forward nudge and near clip, in metres (ADR-5). Falls back to the player controller's own
    // config, then to PlayerConfig's defaults, so the camera can never disagree with the body it rides on.
    [SerializeField] private PlayerConfig config;

    private PlayerConfig Cfg => config != null ? config : (_controller != null ? _controller.Config : null);
    private float EyeHeight => Cfg != null ? Cfg.SafeEyeHeight : PlayerConfig.DefaultEyeHeight;
    private float ForwardNudge => Cfg != null ? Cfg.SafeCameraForwardNudge : PlayerConfig.DefaultCameraForwardNudge;
    private float NearClip => Cfg != null ? Cfg.SafeCameraNearClip : PlayerConfig.DefaultCameraNearClip;

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

            // The near clip scales with the body (ADR-5): nudge + near - radius is how far the near plane
            // reaches past the capsule, and it must stay what it was in body proportions.
            var cam = GetComponent<Camera>();
            if (cam != null) cam.nearClipPlane = NearClip;

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
        transform.position = GetHeadPosition() + transform.forward * ForwardNudge;
    }

    // Returns the world position of the character's head.
    // If the head bone was found, use its exact position.
    // Otherwise, fall back to a height offset above the character's feet.
    private Vector3 GetHeadPosition()
    {
        if (_headBone != null)
            return _headBone.position;

        // Fallback (the live path: this model has no skeleton): the eye height above the feet, from config.
        return target.position + Vector3.up * EyeHeight;
    }
}
