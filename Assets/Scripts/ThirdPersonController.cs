using UnityEngine;
using UnityEngine.InputSystem;
using CameraGame.Core;
using CameraGame.Player;

// RequireComponent automatically adds these components when you add this script.
// It also prevents you from accidentally removing them in the Inspector.
// Our movement NEEDS a CharacterController (for physics) and PlayerInput (for controls).
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInput))]
public class ThirdPersonController : MonoBehaviour
{
    [Header("Tuning")]
    // Walk and sprint speed, step height, jump height, gravity and the ground-stick speed all live in ONE
    // PlayerConfig asset (Assets/Data/Player/PlayerConfig.asset), in metres and seconds for a 1.77 m person
    // (ADR-5, Story 2.1). ThirdPersonCamera and the verification rigs read the same asset, so a value is
    // tuned in one place. If it is missing, the game warns once and keeps running on PlayerConfig's defaults.
    [SerializeField] private PlayerConfig config;

    /// <summary>The tuning asset this controller runs on (null = the built-in defaults).</summary>
    public PlayerConfig Config => config;

    [Header("Mouse Look")]
    // How fast the camera turns when you move the mouse.
    // Higher = faster turning, lower = slower. Adjust to taste.
    [SerializeField] private float mouseSensitivity = 0.15f;

    // Read through the config's Safe* accessors every frame (cheap), so tuning the asset during play takes
    // effect at once. Each falls back to the ADR-5 default when there is no config.
    private float WalkSpeed => config != null ? config.SafeWalkSpeed : PlayerConfig.DefaultWalkSpeed;
    private float SprintSpeed => config != null ? config.SafeSprintSpeed : PlayerConfig.DefaultSprintSpeed;
    private float StepOffset => config != null ? config.SafeStepOffset : PlayerConfig.DefaultStepOffset;
    private float JumpHeight => config != null ? config.SafeJumpHeight : PlayerConfig.DefaultJumpHeight;
    private float Gravity => config != null ? config.SafeGravity : PlayerConfig.DefaultGravity;
    private float GroundStickVelocity => config != null ? config.SafeGroundStickVelocity : PlayerConfig.DefaultGroundStickVelocity;
    private float EyeHeight => config != null ? config.SafeEyeHeight : PlayerConfig.DefaultEyeHeight;

    [Header("Camera Reference")]
    // Reference to the camera's Transform. We need this to make movement camera-relative:
    // when you press "W", the character moves in the direction the CAMERA is facing,
    // not the direction the character is facing. This feels natural in 3rd person games.
    [SerializeField] private Transform cameraTransform;

    // --- Private state variables (not visible in Inspector) ---

    // The CharacterController handles collision detection and movement physics.
    // Unlike a Rigidbody, it doesn't use Unity's physics engine — we control it manually.
    private CharacterController _controller;

    // Raw input from WASD/joystick. X = left/right, Y = forward/backward.
    // Updated by the Input System via OnMove() callback.
    private Vector2 _moveInput;

    // Raw mouse delta from the Look action. X = horizontal, Y = vertical.
    private Vector2 _lookInput;

    // Is the player currently holding the sprint button?
    private bool _isSprinting;

    // Current vertical speed (for gravity and jumping). Negative = falling, positive = rising.
    private float _verticalVelocity;

    // Tracks the camera's vertical angle (looking up/down).
    // Positive = looking down, negative = looking up. Clamped to prevent flipping.
    private float _pitch;

    // Public property so the camera script can read our pitch value.
    // This is how two scripts on different GameObjects communicate:
    // the camera reads this every frame in LateUpdate to know how far up/down to tilt.
    public float CameraPitch => _pitch;

    // Set to true when the player presses Jump, consumed when the jump actually happens.
    // This "request" pattern prevents double-jumps and ensures the jump only fires once.
    private bool _jumpRequested;

