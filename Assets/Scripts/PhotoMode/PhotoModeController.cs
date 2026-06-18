using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using CameraGame.Core;
using CameraGame.Events;
using CameraGame.Grading;

namespace CameraGame.PhotoMode
{
    /// <summary>
    /// Owns the two camera modes — <see cref="CameraMode.Walk"/> and <see cref="CameraMode.Photo"/> —
    /// on the single first-person camera rig (ADR-1). Hold the RaiseCamera input (RMB / gamepad LT)
    /// to enter Photo (the viewfinder overlay fades in); release to return to Walk.
    ///
    /// MUST live on the player object ("main characters") — the GameObject that carries
    /// <c>PlayerInput</c> — because PlayerInput "Send Messages" only calls OnXxx methods on its own
    /// GameObject, not on the camera or children. Stories 1.4 (zoom) and 1.5 (capture) gate on
    /// <see cref="IsPhotoMode"/>.
    /// </summary>
    public class PhotoModeController : MonoBehaviour
    {
        public enum CameraMode { Walk, Photo }

        [Header("Viewfinder")]
        [Tooltip("Root GameObject of the on-screen viewfinder overlay (a Screen-Space Canvas). " +
                 "Faded in on Photo, out on Walk. If unassigned, Photo Mode still tracks state but shows no overlay.")]
        [SerializeField] private GameObject viewfinderRoot;

        [Header("Transition")]
        [Tooltip("Seconds to fade the viewfinder in/out. Capped at the 0.3s input-responsiveness budget (NFR2). " +
                 "Story 1.4 folds camera tunables into a CameraConfig ScriptableObject.")]
        [SerializeField, Range(0f, 0.3f)] private float transitionSeconds = 0.15f;

        [Header("Zoom (Photo Mode)")]
        [Tooltip("Designer-facing zoom tunables (FOV endpoints, step, lerp speed). " +
                 "All zoom magic numbers live here, not in code (AC2).")]
        [SerializeField] private CameraConfig cameraConfig;

        [Tooltip("The single first-person Camera whose field-of-view zoom drives. " +
                 "If left unassigned, falls back to Camera.main at Awake.")]
        [SerializeField] private Camera photoCamera;

        [Header("Capture (Photo Mode)")]
        [Tooltip("Event channel raised on each capture, carrying a ShotGrade (placeholder until grading " +
                 "lands in Stories 1.9–1.10). The core capture capability — if unassigned, capture is inert.")]
        [SerializeField] private ShotCapturedChannel shotCapturedChannel;

        [Tooltip("Designer-facing capture-feedback tunables (flash duration/color, SFX volume). " +
                 "No capture magic numbers live in code (AC1).")]
        [SerializeField] private CaptureConfig captureConfig;

        [Tooltip("2D AudioSource (Spatial Blend = 0, Play On Awake off) used to play the shutter SFX. " +
                 "If unassigned, the shutter is silently skipped (fail-soft) — flash + event still fire.")]
        [SerializeField] private AudioSource captureAudioSource;

        [Tooltip("Shutter sound played on capture via PlayOneShot. May be left null until a clip exists — " +
                 "SFX is fail-soft inert, the flash + event still work.")]
        [SerializeField] private AudioClip shutterClip;

        [Tooltip("Full-screen UI CanvasGroup pulsed white on capture (the shutter flash). " +
                 "If unassigned, the flash is skipped (fail-soft).")]
        [SerializeField] private CanvasGroup captureFlash;

        /// <summary>Current camera mode. Read-only to the outside world.</summary>
        public CameraMode Mode { get; private set; } = CameraMode.Walk;

        /// <summary>True while the camera is raised. Future Zoom/Capture handlers gate on this (AC3).</summary>
        public bool IsPhotoMode => Mode == CameraMode.Photo;

        // Cached CanvasGroup on the viewfinder so we fade alpha instead of toggling the whole object
        // (smoother, and avoids per-toggle UI layout rebuilds).
        private CanvasGroup _viewfinderGroup;

        // False when the viewfinder reference is missing — we then track mode but skip all visual work
        // (fail-soft, NFR8: never throw in Update).
        private bool _viewfinderReady;

        // Normalized zoom: 0 = wide (1x / wideFov), 1 = telephoto (4x / teleFov). The FOV is a Lerp
        // across this every frame, so zoom always eases smoothly rather than snapping.
        private float _zoomT;

        // False when cameraConfig or the resolved camera is missing — zoom then goes inert while mode +
        // viewfinder still work (fail-soft, NFR8: never throw in Update). Mirrors _viewfinderReady.
        private bool _zoomReady;

