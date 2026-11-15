using System.Text;
using UnityEngine;

namespace CameraGame.Recording
{
    /// <summary>
    /// Designer-facing tunables of the camcorder (Story 1.15): how long a clip may run before the memory card
    /// exists, how many actors and Animator layers a tick can hold, and the REC beep level. Lives at
    /// <c>Assets/Data/Recording/RecordingConfig.asset</c>.
    ///
    /// Read EVERY value through its <c>Safe*</c> accessor: a non-finite value falls back to the design
    /// default and an out-of-range one is clamped, with the same constants as the field initialisers.
    /// Problems are reported by <see cref="TryGetConfigProblem"/>, which <see cref="ClipRecorder"/> calls once
    /// in <c>Awake</c> and logs as a warning. There is deliberately no <c>OnValidate</c> that rewrites the
    /// asset: GradeHudConfig.cs explains why (it silently "fixes" a typo into a different authored value, and
    /// it never runs on an asset edited on disk or by a script).
    ///
    /// The tick rate (30 Hz) and the 16-bit sample format are NOT here: they are invariants of the footage
    /// format (<see cref="FootageFormat"/>), not tunables.
    /// </summary>
    [CreateAssetMenu(menuName = "CameraGame/Recording/Recording Config", fileName = "RecordingConfig")]
    public class RecordingConfig : ScriptableObject
    {
        public const float DefaultMaxClipSeconds = 120f;
        public const float MinClipSeconds = 1f;

        /// <summary>The longest clip any value can ask for. The recorder preallocates two clips' storage at
        /// this length at worst (~58 MB each for 48 kHz stereo), so a typo of 1e9 cannot ask for gigabytes.</summary>
        public const float MaxClipSecondsCap = 300f;

        public const int DefaultMaxActorsPerTick = 2;
        public const int MaxActorsPerTickCap = 16;
        public const int DefaultMaxAnimatorLayers = 2;
        public const int MaxAnimatorLayersCap = 8;
        public const float DefaultBeepVolume = 0.5f;

        [Tooltip("The longest a clip may run before it stops by itself (StopReason.LengthLimit), keeping the " +
                 "footage. 120 s is the ~2 min memory card the stakeholder confirmed on 2026-11-10; Story 1.17 " +
                 "replaces this bound with the card's remaining seconds. The recorder allocates its storage " +
                 "for this length at startup, so it also bounds the footage's memory (NFR3).")]
        public float maxClipSeconds = DefaultMaxClipSeconds;

        [Tooltip("How many live event actors one tick of the track can hold. EventManager.maxConcurrent is 1 " +
                 "for the slice (NFR6); more actors than this are left out of the clip, with one warning.")]
        public int maxActorsPerTick = DefaultMaxActorsPerTick;

        [Tooltip("How many Animator layers per actor the track records. DrunkAnimator has one; deeper layers " +
                 "are left out with one warning (the Recordability rule).")]
        public int maxAnimatorLayers = DefaultMaxAnimatorLayers;

        [Tooltip("Volume of the REC start and stop beeps (AudioSource.PlayOneShot volume scale, 0–1).")]
        [Range(0f, 1f)] public float beepVolume = DefaultBeepVolume;

        public float SafeMaxClipSeconds => ClampFinite(maxClipSeconds, MinClipSeconds, MaxClipSecondsCap, DefaultMaxClipSeconds);
        public int SafeMaxActorsPerTick => Mathf.Clamp(maxActorsPerTick, 1, MaxActorsPerTickCap);
        public int SafeMaxAnimatorLayers => Mathf.Clamp(maxAnimatorLayers, 1, MaxAnimatorLayersCap);
        public float SafeBeepVolume => ClampFinite(beepVolume, 0f, 1f, DefaultBeepVolume);

        /// <summary>The safe value for a config that may be missing: the design default when <paramref name="c"/>
        /// is null. The recorder records with defaults rather than not at all.</summary>
        public static float MaxClipSecondsOf(RecordingConfig c) => c != null ? c.SafeMaxClipSeconds : DefaultMaxClipSeconds;
        public static int MaxActorsPerTickOf(RecordingConfig c) => c != null ? c.SafeMaxActorsPerTick : DefaultMaxActorsPerTick;
        public static int MaxAnimatorLayersOf(RecordingConfig c) => c != null ? c.SafeMaxAnimatorLayers : DefaultMaxAnimatorLayers;
        public static float BeepVolumeOf(RecordingConfig c) => c != null ? c.SafeBeepVolume : DefaultBeepVolume;

        /// <summary>Non-finite → <paramref name="fallback"/>; otherwise clamped to [min, max].</summary>
        public static float ClampFinite(float v, float min, float max, float fallback)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return fallback;
            return Mathf.Clamp(v, min, max);
        }

        /// <summary>
        /// Describes every authored value the <c>Safe*</c> accessors would have to change, in one message, so a
        /// designer sees all of them at once. False when the config is clean.
        /// </summary>
        public bool TryGetConfigProblem(out string problem)
        {
            var sb = new StringBuilder();

            if (float.IsNaN(maxClipSeconds) || float.IsInfinity(maxClipSeconds))
                sb.Append($"maxClipSeconds is {maxClipSeconds}; using {DefaultMaxClipSeconds} s. ");
            else if (maxClipSeconds < MinClipSeconds)
                sb.Append($"maxClipSeconds {maxClipSeconds} is below {MinClipSeconds} s; clips stop at {MinClipSeconds} s. ");
            else if (maxClipSeconds > MaxClipSecondsCap)
                sb.Append($"maxClipSeconds {maxClipSeconds} is above the {MaxClipSecondsCap} s cap; clips stop at {MaxClipSecondsCap} s. ");

            if (maxActorsPerTick < 1 || maxActorsPerTick > MaxActorsPerTickCap)
                sb.Append($"maxActorsPerTick {maxActorsPerTick} is outside 1–{MaxActorsPerTickCap}; using {SafeMaxActorsPerTick}. ");

            if (maxAnimatorLayers < 1 || maxAnimatorLayers > MaxAnimatorLayersCap)
                sb.Append($"maxAnimatorLayers {maxAnimatorLayers} is outside 1–{MaxAnimatorLayersCap}; using {SafeMaxAnimatorLayers}. ");

            if (float.IsNaN(beepVolume) || float.IsInfinity(beepVolume))
                sb.Append($"beepVolume is {beepVolume}; using {DefaultBeepVolume}. ");
            else if (beepVolume < 0f || beepVolume > 1f)
                sb.Append($"beepVolume {beepVolume} is outside 0–1; using {SafeBeepVolume}. ");

            problem = sb.Length > 0 ? sb.ToString().TrimEnd() : null;
            return problem != null;
        }
    }
}
