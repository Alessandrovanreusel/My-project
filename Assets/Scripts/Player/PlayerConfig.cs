using UnityEngine;

namespace CameraGame.Player
{
    /// <summary>
    /// The player's scale-tied tunables, in METRES and seconds: how fast they walk and sprint, how high they
    /// jump, and where the first-person camera sits on their body. ADR-5 (Story 2.1) settled the world scale:
    /// the town is roughly real-world metres and the characters are people about 1.77 m tall, so every number
    /// here is a person's number, not the old 8.86 m giant's.
    ///
    /// ⚠️ WHY A SCRIPTABLEOBJECT AND NOT FIELDS ON THE COMPONENTS. Two reasons, both from Story 2.1:
    ///   1. The architecture's configuration rule — tunables live as ScriptableObjects in Assets/Data/, never as
    ///      code literals. The eye height and forward nudge WERE code literals (8.5 and 1) in ThirdPersonCamera,
    ///      which is how a world rescale became a code change.
    ///   2. The verification rigs (Photo Shoot, Gallery, HUD) build private worlds with no player in them, but
    ///      they can load an asset. That makes this the one source of truth for "where is the player's eye" —
    ///      for the game AND for every rig, so the next rescale moves them all at once.
    ///
    /// The CharacterController's own capsule (height, radius, centre, skin width) stays ON THE COMPONENT: it is
    /// Unity's serialized physics shape and mirroring it here would create two answers to one question.
    /// <see cref="TryGetBodyProblem"/> checks the two against each other instead.
    ///
    /// ⚠️ Read every value through its <c>Safe*</c> accessor. [Min] and OnValidate are editor-only and this
    /// project hand-authors these assets as YAML, so a zero typed into the file reaches the game intact.
    /// </summary>
    [CreateAssetMenu(menuName = "CameraGame/Player/Player Config", fileName = "PlayerConfig")]
    public class PlayerConfig : ScriptableObject
    {
        // ADR-5 design constants. The defaults are also what the game falls back to, with one warning, when no
        // config is assigned or a value is unusable (NFR8: fail soft, never throw).
        public const float DefaultWalkSpeed = 4f;
        public const float DefaultSprintSpeed = 7f;
        public const float DefaultStepOffset = 0.35f;
        public const float DefaultJumpHeight = 1.2f;
        public const float DefaultGravity = -15f;
        public const float DefaultGroundStickVelocity = -2f;
        public const float DefaultEyeHeight = 1.65f;
        public const float DefaultCameraForwardNudge = 0.316f;
        public const float DefaultCameraNearClip = 0.095f;

        [Header("Movement (metres, seconds)")]
        [Tooltip("Walking speed in metres per second.")]
        [Min(0.01f)] public float walkSpeed = DefaultWalkSpeed;

        [Tooltip("Speed while Sprint is held, in metres per second. Must not be slower than walkSpeed.")]
        [Min(0.01f)] public float sprintSpeed = DefaultSprintSpeed;

        [Tooltip("Tallest ledge (metres) the player walks up without jumping — a kerb, a step. Unity's guide for a " +
                 "~2 m person is 0.1–0.4. Written into the CharacterController at Awake, so this is the one value.")]
        [Min(0f)] public float stepOffset = DefaultStepOffset;

        [Tooltip("Height of a jump's apex above the take-off point, in metres.")]
        [Min(0f)] public float jumpHeight = DefaultJumpHeight;

        [Tooltip("Downward acceleration while airborne, in m/s² (negative). The player's own gravity, not Physics.gravity.")]
        public float gravity = DefaultGravity;

        [Tooltip("Small downward speed (m/s, negative) held while grounded so the CharacterController stays " +
                 "pressed to the ground and isGrounded does not flicker on slopes.")]
        public float groundStickVelocity = DefaultGroundStickVelocity;

        [Header("First-person camera (metres)")]
        [Tooltip("Height of the player's eye — the camera — above their feet. ADR-5: 0.93 of a 1.77 m stature.")]
        [Min(0.01f)] public float eyeHeight = DefaultEyeHeight;

        [Tooltip("How far in front of the body axis the camera sits, so it never sees the inside of the player's own " +
                 "head. Scaled with the controller radius (ADR-5), so the camera's geometry against a wall stays " +
                 "what it was in body proportions.")]
        [Min(0f)] public float cameraForwardNudge = DefaultCameraForwardNudge;

        [Tooltip("The first-person camera's near clip plane, in metres. Scaled with the radius and the nudge: " +
                 "nudge + near − radius is how far the near plane reaches beyond the capsule.")]
        [Min(0.001f)] public float cameraNearClip = DefaultCameraNearClip;