        // Capture fail-soft flags, validated independently in Awake so one missing ref (e.g. no shutter
        // clip) never disables the others (the flash, the event) — same independence praised in the 1.4
        // review. _captureReady = can raise the event; _shutterReady = can play SFX; _flashReady = can pulse.
        private bool _captureReady;
        private bool _shutterReady;
        private bool _flashReady;

        // Flash decay timer: set to 1 on capture, eased to 0 over captureConfig.flashDuration in Update,
        // so the flash onset is immediate but its fade is frame-rate-eased rather than a one-frame pop.
        private float _flashT;

        // Cached UI graphic on the flash CanvasGroup so we set its tint once in Awake (no per-frame fetch).
        private Graphic _flashGraphic;

        private void Awake()
        {
            // --- Zoom setup (independent of the viewfinder so a missing overlay never disables zoom) ---
            if (photoCamera == null)
                photoCamera = Camera.main; // cache once here; never call Camera.main in Update.

            _zoomReady = cameraConfig != null && photoCamera != null;
            if (!_zoomReady)
                GameLog.Error("PhotoMode",
                    "CameraConfig or Camera unassigned — zoom disabled (mode + viewfinder still work).", this);

            // --- Capture setup (each piece guarded independently so one missing ref never disables the
            // others; placed before the viewfinder block so its early-return can't skip capture wiring). ---
            _captureReady = shotCapturedChannel != null;
            if (!_captureReady)
                GameLog.Error("PhotoMode",
                    "ShotCapturedChannel unassigned — capture will fire feedback but raise no event.", this);

            _shutterReady = captureAudioSource != null && shutterClip != null && captureConfig != null;
            if (!_shutterReady)
                // Info, not Warn: leaving the shutter clip unassigned is an expected, supported state
                // (no SFX authored yet) — capture still runs (flash + event). Keeps the console clean (NFR5).
                GameLog.Info("PhotoMode",
                    "Shutter SFX not assigned — capture runs silent (flash + event still fire).");

            _flashReady = captureFlash != null && captureConfig != null;
            if (_flashReady)
            {
                _flashGraphic = captureFlash.GetComponent<Graphic>();
                if (_flashGraphic == null)
                    _flashGraphic = captureFlash.GetComponentInChildren<Graphic>(includeInactive: true);

                if (_flashGraphic == null)
                {
                    GameLog.Error("PhotoMode",
                        "Capture flash has no visible UI Graphic — flash disabled (SFX + event still fire).", this);
                    _flashReady = false;
                }
                else
                {
                    // Start hidden and non-interactive; cache the Graphic so we tint it once, not per frame.
                    captureFlash.alpha = 0f;
                    captureFlash.blocksRaycasts = false;
                    captureFlash.interactable = false;
                    _flashGraphic.color = captureConfig.flashColor;
                }
            }
            else
            {
                GameLog.Error("PhotoMode",
                    "Capture flash inert (CanvasGroup or CaptureConfig unassigned) — SFX + event still fire.", this);
            }

            if (viewfinderRoot == null)
            {
                GameLog.Error("PhotoMode",
                    "viewfinderRoot is not assigned — Photo Mode will track state but show no overlay.", this);
                _viewfinderReady = false;
                return;
            }

            // A CanvasGroup lets us drive a fade via alpha. Add one if the overlay doesn't have it.
            _viewfinderGroup = viewfinderRoot.GetComponent<CanvasGroup>();
            if (_viewfinderGroup == null)
                _viewfinderGroup = viewfinderRoot.AddComponent<CanvasGroup>();

            _viewfinderReady = true;

            // Start hidden, in Walk mode. Keep the object active so Update can fade it in on demand.
            viewfinderRoot.SetActive(true);
            _viewfinderGroup.alpha = 0f;
            _viewfinderGroup.blocksRaycasts = false;
            _viewfinderGroup.interactable = false;
        }

