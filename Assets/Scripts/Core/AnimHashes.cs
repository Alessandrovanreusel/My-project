using UnityEngine;

namespace CameraGame.Core
{
    /// <summary>Cached Animator parameter hashes (faster + no magic strings). Add per story as needed.</summary>
    public static class AnimHashes
    {
        public static readonly int Speed = Animator.StringToHash("Speed"); // used by CharacterAnimator
    }
}
