using UnityEngine;
using UnityEngine.InputSystem;

// RequireComponent automatically adds these components when you add this script.
// It also prevents you from accidentally removing them in the Inspector.
// Our movement NEEDS a CharacterController (for physics) and PlayerInput (for controls).
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(PlayerInput))]
public class ThirdPersonController : MonoBehaviour
{
    [Header("Movement")]
    // Walking speed in units per second. The character moves this fast by default.
    [SerializeField] private float walkSpeed = 5f;

    // Sprint speed — used when holding the Sprint button (Shift by default).
    [SerializeField] private float sprintSpeed = 10f;

    // How long (in seconds) it takes the character to rotate toward the movement direction.
    // Lower = snappier turning, higher = more sluggish. 0.12 feels responsive but not instant.
    [SerializeField] private float rotationSmoothTime = 0.12f;

    [Header("Jumping")]
    // How high the character jumps (in meters/units).
    [SerializeField] private float jumpHeight = 1.2f;

    // Gravity strength. Negative because gravity pulls DOWN. Unity's default is -9.81,
    // but -15 feels snappier and more "gamey" (less floaty).
    [SerializeField] private float gravity = -15f;

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

    // Is the player currently holding the sprint button?
    private bool _isSprinting;

    // Current vertical speed (for gravity and jumping). Negative = falling, positive = rising.
    private float _verticalVelocity;

    // Used internally by SmoothDampAngle to track rotation momentum.
    // We never set this ourselves — Mathf.SmoothDampAngle reads and writes it.
    private float _rotationVelocity;

    // Set to true when the player presses Jump, consumed when the jump actually happens.
    // This "request" pattern prevents double-jumps and ensures the jump only fires once.
    private bool _jumpRequested;

    private void Awake()
    {
        // Cache the CharacterController reference. GetComponent is expensive,
        // so we do it once in Awake rather than every frame.
        _controller = GetComponent<CharacterController>();

        // If no camera was assigned in the Inspector, automatically find the Main Camera.
        // Camera.main returns the camera tagged "MainCamera" in the scene.
        if (cameraTransform == null && Camera.main != null)
            cameraTransform = Camera.main.transform;
    }

    // Update runs once per frame. We process physics and movement here.
    // The order matters: gravity first, then jump (which overrides gravity), then move.
    private void Update()
    {
        ApplyGravity();  // Pull the character down each frame
        HandleJump();    // Check if we should launch upward
        Move();          // Move horizontally + apply vertical velocity
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
        _moveInput = value.Get<Vector2>();
    }

    // Called when the Jump button is pressed (Space by default).
    // We only allow jumping if the character is on the ground.
    public void OnJump(InputValue value)
    {
        if (_controller.isGrounded)
            _jumpRequested = true;
    }

    // Called when Sprint is pressed or released (Shift by default).
    // value.isPressed is true while held, false when released.
    public void OnSprint(InputValue value)
    {
        _isSprinting = value.isPressed;
    }

    // =====================================================================
    // MOVEMENT LOGIC
    // =====================================================================

    private void ApplyGravity()
    {
        // When grounded and already falling, reset to a small negative value.
        // We use -2 instead of 0 to keep the CharacterController "pressed" against
        // the ground. If we used 0, isGrounded might flicker on slopes.
        if (_controller.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;
        else
            // When in the air, accelerate downward. This is basic physics:
            // velocity += acceleration * time. Gravity is negative, so we fall faster each frame.
            _verticalVelocity += gravity * Time.deltaTime;
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
            _verticalVelocity = Mathf.Sqrt(jumpHeight * -2f * gravity);

            // Consume the request so we don't jump again next frame.
            _jumpRequested = false;
        }
    }

    private void Move()
    {
        // Pick speed based on whether the player is sprinting.
        float speed = _isSprinting ? sprintSpeed : walkSpeed;

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
            // CAMERA-RELATIVE MOVEMENT — this is the key trick for 3rd person controllers.
            //
            // Atan2 gives us the angle of our input direction (in radians → convert to degrees).
            // Then we ADD the camera's Y rotation. This means:
            //   - Press W → move in the direction the CAMERA is looking
            //   - Press D → move to the RIGHT of where the camera is looking
            // Without the camera angle, W would always mean "world north" regardless of camera.
            float targetAngle = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg
                                + cameraTransform.eulerAngles.y;

            // Smoothly rotate the CHARACTER to face the movement direction.
            // SmoothDampAngle handles the wrap-around at 360°→0° gracefully.
            // _rotationVelocity is a ref parameter that SmoothDamp uses internally
            // to track momentum — it makes the rotation feel organic, not robotic.
            float angle = Mathf.SmoothDampAngle(
                transform.eulerAngles.y,   // Where we're facing now
                targetAngle,               // Where we want to face
                ref _rotationVelocity,     // Internal velocity tracker
                rotationSmoothTime         // How long the smooth transition takes
            );

            // Apply the rotation. We only rotate around Y (left/right turning).
            // X and Z stay at 0 so the character doesn't tilt forward or sideways.
            transform.rotation = Quaternion.Euler(0f, angle, 0f);

            // Convert the target angle into a world-space direction vector.
            // Quaternion.Euler creates a rotation, then we multiply by Vector3.forward
            // to get "which way is forward after rotating by targetAngle degrees?"
            moveDirection = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
        }

        // Combine horizontal movement with vertical velocity (gravity/jumping).
        // moveDirection.normalized * speed = horizontal movement at desired speed.
        // Vector3.up * _verticalVelocity = vertical movement (falling or jumping).
        Vector3 finalMove = moveDirection.normalized * speed + Vector3.up * _verticalVelocity;

        // CharacterController.Move() applies the movement with collision detection.
        // Multiply by Time.deltaTime to make it frame-rate independent:
        // at 60 FPS each frame moves 1/60th of the speed, at 30 FPS each frame moves 1/30th.
        // The result is the same distance per second regardless of frame rate.
        _controller.Move(finalMove * Time.deltaTime);
    }
}