        private void Update()
        {
            if (_viewfinderReady)
            {
                // Fade toward the target alpha for the current mode, finishing within transitionSeconds
                // (≤ 0.3s, so we always meet the raise/lower budget — NFR2).
                float target = IsPhotoMode ? 1f : 0f;
                float step = transitionSeconds > 0f ? Time.deltaTime / transitionSeconds : 1f;
                _viewfinderGroup.alpha = Mathf.MoveTowards(_viewfinderGroup.alpha, target, step);
            }

            if (_zoomReady)
            {
                // Drive FOV every frame toward its target so zoom eases smoothly even on frames with no
                // scroll input. In Walk we always target the wide end, so leaving Photo zooms back out.
                float targetFov = IsPhotoMode
                    ? Mathf.Lerp(cameraConfig.wideFov, cameraConfig.teleFov, _zoomT)
                    : cameraConfig.wideFov;
                photoCamera.fieldOfView =
                    Mathf.Lerp(photoCamera.fieldOfView, targetFov, cameraConfig.zoomLerpSpeed * Time.deltaTime);
            }

            if (_flashReady && _flashT > 0f)
            {
                // Capture pops _flashT to 1; ease it to 0 over flashDuration so the flash fades smoothly
                // instead of snapping off after one frame. alpha tracks _flashT directly (clamped).
                _flashT = Mathf.MoveTowards(_flashT, 0f, Time.unscaledDeltaTime / captureConfig.SafeFlashDuration);
                captureFlash.alpha = Mathf.Clamp01(_flashT);
            }
        }

        // =====================================================================
        // INPUT SYSTEM CALLBACK (PlayerInput "Send Messages")
        // =====================================================================
        // Method name "OnRaiseCamera" derives from the action name
        // GameConstants.InputActions.RaiseCamera ("RaiseCamera").
        //
        // The RaiseCamera action is type *Value* (not Button) on purpose: under PlayerInput Send
        // Messages a Button action delivers ONLY the press, never the release, which would latch the
        // camera raised forever — the exact "sticky" bug fixed for Sprint in Story 1.2. A Value action
        // delivers both edges, so value.isPressed correctly flips false on release.
        public void OnRaiseCamera(InputValue value)
        {
            SetMode(value.isPressed ? CameraMode.Photo : CameraMode.Walk);
        }

        // Method name "OnZoom" derives from the action name GameConstants.InputActions.Zoom ("Zoom").
        // The Zoom action is Value/Vector2 (mirrors Move/Look): a scroll/axis needs continuous values
        // (and the reset-to-zero frames), which a Button action would not deliver.
        public void OnZoom(InputValue value)
        {
            // Zoom is a no-op outside Photo mode (AC1 gating) and inert when fail-soft disabled (AC3).
            if (!IsPhotoMode || !_zoomReady) return;

            float y = value.Get<Vector2>().y;
            if (Mathf.Approximately(y, 0f)) return; // scroll/dpad deliver 0 on release frames.

            // Step by SIGN, not raw delta: mouse-wheel magnitude is device/platform dependent (≈1 vs ≈120),
            // so one notch / one dpad press = one zoomStepPerNotch increment. Predictable and tunable.
            _zoomT = Mathf.Clamp01(_zoomT + Mathf.Sign(y) * cameraConfig.zoomStepPerNotch);
        }

        // Method name "OnCapture" derives from the action name GameConstants.InputActions.Capture
        // ("Capture"). Unlike RaiseCamera/Zoom, Capture is a *Button* action on purpose: a discrete
        // one-shot tap. Under PlayerInput Send Messages a Button delivers exactly ONE call per press —
        // exactly one shot. (A Value action would fire on press AND release, double-capturing, unless
        // guarded with `if (!value.isPressed) return;`.) See story guardrail #1.
        public void OnCapture(InputValue value)
        {
            if (!IsPhotoMode) return; // AC2: capture is a no-op in Walk (camera lowered).

            // All three effects fire SYNCHRONOUSLY here — no coroutine/Invoke/GPU readback — to stay
            // inside the < 0.2 s capture-to-feedback budget (NFR2). Each is guarded independently (AC3).

            // Shutter SFX (layers over other clips; doesn't cut itself).
            if (_shutterReady)
                captureAudioSource.PlayOneShot(shutterClip, captureConfig.sfxVolume);

            // Flash: pop to full now; Update eases it back to 0 over flashDuration.
            if (_flashReady)
            {
                _flashT = 1f;
                captureFlash.alpha = 1f;
            }

            // Raise the event with a PLACEHOLDER grade — real grading is Stories 1.9–1.10.
            if (_captureReady)
                shotCapturedChannel.Raise(ShotGrade.Placeholder);

            // Milestone log — capture is user-driven/infrequent, so this is not console spam.
            GameLog.Info("PhotoMode", "Shot captured (placeholder grade).");
        }

        private void SetMode(CameraMode mode)
        {
            if (Mode == mode) return;
            Mode = mode;

            // Compose every Photo raise from wide (1x) when configured, so framing starts consistent.
            if (Mode == CameraMode.Photo && _zoomReady && cameraConfig.resetZoomOnRaise)
                _zoomT = 0f;

            GameLog.Debug_("PhotoMode", $"Mode -> {Mode}");
        }
    }
}
