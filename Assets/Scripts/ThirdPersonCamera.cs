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
    private readonly Vector3[] _nearPoints = new Vector3[9];   // the near plane at a candidate pose (NearPlaneClear)
    private bool _wedged;                                       // this frame, not even a nudge of 0 keeps the near plane clear

    // How far the eye may drop when the body is wedged into an overhang (Duck), in metres.
    private const float MaxDuck = 0.3f;
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
        float nudge = ClearNudge(head, forward);
        transform.position = _wedged ? Duck(head) : head + forward * nudge;
    }

    /// <summary>How far forward of the body axis the camera can sit without its near plane reaching a surface: the
    /// configured nudge, cut short where a sphere — the near plane's reach (from the live FOV, aspect and near clip) plus
    /// the wall margin — cast from the axis along the facing meets something. The player's own capsule is never hit: a
    /// cast ignores colliders it starts inside, and the camera's own body is skipped by name of its root. A surface the
    /// cast STARTS inside is tested on the near plane itself (NearPlaneClearNudge), never skipped.</summary>
    private float ClearNudge(Vector3 head, Vector3 forward)
    {
        float nudge = ForwardNudge;
        _wedged = false;
        if (_cam == null) _cam = GetComponent<Camera>();   // enabled after Awake (rigs do that): set up here instead
        if (nudge <= 0f || _cam == null) return nudge;
        float radius = PlayerConfig.NearPlaneReach(_cam.fieldOfView, _cam.aspect, _cam.nearClipPlane) + WallMargin;
        if (radius <= 0f) return nudge;
        int n = Physics.SphereCastNonAlloc(head, radius, forward, _wallHits, nudge, _wallMask, QueryTriggerInteraction.Ignore);
        float clear = nudge;
        bool startsInside = false;
        for (int i = 0; i < n; i++)
        {
            RaycastHit h = _wallHits[i];
            if (h.collider == null || h.collider.transform.IsChildOf(target)) continue;
            // Overlapping at the start: the cast cannot say how far ahead it is, so the near plane is tested directly below.
            if (h.distance <= 0f && h.point == Vector3.zero) { startsInside = true; continue; }
            clear = Mathf.Min(clear, h.distance);
        }
        clear = Mathf.Max(0f, clear);
        return startsInside ? NearPlaneClearNudge(head, forward, clear) : clear;
    }

    /// <summary>
    /// ⚠️ Story 2.2 (cross-review finding 3, reproduced in a rig). A surface already inside the cast's start sphere at the eye
    /// used to be skipped. An underside sloping DOWN 10–30° in front of the head (a boulder's overhang) stops the capsule's top
    /// right there, so the full nudge put the near plane inside it, and at 30° the camera itself: looking up showed what was
    /// behind it, the whole frame. Here the near plane is tested where it would be: lines from the eye (inside the body, so in
    /// free space) to its centre, corners and edge midpoints, each stretched by the wall margin. The longest clear nudge up to
    /// <paramref name="upTo"/> wins, found in a few halvings. At 0 the near plane lies inside the capsule's skin, where the
    /// controller lets no surface be. Runs only while something is that close to the head; no allocation.
    /// </summary>
    private float NearPlaneClearNudge(Vector3 head, Vector3 forward, float upTo)
    {
        if (NearPlaneClear(head, head + forward * upTo)) return upTo;
        float lo = 0f, hi = upTo;
        for (int i = 0; i < 6; i++)
        {
            float mid = 0.5f * (lo + hi);
            if (NearPlaneClear(head, head + forward * mid)) lo = mid;
            else hi = mid;
        }
        if (lo <= 0f && !NearPlaneClear(head, head)) _wedged = true;
        return lo;
    }

    /// <summary>
    /// ⚠️ Story 2.2 (cross-review, reproduced at the boulder rock.023 on the south hill). Walking up a slope into an overhang
    /// that slopes DOWN wedges the body: the CharacterController ended with its top 0.106 m INSIDE the rock, the rock 2–3 cm
    /// from the eye, so no forward nudge could keep the near plane out and the view showed the hillside behind the rock. The
    /// clear view is lower: the eye drops in 5 cm steps, up to <see cref="MaxDuck"/>, and the first pose whose near plane the
    /// lines from the eye find clear wins. If none is, the camera stays at the eye: it is never moved somewhere untested.
    /// </summary>
    private Vector3 Duck(Vector3 head)
    {
        for (int step = 1; step * 0.05f <= MaxDuck + 1e-4f; step++)
        {
            Vector3 p = head + Vector3.down * (step * 0.05f);
            if (NearPlaneClear(head, p)) return p;
        }
        return head;
    }

    private bool NearPlaneClear(Vector3 eye, Vector3 cameraPosition)
    {
        PlayerConfig.NearPlanePoints(cameraPosition, transform.rotation, _cam.fieldOfView, _cam.aspect, _cam.nearClipPlane, _nearPoints);
        float margin = WallMargin;
        for (int p = 0; p < _nearPoints.Length; p++)
        {
            Vector3 d = _nearPoints[p] - eye;
            float len = d.magnitude;
            if (len < 1e-5f) continue;
            int n = Physics.RaycastNonAlloc(eye, d / len, _wallHits, len + margin, _wallMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < n; i++)
                if (_wallHits[i].collider != null && !_wallHits[i].collider.transform.IsChildOf(target)) return false;
        }
        return true;
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
