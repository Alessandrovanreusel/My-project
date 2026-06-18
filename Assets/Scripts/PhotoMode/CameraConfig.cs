using UnityEngine;

namespace CameraGame.PhotoMode
{
    /// <summary>
    /// Designer-facing tuning knobs for the Photo-Mode camera (zoom). Lives as a ScriptableObject
    /// asset (Assets/Data/Camera/CameraConfig.asset) and is assigned to <see cref="PhotoModeController"/>
    /// in the Inspector, so zoom feel can be re-tuned WITHOUT a recompile (Architecture §Configuration).
    /// No FOV / sensitivity magic numbers belong in the controller — they all live here.
    /// </summary>
    [CreateAssetMenu(menuName = "CameraGame/Camera/Camera Config", fileName = "CameraConfig")]
    public class CameraConfig : ScriptableObject
    {
        [Tooltip("Wide end of the zoom range — the 1x / Walk field-of-view, in degrees. ~60 by GDD default.")]
        [Range(20f, 90f)] public float wideFov = 60f;

        [Tooltip("Telephoto end of the zoom range — the fully zoomed-in 4x field-of-view, in degrees. ~18 by GDD default.")]
        [Range(5f, 60f)] public float teleFov = 18f;

        [Tooltip("Fraction of the full 0->1 zoom range moved per scroll notch / dpad press. Higher = coarser zoom steps.")]
        [Range(0.02f, 0.5f)] public float zoomStepPerNotch = 0.15f;

        [Tooltip("How fast the camera FOV eases toward its target each second. Higher = snappier; lower = floatier.")]
        [Range(1f, 30f)] public float zoomLerpSpeed = 12f;

        [Tooltip("If true, each time the camera is raised into Photo mode it starts at 1x (wide), so every shot composes from wide first.")]
        public bool resetZoomOnRaise = true;

        // The Inspector ranges for wideFov (20-90) and teleFov (5-60) overlap, so a slip while tuning
        // could leave teleFov > wideFov — which would make "zoom in" *widen* the FOV (inverted zoom).
        // OnValidate runs in the Editor whenever an Inspector value changes, so we clamp the telephoto
        // end to never exceed the wide end and keep zoom direction correct.
        private void OnValidate()
        {
            if (teleFov > wideFov)
                teleFov = wideFov;
        }
    }
}
