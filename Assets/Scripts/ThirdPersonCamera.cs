using UnityEngine;
using CameraGame.Player;

/// <summary>
/// First-person POV camera that sits at the character's head position
/// and looks where the mouse points. The ThirdPersonController handles
/// the mouse input — this script just positions and rotates the camera.
/// </summary>
// Runs before default-order scripts (Story 2.1 review): its Awake writes the config's near clip, and GalleryView's
// Awake sizes its canvas from this camera's nearClipPlane. See Awake.
[DefaultExecutionOrder(-100)]
public class ThirdPersonCamera : MonoBehaviour
{
    [Header("Target")]
    // The player's Transform. The camera reads the character's Y rotation (yaw)
    // from this to know which direction to face horizontally.
    [SerializeField] private Transform target;

    [Header("Tuning")]
    // Eye height, forward nudge and near clip, in metres (ADR-5). The BODY's config wins: the controller this camera
    // rides on is the one that validates its config against the CharacterController, so reading that same asset
    // means the camera cannot disagree with the body. This field is only the fallback for a target with no
    // ThirdPersonController (or one with no config); after that, PlayerConfig's defaults.
    [SerializeField] private PlayerConfig config;

    private PlayerConfig Cfg => _controller != null && _controller.Config != null ? _controller.Config : config;
    private float EyeHeight => Cfg != null ? Cfg.SafeEyeHeight : PlayerConfig.DefaultEyeHeight;
    private float ForwardNudge => Cfg != null ? Cfg.SafeCameraForwardNudge : PlayerConfig.DefaultCameraForwardNudge;
    private float NearClip => Cfg != null ? Cfg.SafeCameraNearClip : PlayerConfig.DefaultCameraNearClip;
    private float WallMargin => Cfg != null ? Cfg.SafeCameraWallMargin : PlayerConfig.DefaultCameraWallMargin;

    // Story 2.2 (AC4): the cast that keeps the near plane out of walls. Pre-allocated (no garbage per frame); every layer
    // but the invisible town barriers (WorldBounds), which are not walls and show nothing behind them.
    private readonly RaycastHit[] _wallHits = new RaycastHit[8];
    private int _wallMask = ~0;
    private Camera _cam;

    // --- Private references found at runtime ---

    // The Head bone inside the character's skeleton.
    // We position the camera exactly here so you see from the character's eyes.
    private Transform _headBone;

    // Reference to the controller script to read CameraPitch (vertical look angle).
    // The controller handles mouse input and calculates the pitch — we just read it.
    private ThirdPersonController _controller;

    // Set once the target, head bone and controller have been looked up (Awake or Start, whichever runs first).
    private bool _resolved;

    // ⚠️ THE NEAR CLIP IS APPLIED IN AWAKE, NOT ONLY IN START (Story 2.1 review, reproduced). GalleryView.Awake
    // places its Screen Space - Camera canvas at this camera's nearClipPlane + 0.1. When the config's near clip was
    // written only in Start, the gallery had already been sized from the scene's serialized value, so a
    // cameraNearClip more than 0.1 m above it (0.3, the pre-ADR-5 value, did it) put the canvas in front of the near
    // plane and the gallery opened INVISIBLY while still freezing the player. Awake plus DefaultExecutionOrder(-100)
    // applies the config before any default-order script's Awake reads the camera.
    private void Awake()
    {
        // Unity calls Awake even on a DISABLED component. A rig that switched this camera off owns the Camera, and
        // used to be left alone because Start never ran — keep it that way.
        if (!enabled) return;

        ResolveTarget();
        ApplyNearClip();
        _cam = GetComponent<Camera>();
        int bounds = LayerMask.NameToLayer("WorldBounds");
        if (bounds >= 0) _wallMask = ~(1 << bounds);
        int ignore = LayerMask.NameToLayer("Ignore Raycast");
        if (ignore >= 0) _wallMask &= ~(1 << ignore);
    }

    private void Start()
    {
        ResolveTarget();   // no-op if Awake already did it; does the work if this was enabled only after Awake
        if (target == null) return;

        ApplyNearClip();

        // Snap camera to head position on the first frame (no smoothing needed).
        transform.position = GetHeadPosition();
        transform.rotation = Quaternion.Euler(0f, target.eulerAngles.y, 0f);
    }