    private void Awake()
    {
        // Cache the CharacterController reference. GetComponent is expensive,
        // so we do it once in Awake rather than every frame.
        _controller = GetComponent<CharacterController>();

        // Fail soft (NFR8): say ONCE what is wrong, then run on safe values. Never throw.
        if (config == null)
            GameLog.Warn("Player", "ThirdPersonController has no PlayerConfig assigned — running on the built-in ADR-5 defaults.");
        else if (config.TryGetConfigProblem(out string configProblem))
            GameLog.Warn("Player", $"PlayerConfig '{config.name}': {configProblem}.");
        if (PlayerConfig.TryGetBodyProblem(EyeHeight, StepOffset, _controller.height, _controller.radius, out string bodyProblem))
            GameLog.Warn("Player", $"{bodyProblem}.");

        // Override the CharacterController's Inspector stepOffset with the config's value.
        // This makes the config the source of truth for movement tuning.
        _controller.stepOffset = StepOffset;

        // If no camera was assigned in the Inspector, automatically find the Main Camera.
        // Camera.main returns the camera tagged "MainCamera" in the scene.
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;

        // Lock the cursor to the center of the screen and hide it.
        // This is standard for POV/FPS games — you don't want the mouse cursor
        // floating around the screen. The mouse delta still works for looking around.
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    // Update runs once per frame. We process physics and movement here.
    // The order matters: look first (so rotation is current), gravity, jump, then move.
    private void Update()
    {
        HandleMouseLook(); // Rotate character based on mouse input
        ApplyGravity();    // Pull the character down each frame
        HandleJump();      // Check if we should launch upward
        Move();            // Move horizontally + apply vertical velocity
    }

    // Applies mouse input to rotate the character (yaw) and track pitch (up/down).
    // Yaw = horizontal mouse movement = rotating the character body left/right.
    // Pitch = vertical mouse movement = tilting the camera up/down (stored for the camera to read).
    private void HandleMouseLook()
    {
        // Rotate the character body left/right based on horizontal mouse movement.
        // transform.Rotate spins the object around an axis. We only rotate around Y (up).
        float yaw = _lookInput.x * mouseSensitivity;
        transform.Rotate(0f, yaw, 0f);

        // Track pitch (vertical look angle) but DON'T rotate the character body up/down.
        // The camera script reads CameraPitch and applies it to the camera only.
        // We subtract because moving mouse UP (positive Y) should look UP (negative pitch).
        // Clamp to ±80° so you can't flip the camera upside down.
        _pitch = Mathf.Clamp(_pitch - _lookInput.y * mouseSensitivity, -80f, 80f);
    }

    // =====================================================================
    // INPUT SYSTEM CALLBACKS
    // =====================================================================
    // These methods are called automatically by the PlayerInput component
    // because it's set to "Send Messages" behavior. When the player presses
    // the "Move" action, Unity calls OnMove(). Same for OnJump, OnSprint.
    // The method name must be "On" + the action name from the Input Actions asset.
    // =====================================================================

    // Called every frame while WASD/joystick is used. Receives a Vector2:
    // (0,1) = forward, (0,-1) = backward, (1,0) = right, (-1,0) = left.
    public void OnMove(InputValue value)
    {
        if (InputSuppressed) return;
        _moveInput = value.Get<Vector2>();
    }

    // Called when the Jump button is pressed (Space by default).
    // We only allow jumping if the character is on the ground.
    public void OnJump(InputValue value)
    {
        if (InputSuppressed) return;

        if (_controller.isGrounded)
            _jumpRequested = true;
    }

    // Called when Sprint is pressed or released (Shift by default).
    // value.isPressed is true while held, false when released.
    public void OnSprint(InputValue value)
    {
        if (InputSuppressed) return;
        _isSprinting = value.isPressed;
    }

    // Called every frame while the mouse moves (or right stick on gamepad).
    // The "Look" action is already defined in InputSystem_Actions with <Pointer>/delta binding.
    // Delta means "how many pixels the mouse moved this frame", not the absolute position.
    public void OnLook(InputValue value)
    {
        if (InputSuppressed) return;
        _lookInput = value.Get<Vector2>();
    }

    /// <summary>True while something else owns the screen and the player should not be driving.</summary>
    public bool InputSuppressed { get; private set; }

    /// <summary>
    /// Stops (or allows) the player walking and looking. Story 1.11's gallery calls this while it owns the
    /// screen: its backdrop is fully opaque, so without this the player walks blind — and the 2026-07-30
    /// code review found they could stroll off a ledge behind their own photographs.
    ///
    /// ⚠️ THE HELD INPUT IS CLEARED ON THE WAY IN, and that is the whole point rather than tidiness. Under
    /// PlayerInput's Send Messages, a key held down when suppression starts sends NO further message until
    /// it changes — so a player holding W as the gallery opens would keep the last non-zero _moveInput and
    /// walk for as long as it stayed open. Zeroing here is what makes "suppressed" actually mean stopped.
    /// This project has fixed the sticky-held-input class of bug twice already.
    ///
    /// ⚠️ DELIBERATELY KNOWS NOTHING ABOUT THE GALLERY, exactly like PhotoModeController.SetRaiseSuppressed.
    /// It is a general capability the caller supplies meaning to, so a pause menu or a map screen can use
    /// the same switch without this class learning about any of them.
    /// </summary>
    public void SetInputSuppressed(bool suppressed)
    {
        InputSuppressed = suppressed;

        if (!suppressed) return;

        _moveInput = Vector2.zero;
        _lookInput = Vector2.zero;
        _isSprinting = false;
        _jumpRequested = false;
    }

    // =====================================================================
    // MOVEMENT LOGIC
    // =====================================================================

    private void ApplyGravity()
    {
        // When grounded and already falling, reset to a small negative value (the config's
        // groundStickVelocity, -2 m/s) to keep the CharacterController "pressed" against
        // the ground. If we used 0, isGrounded might flicker on slopes.
        if (_controller.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = GroundStickVelocity;
        else
            // When in the air, accelerate downward. This is basic physics:
            // velocity += acceleration * time. Gravity is negative, so we fall faster each frame.
            _verticalVelocity += Gravity * Time.deltaTime;
    }

    private void HandleJump()
    {
        // Only jump if requested AND currently on the ground.
        // Double-check isGrounded to prevent edge cases.
        if (_jumpRequested && _controller.isGrounded)
        {
            // Physics formula: v = sqrt(2 * height * gravity)
            // We multiply gravity by -2 because gravity is negative, and we need
            // a positive upward velocity. This gives us exactly the right speed
            // to reach 'jumpHeight' meters before falling back down.
            _verticalVelocity = Mathf.Sqrt(JumpHeight * -2f * Gravity);

            // Consume the request so we don't jump again next frame.
            _jumpRequested = false;
        }
    }

#if UNITY_EDITOR
    // Editor-only structural check (Story 2.1, 1g): the config's eye and step against this body.
    private void OnValidate()
    {
        var cc = GetComponent<CharacterController>();
        if (cc != null && PlayerConfig.TryGetBodyProblem(EyeHeight, StepOffset, cc.height, cc.radius, out string problem))
            Debug.LogWarning($"[Player] {name}: {problem}.", this);
    }
#endif

    private void Move()
    {
        // Pick speed based on whether the player is sprinting.
        float speed = _isSprinting ? SprintSpeed : WalkSpeed;

        // Convert 2D input (WASD) into a 3D direction vector.
        // Input X becomes world X (left/right), Input Y becomes world Z (forward/backward).
        // .normalized ensures diagonal movement isn't faster than straight movement.
        Vector3 inputDirection = new Vector3(_moveInput.x, 0f, _moveInput.y).normalized;

        // Start with no horizontal movement. If there's input, we'll calculate it below.
        Vector3 moveDirection = Vector3.zero;

        // Only move if the joystick/WASD is pushed beyond a small deadzone (0.1).
        // This prevents tiny drift from analog sticks causing unwanted movement.
        if (inputDirection.magnitude >= 0.1f)
        {
            // POV MOVEMENT — the character always faces where the mouse points.
            // We use the CHARACTER's own forward/right directions (not camera).
            // In POV mode, the camera faces the same direction as the character,
            // so transform.forward IS the camera's forward direction.
            //   W → transform.forward (where you're looking)
            //   S → -transform.forward (backward)
            //   A → -transform.right (left)
            //   D → transform.right (right)
            moveDirection = transform.forward * inputDirection.z
                          + transform.right * inputDirection.x;
        }

        // Combine horizontal movement with vertical velocity (gravity/jumping).
        Vector3 finalMove = moveDirection.normalized * speed + Vector3.up * _verticalVelocity;

        // CharacterController.Move() applies the movement with collision detection.
        // Multiply by Time.deltaTime to make it frame-rate independent.
        _controller.Move(finalMove * Time.deltaTime);
    }
}