        public float SafeWalkSpeed => Positive(walkSpeed, DefaultWalkSpeed);
        public float SafeSprintSpeed => Mathf.Max(SafeWalkSpeed, Positive(sprintSpeed, DefaultSprintSpeed));
        public float SafeStepOffset => NonNegative(stepOffset, DefaultStepOffset);
        public float SafeJumpHeight => NonNegative(jumpHeight, DefaultJumpHeight);
        // ⚠️ FINITE, not just "not NaN" (Story 2.1 review, reproduced): a gravity or ground-stick of -Infinity passed
        // the old NaN-only check, made _verticalVelocity -Infinity, and CharacterController.Move rejected every move
        // from then on — the player froze in mid-air with a clean console, and stayed frozen even after the asset
        // was fixed, because -Infinity + anything is still -Infinity.
        public float SafeGravity => !IsFinite(gravity) || gravity >= 0f ? DefaultGravity : gravity;
        public float SafeGroundStickVelocity =>
            !IsFinite(groundStickVelocity) || groundStickVelocity > 0f ? DefaultGroundStickVelocity : groundStickVelocity;
        public float SafeEyeHeight => Positive(eyeHeight, DefaultEyeHeight);
        public float SafeCameraForwardNudge => NonNegative(cameraForwardNudge, DefaultCameraForwardNudge);
        public float SafeCameraNearClip => Positive(cameraNearClip, DefaultCameraNearClip);

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static float Positive(float v, float fallback) =>
            float.IsNaN(v) || float.IsInfinity(v) || v <= 0f ? fallback : v;

        private static float NonNegative(float v, float fallback) =>
            float.IsNaN(v) || float.IsInfinity(v) || v < 0f ? fallback : v;

        /// <summary>The first value that is unusable and therefore being replaced by its default — reported
        /// with both numbers, because the value typed is not the value used and nothing else would say so.</summary>
        public bool TryGetConfigProblem(out string problem)
        {
            problem = null;
            if (SafeWalkSpeed != walkSpeed) problem = $"walkSpeed {walkSpeed} is not a positive speed — using {SafeWalkSpeed}";
            else if (SafeSprintSpeed != sprintSpeed) problem = $"sprintSpeed {sprintSpeed} is unusable or slower than walkSpeed — using {SafeSprintSpeed}";
            else if (SafeStepOffset != stepOffset) problem = $"stepOffset {stepOffset} is negative or not a number — using {SafeStepOffset}";
            else if (SafeJumpHeight != jumpHeight) problem = $"jumpHeight {jumpHeight} is negative or not a number — using {SafeJumpHeight}";
            else if (SafeGravity != gravity) problem = $"gravity {gravity} does not pull down or is not finite — using {SafeGravity}";
            else if (SafeGroundStickVelocity != groundStickVelocity) problem = $"groundStickVelocity {groundStickVelocity} pushes up or is not finite — using {SafeGroundStickVelocity}";
            else if (SafeEyeHeight != eyeHeight) problem = $"eyeHeight {eyeHeight} is not a positive height — using {SafeEyeHeight}";
            else if (SafeCameraForwardNudge != cameraForwardNudge) problem = $"cameraForwardNudge {cameraForwardNudge} is negative — using {SafeCameraForwardNudge}";
            else if (SafeCameraNearClip != cameraNearClip) problem = $"cameraNearClip {cameraNearClip} is not positive — using {SafeCameraNearClip}";
            return problem != null;
        }

        /// <summary>
        /// The structural invariant between this config and the body it is mounted on (Story 2.1, 1g): the eye
        /// must sit inside the body's height, and a step taller than the body is not a step. Pure, so it is
        /// tested without a scene; the controller calls it at Awake and OnValidate.
        /// </summary>
        public static bool TryGetBodyProblem(float eyeHeight, float stepOffset, float bodyHeight, float bodyRadius,
                                             out string problem)
        {
            problem = null;
            if (bodyHeight <= 0f || bodyRadius <= 0f)
                problem = $"the CharacterController has no usable body (height {bodyHeight}, radius {bodyRadius})";
            else if (eyeHeight >= bodyHeight)
                problem = $"eyeHeight {eyeHeight} m is at or above the body's height {bodyHeight} m — the camera would sit above the player's head";
            else if (stepOffset >= bodyHeight)
                problem = $"stepOffset {stepOffset} m is not below the body's height {bodyHeight} m";
            return problem != null;
        }

        /// <summary>Whether a NavMesh agent describes the same body, within tolerance (ADR-5: one agent the size
        /// of the characters).</summary>
        public static bool AgentMatchesBody(float agentRadius, float agentHeight, float bodyRadius, float bodyHeight,
                                            float tolerance = 0.05f) =>
            Mathf.Abs(agentRadius - bodyRadius) <= tolerance && Mathf.Abs(agentHeight - bodyHeight) <= tolerance;
    }
}
