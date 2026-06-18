using UnityEngine;

namespace CameraGame.PhotoMode
{
    /// <summary>
    /// Designer-facing tuning knobs for the photo CAPTURE feedback — the shutter flash and SFX volume
    /// (Story 1.5). Lives as a ScriptableObject asset (Assets/Data/Camera/CaptureConfig.asset) assigned
    /// to <see cref="PhotoModeController"/> in the Inspector, so capture feel re-tunes WITHOUT a recompile
    /// (Architecture §Configuration). Mirrors <see cref="CameraConfig"/> — no feel magic numbers in the
    /// controller; they all live here.
    /// </summary>
    [CreateAssetMenu(menuName = "CameraGame/Camera/Capture Config", fileName = "CaptureConfig")]
    public class CaptureConfig : ScriptableObject
    {
        public const float MinFlashDuration = 0.02f;
        public const float MaxFlashDuration = 0.2f;

        [Tooltip("Seconds the capture flash takes to fade from full to invisible. Kept under the 0.2s " +
                 "capture-to-feedback budget (NFR2) so the pulse always reads as instant.")]
        [Range(MinFlashDuration, MaxFlashDuration)] public float flashDuration = 0.12f;

        public float SafeFlashDuration => Mathf.Clamp(flashDuration, MinFlashDuration, MaxFlashDuration);

        [Tooltip("Tint of the full-screen capture flash. White by default for a clean shutter pop.")]
        public Color flashColor = Color.white;

        [Tooltip("Playback volume for the shutter SFX (PlayOneShot volumeScale). 1 = full.")]
        [Range(0f, 1f)] public float sfxVolume = 1f;

        private void OnValidate()
        {
            flashDuration = SafeFlashDuration;
            sfxVolume = Mathf.Clamp01(sfxVolume);
        }
    }
}
