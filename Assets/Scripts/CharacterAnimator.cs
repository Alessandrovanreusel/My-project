using UnityEngine;

/// <summary>
/// Bridges the CharacterController movement to the Animator.
/// Reads horizontal speed and feeds it to the "Speed" animator parameter.
/// The Animator Controller uses this to transition between Idle, Walk, and Run states.
/// </summary>
public class CharacterAnimator : MonoBehaviour
{
    // Reference to the Animator component that plays our animation clips
    private Animator _animator;

    // Reference to the CharacterController so we can read how fast the player is moving
    private CharacterController _controller;

    // Animator.StringToHash converts the string "Speed" into an integer ID.
    // Why? Because every frame we call SetFloat, and comparing integers is WAY faster
    // than comparing strings. "static readonly" means this is calculated once and shared
    // across all instances — it never changes.
    private static readonly int SpeedParam = Animator.StringToHash("Speed");

    private void Start()
    {
        // GetComponentInChildren searches this GameObject AND all its children.
        // We use this because the Animator component lives on the root ("main characters")
        // but could also be on a child like the Armature. This makes it flexible.
        _animator = GetComponentInChildren<Animator>();

        // GetComponent only searches THIS exact GameObject (not children).
        // CharacterController must be on the same object as this script.
        _controller = GetComponent<CharacterController>();

        // Warn in the console if something is missing — helps us debug setup issues
        if (_animator == null)
            Debug.LogWarning("CharacterAnimator: No Animator found on " + gameObject.name);
        if (_controller == null)
            Debug.LogWarning("CharacterAnimator: No CharacterController found on " + gameObject.name);
    }

    private void Update()
    {
        // Safety check: if either component is missing, skip everything.
        // Without this, we'd get a NullReferenceException crash every frame.
        if (_animator == null || _controller == null) return;

        // _controller.velocity gives us how fast the character moved LAST frame (in units/sec).
        // We copy it so we can zero out the Y axis without modifying the original.
        Vector3 horizontalVelocity = _controller.velocity;

        // Zero out vertical speed — we only care about horizontal movement.
        // Without this, falling or jumping would trigger the walk/run animation!
        horizontalVelocity.y = 0f;

        // .magnitude calculates the length of the vector (Pythagorean theorem: sqrt(x² + z²)).
        // This gives us a single number: how fast the character is moving on the ground.
        // We feed this into the Animator's "Speed" parameter, which drives the state machine:
        //   Speed < 0.1  → Idle
        //   Speed 0.1-7  → Walk
        //   Speed > 7    → Run
        _animator.SetFloat(SpeedParam, horizontalVelocity.magnitude);
    }
}
