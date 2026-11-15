namespace CameraGame.Recording
{
    /// <summary>
    /// Why a clip stopped (Story 1.15). Every stop keeps the footage recorded so far: none of these reasons
    /// throws a clip away, which is the rule "lowering never throws footage away" (AC2) applied to every way
    /// a recording can end.
    /// </summary>
    public enum StopReason
    {
        /// <summary>The player pressed Record (LMB / RT) a second time.</summary>
        PlayerPressed,

        /// <summary>The camera left the Viewfinder: the RaiseCamera release, the gallery's suppression, or any
        /// other caller of <c>PhotoModeController.SetPhotoMode(false)</c>.</summary>
        CameraLowered,

        /// <summary>The clip reached <see cref="RecordingConfig.SafeMaxClipSeconds"/> (or the preallocated
        /// capacity). Story 1.17 replaces this bound with the memory card's remaining seconds; the stop path
        /// stays the same.</summary>
        LengthLimit,

        /// <summary>The audio device or its configuration changed mid-clip (sample rate, speaker mode, a
        /// headset unplugged). The PCM after the change could not share the clip's timeline.</summary>
        AudioDeviceChanged,

        /// <summary>The recorder (or the controller driving it) was disabled or destroyed mid-clip: a scene
        /// unload, leaving play mode, or a rig switching it off.</summary>
        Disabled,
    }
}
