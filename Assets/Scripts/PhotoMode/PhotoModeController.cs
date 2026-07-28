using System.Collections.Generic;
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

        [Header("Grading (Photo Mode)")]

        [Tooltip("Designer-facing grading tunables — the subject-size and occlusion gates (Story 1.9) plus " +
                 "the composition sweet spot and peak-timing window (Story 1.10). If unassigned, capture " +
                 "falls back to the placeholder grade rather than going silent.")]
        [SerializeField] private GradingConfig gradingConfig;

        [Tooltip("The EventManager whose live actors are the photographable subjects. Grading asks it for " +
                 "the current subjects at the moment of capture — subjects are pooled, so they are read " +
                 "live and never cached. If unassigned, capture falls back to the placeholder grade.")]
        [SerializeField] private EventManager eventManager;

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

        // Grading readiness, resolved independently of the three above (Story 1.9). A missing GradingConfig
        // must not disable the flash, and a missing flash must not disable grading — the same independence
        // the 1.4 and 1.5 reviews called out. When false, capture keeps raising the placeholder grade.
        private bool _gradingReady;

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

            // Grading needs a config, a source of subjects, and the camera it grades through. Resolved after
            // the camera fallback above so photoCamera is already settled.
            _gradingReady = gradingConfig != null && eventManager != null && photoCamera != null;
            if (!_gradingReady)
            {
                GameLog.Error("Grading",
                    "GradingConfig, EventManager or Camera unassigned — captures will keep returning the " +
                    "placeholder grade (feedback and the event still fire).", this);
            }
            else if (gradingConfig.TryGetConfigProblem(out string gradingProblem))
            {
                // Fail-soft must not mean invisible: these are values that let grading run while producing
                // meaningless verdicts, which is far harder to notice than an outright failure.
                GameLog.Warn("Grading", $"GradingConfig: {gradingProblem}");
            }

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
            SetPhotoMode(value.isPressed);
        }

        /// <summary>Raises or lowers the camera. The input handler above is a thin adapter over this, so
        /// anything that needs to drive the camera without synthesising an InputValue can call it.</summary>
        public void SetPhotoMode(bool raised)
        {
            SetMode(raised ? CameraMode.Photo : CameraMode.Walk);
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

        /// <summary>
        /// Sets zoom directly (0 = wide, 1 = full telephoto). Update still eases the FOV toward it, exactly
        /// as it does for scroll input — so callers must let a few frames pass before the framing settles.
        /// Setting <c>Camera.fieldOfView</c> from outside does NOT work: Update drives it from this value
        /// every frame and would immediately undo it.
        /// </summary>
        public void SetZoom(float normalized)
        {
            _zoomT = Mathf.Clamp01(normalized);
        }

        // Method name "OnCapture" derives from the action name GameConstants.InputActions.Capture
        // ("Capture"). Unlike RaiseCamera/Zoom, Capture is a *Button* action on purpose: a discrete
        // one-shot tap. Under PlayerInput Send Messages a Button delivers exactly ONE call per press —
        // exactly one shot. (A Value action would fire on press AND release, double-capturing, unless
        // guarded with `if (!value.isPressed) return;`.) See story guardrail #1.
        public void OnCapture(InputValue value)
        {
            Capture();
        }

        /// <summary>
        /// Takes the photo: feedback, grade, event. <see cref="OnCapture"/> is a thin input adapter over
        /// this, so the shutter can also be pulled by something other than a key press — which is what lets
        /// the real capture path be driven and photographed rather than approximated.
        /// </summary>
        public void Capture()
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

            // Grade AFTER firing the feedback: the flash and shutter are what make the camera feel instant,
            // and they must not wait on anything. Grading itself is a few dozen floating-point operations
            // plus a handful of linecasts, comfortably inside the 0.2 s budget (NFR2).
            ShotGrade grade = ShotGrade.Placeholder;

            // Explicitly Unevaluated, not `default`: a default-constructed GradeDetail leaves
            // VisibleFraction at 0, so OcclusionTested reads true and the overlay prints "line-of-sight 0%"
            // — "completely hidden" — for a shot that was never graded at all. Same mistake GradeMiss
            // .Unevaluated fixed, one field over.
            GradeDetail detail = GradeDetail.Unevaluated;
            if (_gradingReady)
                grade = GradeBestSubject(out detail);

            if (_captureReady)
                shotCapturedChannel.Raise(grade);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _debugGrade = grade;
            _debugDetail = detail;
            _debugUntil = Time.unscaledTime + DebugHoldSeconds;
#endif

            // Milestone log — capture is user-driven/infrequent, so this is not console spam. It reports the
            // per-axis BREAKDOWN as well as the total and the reason: a bare "62%" tells a designer nothing
            // about which axis cost them the shot, which is the only actionable part. (ShotGrade.ToString
            // carries composition × timing; GradeDetail carries the measurements behind them.)
            if (_gradingReady)
                GameLog.Info("Grading", $"Shot captured — {grade}, {detail}.");
            else
                GameLog.Info("PhotoMode", "Shot captured (placeholder grade — grading not configured).");
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // --- Grading debug overlay (Story 1.9, Task 6) ----------------------------------------------
        // Grading has no sound and no animation, so there is nothing to check by eye or ear the way the
        // cue (1.8) and the walk (1.7) could be. This draws what the grader actually saw — the projected
        // box and the verdict — so "the number looks wrong" becomes "the box is wrong". Editor/dev only;
        // it compiles out of a release build entirely.
        //
        // ⚠️ It is a SNAPSHOT, not a live readout: the box is in screen space as of the instant the shutter
        // fired, while the world carries on moving behind it. Linger too long and it reads as a box floating
        // over empty scenery — which already caused one wrong bug diagnosis from a screenshot taken a couple
        // of seconds after the shot. Hence the short hold and the explicit "at capture" label below.
        private GradeDetail _debugDetail;
        private ShotGrade _debugGrade;

        // -1, not 0: at Time.unscaledTime == 0 on the very first frame, `0 > 0` is false, so a zero default
        // let the overlay draw its "SHOT FAILED: Unevaluated" panel before any shutter had been pulled.
        private float _debugUntil = -1f;

        private const float DebugHoldSeconds = 1.5f;

        /// <summary>The grade from the most recent capture. Editor-only, for tooling that photographs the
        /// real capture path and needs the verdict that went with the frame.</summary>
        public ShotGrade LastGrade => _debugGrade;

        /// <summary>Diagnostic detail from the most recent capture (reason, screen rect, coverage).</summary>
        public GradeDetail LastDetail => _debugDetail;

        private void OnGUI()
        {
            if (Time.unscaledTime > _debugUntil) return;

            Rect r = _debugDetail.ScreenRect;
            bool hit = _debugDetail.Miss == GradeMiss.None;

            // GUI space has y growing DOWNWARD; screen-space rects from the grader grow upward.
            // Screen.height is correct here: WorldToScreenPoint returns WINDOW-space pixels (so does
            // cam.pixelRect), and GUI space spans the whole window — this stays right even for a camera with
            // a viewport rect. It would only be wrong if a targetTexture were bound at capture time, which
            // puts the rect in render-target space; that happens solely under the offline photo-shoot rig,
            // which draws its own box into the image and never reads this overlay.
            var boxed = new Rect(r.x, Screen.height - r.y - r.height, r.width, r.height);

            Color prev = GUI.color;
            GUI.color = hit ? Color.green : Color.red;
            if (r.width > 0f && r.height > 0f)
            {
                GUI.Box(boxed, GUIContent.none);
            }

            // The FRAME CENTRE, which is what the placement term is measured against. Drawn only for a
            // counted shot — on a rejected one it is noise over the reason the shot failed. Faint, so it
            // reads as a guide rather than as part of the grader's verdict (the box is that).
            //
            // ⚠️ This used to draw a rule-of-thirds grid. It stopped doing so when the placement term was
            // reversed to reward centring: a guide that no longer marks what the score is computed from is
            // worse than no guide at all — it is an invitation to diagnose a "wrong" score against lines
            // nothing measures.
            if (hit)
            {
                GUI.color = new Color(1f, 1f, 1f, 0.25f);
                DrawCentreGuide();
            }

            GUI.color = Color.white;
            var label = new Rect(12f, 12f, 640f, 120f);
            GUI.Box(label, GUIContent.none);
            GUI.Label(new Rect(20f, 16f, 620f, 24f),
                hit ? $"SHOT: {_debugGrade.Percent01:P0}  {_debugGrade.Stars}★" : $"SHOT FAILED: {_debugDetail.Miss}");

            // The three axes, which is what makes a disappointing score actionable: "62%" says nothing,
            // "composition 92% x timing 67%" says you were late, not badly framed.
            GUI.Label(new Rect(20f, 38f, 620f, 24f),
                $"composition {_debugGrade.Composition01:P0}  ×  timing {_debugGrade.Timing01:P0}" +
                $"   ·   subject seen {_debugGrade.Subject01:P0}");
            GUI.Label(new Rect(20f, 60f, 620f, 24f),
                $"height {_debugDetail.HeightFraction:P1}  (gate {(gradingConfig != null ? gradingConfig.SafeMinSubjectHeight : 0f):P1})" +
                $"   ·   framed {_debugDetail.FramedFraction:P0}  ·  area {_debugDetail.Coverage01:P1}");
            GUI.Label(new Rect(20f, 82f, 620f, 24f),
                $"peak offset {_debugDetail.PeakOffsetText}  ·  line-of-sight {_debugDetail.VisibleText}" +
                $"  ·  box {r.width:F0}x{r.height:F0}px");

            // Says out loud that the box is frozen at the shutter while the world moves on.
            GUI.Label(new Rect(20f, 104f, 620f, 24f), "▣ snapshot at capture — not a live view");
            GUI.color = prev;
        }

        /// <summary>Draws the frame's centre cross — the point the placement term measures distance from.
        /// Uses <c>photoCamera.pixelRect</c> rather than the whole window, so it lands where the grader
        /// actually measured even when the camera renders to a sub-rect of the screen.</summary>
        private void DrawCentreGuide()
        {
            Rect v = photoCamera != null ? photoCamera.pixelRect : new Rect(0f, 0f, Screen.width, Screen.height);
            const float thickness = 1f;
            const float arm = 24f;

            float cx = v.xMin + v.width * 0.5f;

            // GUI space grows DOWNWARD while the viewport grows upward, hence the flip through
            // Screen.height — the same conversion the grader's box above goes through.
            float cy = Screen.height - (v.yMin + v.height * 0.5f);

            GUI.DrawTexture(new Rect(cx - thickness * 0.5f, cy - arm, thickness, arm * 2f), Texture2D.whiteTexture);
            GUI.DrawTexture(new Rect(cx - arm, cy - thickness * 0.5f, arm * 2f, thickness), Texture2D.whiteTexture);
        }
#endif

        /// <summary>
        /// Grades every live subject and keeps the best result — the player photographed whichever one they
        /// framed best. <c>maxConcurrent</c> is 1 for the MVP slice, so today this loop runs once; written as
        /// a loop anyway so Epic 2's busier town cannot silently grade the wrong actor.
        ///
        /// Subjects are read LIVE from the manager here and never stored: they are pooled, so a cached
        /// reference does not go null when its event ends — it quietly becomes a different event
        /// (ISubject's liveness contract).
        /// </summary>
        private ShotGrade GradeBestSubject(out GradeDetail bestDetail)
        {
            // Seeded with the SAME reason as bestDetail below, not a bare 0% miss. The two are printed side
            // by side in the capture log, and seeding them from different places is how they come to
            // disagree — the grade saying "never evaluated" while the detail says "no subject".
            ShotGrade best = ShotGrade.Missed(GradeMiss.NoSubject);

            // Explicit, not `default`. An untouched GradeDetail reports no miss reason at all, which reads
            // as a pass — so a shutter press in an empty world was being reported as a counted shot even
            // though the grade itself was correctly 0%. Photographing nobody is a miss, and it should say
            // which kind. (GradeMiss.Unevaluated now guards the same mistake structurally.)
            bestDetail = new GradeDetail(GradeMiss.NoSubject, default, 0f, GradeDetail.NotEvaluated);

            IReadOnlyList<EventActor> actors = eventManager.ActiveActors;
            bool graded = false;

            for (int i = 0; i < actors.Count; i++)
            {
                EventActor actor = actors[i];
                if (actor == null) continue;   // destroyed between spawn and capture — skip, not throw

                // ActiveActors tracks the LIFECYCLE, not the GameObject's activation. Deactivating the
                // manager deactivates the pooled actors parented to it, so their Update stops, they never
                // reach Despawn, Despawned never fires and they sit in the list indefinitely — while an
                // inactive SkinnedMeshRenderer still reports perfectly plausible bounds. Without this check
                // the player photographs a visibly empty street and gets a graded hit.
                if (!actor.isActiveAndEnabled) continue;

                ShotGrade g = ShotGrader.Grade(photoCamera, actor, gradingConfig, out GradeDetail d);

                // Keyed on "have we graded anything yet", NOT on the loop index. With `i == 0` a null or
                // inactive entry at index 0 meant the seeding branch never ran, and since every miss shares
                // ShotGrade.Miss (0%) the `>` test also failed — so a real Occluded/TooSmall rejection was
                // reported to the player as "NoSubject". Strictly greater thereafter, so the FIRST graded
                // subject's reason survives when everything misses at 0%.
                if (!graded || g.Percent01 > best.Percent01)
                {
                    best = g;
                    bestDetail = d;
                    graded = true;
                }
            }

            return best;
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