    private void ResolveTarget()
    {
        if (_resolved) return;
        _resolved = true;

        // Auto-find the player if not assigned in the Inspector.
        if (target == null)
        {
            GameObject player = GameObject.Find("main characters");
            if (player != null)
                target = player.transform;
        }

        if (target == null) return;

        // Find the Head bone deep inside the skeleton hierarchy.
        // Transform.Find uses a path relative to the parent, with "/" separating children.
        // This is the bone path we mapped from the Blender-exported skeleton.
        _headBone = target.Find("Armature/Hips/Spine/Chest/Neck/Head/Head_end");

        if (_headBone == null)
            Debug.LogWarning("POV Camera: Head bone not found! Falling back to target position + height offset.");

        // Get the controller script so we can read the pitch (up/down look angle).
        _controller = target.GetComponent<ThirdPersonController>();
    }

    // The near clip scales with the body (ADR-5): nudge + near - radius is how far the near plane reaches past the
    // capsule. Only with a target: without one this camera drives nothing, so the Camera keeps its own value.
    private void ApplyNearClip()
    {
        if (target == null) return;
        var cam = GetComponent<Camera>();
        if (cam != null) cam.nearClipPlane = NearClip;
    }

    // LateUpdate runs AFTER all Update() calls.
    // The character moves and rotates in Update(), then the camera follows in LateUpdate().
    // This order prevents the camera from lagging one frame behind the character.
    private void LateUpdate()
    {
        if (target == null) return;

        // ROTATION first: combine yaw (character body facing) with pitch (up/down look).
        float pitch = _controller != null ? _controller.CameraPitch : 0f;
        float yaw = target.eulerAngles.y;
        transform.rotation = Quaternion.Euler(pitch, yaw, 0f);

        // POSITION: at the head, then nudged slightly forward so we see past the character mesh.
        // ⚠️ Story 2.2 (AC4, the "see-through walls" report): the nudge follows the BODY's facing (yaw only), not the
        // pitch — it used to follow the pitch, so looking up 80° lifted the eye to ~1.96 m and the near plane to ~2.05 m
        // on a 1.77 m body. And it is cut short in front of a wall: pressed against one, a full 0.316 m nudge plus the
        // 0.095 m near clip put the near plane ~0.08 m INTO the wall, which then showed what was behind it. In the open
        // nothing is hit, so the pose is exactly the old one at pitch 0: 1.65 m up and 0.316 m forward of the feet.
        Vector3 head = GetHeadPosition();
        Vector3 forward = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        transform.position = head + forward * ClearNudge(head, forward);
    }

    /// <summary>How far forward of the body axis the camera can sit without its near plane reaching a surface: the
    /// configured nudge, cut short where a sphere — the near plane's reach (from the live FOV, aspect and near clip) plus
    /// the wall margin — cast from the axis along the facing meets something. The player's own capsule is never hit: a
    /// cast ignores colliders it starts inside, and the camera's own body is skipped by name of its root.</summary>
    private float ClearNudge(Vector3 head, Vector3 forward)
    {
        float nudge = ForwardNudge;
        if (_cam == null) _cam = GetComponent<Camera>();   // enabled after Awake (rigs do that): set up here instead
        if (nudge <= 0f || _cam == null) return nudge;
        float radius = PlayerConfig.NearPlaneReach(_cam.fieldOfView, _cam.aspect, _cam.nearClipPlane) + WallMargin;
        if (radius <= 0f) return nudge;
        int n = Physics.SphereCastNonAlloc(head, radius, forward, _wallHits, nudge, _wallMask, QueryTriggerInteraction.Ignore);
        float clear = nudge;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = _wallHits[i];
            if (h.collider == null || h.collider.transform.IsChildOf(target)) continue;
            if (h.distance <= 0f && h.point == Vector3.zero) continue;   // overlapping at the start: not in front of us
            clear = Mathf.Min(clear, h.distance);
        }
        return Mathf.Max(0f, clear);
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
