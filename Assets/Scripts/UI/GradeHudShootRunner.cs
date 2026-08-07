#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using CameraGame.Events;
using CameraGame.Gallery;
using CameraGame.Grading;
using CameraGame.PhotoMode;

namespace CameraGame.UI
{
    /// <summary>
    /// Photographs the grade-feedback HUD by pulling the REAL shutter in the REAL scene and grabbing the
    /// REAL screen. Driven by <c>GradeHudShootRig</c> (Tools > HUD > Grade HUD Shoot).
    ///
    /// This is NOT a test. It asserts nothing and cannot pass or fail. It runs the real path end to end —
    /// real EventManager, real pooled EventActor with its Animator and NavMeshAgent live, real
    /// <c>PhotoModeController.Capture()</c>, real ShotGrader, real GalleryService, and the HUD canvas that
    /// is actually authored in SampleScene — and produces evidence to be judged.
    ///
    /// ⚠️ WHY THIS LIVES IN THE RUNTIME ASSEMBLY, WRAPPED IN #if UNITY_EDITOR: Unity cannot resolve an
    /// editor-assembly MonoBehaviour attached to a scene object when entering play mode — the component
    /// comes back as "The referenced script (Unknown) is missing!" and the rig silently does nothing while
    /// the scene runs on around it. Paid for once by PhotoShootRunner and once by GalleryShootRunner; not
    /// paying for it a third time.
    ///
    /// ⚠️ THE CAMERA KEEPS RENDERING TO THE SCREEN. Every other rig in this project binds a RenderTexture
    /// for the whole run and reads that back. That technique CANNOT see this feature: a Screen Space –
    /// Overlay canvas is composited after every camera and is absent from <c>cam.Render()</c> by
    /// construction. Phase 0 settles the replacement technique with control shots before anything else runs.
    /// </summary>
    public class GradeHudShootRunner : MonoBehaviour
    {
        [HideInInspector] public GradeHud hud;
        [HideInInspector] public Canvas hudCanvas;
        [HideInInspector] public CanvasGroup hudGroup;
        [HideInInspector] public Image hudPanel;
        [HideInInspector] public PhotoModeController photo;
        [HideInInspector] public EventManager manager;
        [HideInInspector] public Camera cam;
        [HideInInspector] public ThirdPersonCamera chaseCamera;
        [HideInInspector] public ThirdPersonController player;
        [HideInInspector] public GalleryService service;
        [HideInInspector] public GalleryView view;
        [HideInInspector] public GradingConfig gradingConfig;
        [HideInInspector] public GradeHudConfig shippedHudConfig;
        [HideInInspector] public ShotCapturedChannel channel;
        [HideInInspector] public string outputDir = "_bmad-output/verification/hud";

        /// <summary>A colour nothing in this town produces, used as a MARKER when the question is "did any
        /// HUD pixel reach this image?". Scanning for a distinctive colour turns a judgement call about a
        /// dark panel into a count.</summary>
        private static readonly Color Marker = new Color(1f, 0f, 1f, 1f);

        private readonly StringBuilder _log = new StringBuilder();
        private readonly List<GradeHudConfig> _tempConfigs = new List<GradeHudConfig>();
        private readonly List<GameObject> _tempObjects = new List<GameObject>();

        // Everything below is recorded BEFORE it is mutated and put back in Restore(). A rig is scaffolding;
        // leaving the editor on borrowed settings is how the next story gets developed against values
        // nobody chose.
        private bool _prevRunInBackground;
        private float _prevTimeScale;
        private Vector3 _prevCamPos;
        private Quaternion _prevCamRot;
        private float _prevCamFov;
        private bool _prevChaseEnabled;
        private Color _prevPanelColor;
        private GradeHudConfig _prevHudConfig;

        private GameObject _wall;
        private GameObject _markerCanvas;      // the Phase 0 control marker
        private Image _markerImage;
        private PhotoModeController _placeholderPhoto;

        /// <summary>Whether Phase 0 proved the capture technique. Every later phase says so in its own
        /// output, because a picture from an unproven technique is not evidence — it is a guess with a
        /// filename.</summary>
        private bool _captureTrusted;

        private IEnumerator Start()
        {
            _prevRunInBackground = Application.runInBackground;
            _prevTimeScale = Time.timeScale;

            // Without this the editor stops rendering the moment it loses focus, and every capture comes
            // back as whatever was last on the backbuffer.
            Application.runInBackground = true;

            try
            {
                yield return RunAll();
            }
            finally
            {
                Restore();
                Finish();
            }
        }

        private IEnumerator RunAll()
        {
            Directory.CreateDirectory(outputDir);

            _prevCamPos = cam.transform.position;
            _prevCamRot = cam.transform.rotation;
            _prevCamFov = cam.fieldOfView;
            _prevHudConfig = GetHudConfig(hud);
            if (hudPanel != null) _prevPanelColor = hudPanel.color;

            // The chase camera writes Main Camera's transform every LateUpdate; left running, every framing
            // below would be whatever the player happened to be looking at.
            if (chaseCamera != null)
            {
                _prevChaseEnabled = chaseCamera.enabled;
                chaseCamera.enabled = false;
            }

            // Freeze the player so nothing walks off while the rig owns the camera.
            if (player != null) player.SetInputSuppressed(true);

            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.name = "RigWall";
            _wall.SetActive(false);
            _tempObjects.Add(_wall);

            Header();

            yield return Phase0_SettleTheCaptureTechnique();
            yield return PhaseA_EveryHudState();
            yield return PhaseB_TheHudMustNeverReachAPhotograph();
            yield return PhaseC_StressAndReuse();
            yield return PhaseD_Boundaries();
            yield return PhaseE_MeasureTheShutter();
        }

        private void Header()
        {
            _log.AppendLine("GRADE HUD SHOOT — real shutter, real grader, real scene, real screen.");
            _log.AppendLine("Nothing here asserts anything. These are photographs and measurements to be judged.");
            _log.AppendLine();
            _log.AppendLine($"Screen: {Screen.width}x{Screen.height}  (this is what the captures are of)");

            // ⚠️ THE CAPTURES ARE ONLY AS BIG AS THE GAME VIEW WAS. The HUD canvas scales with the screen
            // (CanvasScaler, ScaleWithScreenSize against 1920x1080), so the PROPORTIONS in these pictures
            // are what a player sees at any resolution — but absolute legibility is not, and "6 points on a
            // 4K display" is one of the failure modes this story names outright. Said out loud rather than
            // left for the reader to notice the pixel count.
            if (Screen.width < 1280)
                _log.AppendLine($"        ⚠ THAT IS A SMALL GAME VIEW. The layout in these pictures is " +
                                "faithful (the canvas scales), but do NOT settle 'is the text big enough' " +
                                "from them — judge that in the editor at a normal window size.");
            _log.AppendLine($"Scene:  {UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}" +
                            "  — the SHIPPED scene, not a rig-built world. Phases 0/A/B/C photograph the");
            _log.AppendLine("        canvas authored in SampleScene; Phase D builds throwaway canvases because");
            _log.AppendLine("        what it exercises is fail-soft CODE, not the scene's layout.");
            _log.AppendLine();

            _log.AppendLine("⚠ ERRORS THIS RIG DELIBERATELY PROVOKES (so a clean-console check is not confused):");
            _log.AppendLine("   · [Grading] 'GradingConfig, EventManager or Camera unassigned' — Phase A's");
            _log.AppendLine("     placeholder scenario needs a genuinely unconfigured PhotoModeController.");
            _log.AppendLine("   · [GradeHud] missing-reference errors — Phase D's whole purpose.");
            _log.AppendLine("   Any OTHER error is a finding.");
            _log.AppendLine();

            if (shippedHudConfig == null)
            {
                _log.AppendLine("HUD CONFIG: NONE WIRED. (!)");
            }
            else
            {
                // Read back from the LIVE asset. Adding a field to a ScriptableObject class does NOT add it
                // to an existing asset, and this project hand-authors those assets as YAML — so a mistyped
                // or renamed key silently reads as 0, and a zero hold is a readout nobody can read while the
                // console stays perfectly clean. Printing them beside the results makes that visible rather
                // than inferred.
                _log.AppendLine("HUD CONFIG read back from the live asset (Assets/Data/UI/GradeHudConfig.asset):");
                _log.AppendLine($"    holdSeconds          {shippedHudConfig.SafeHoldSeconds}" +
                                $"   (raw field: {shippedHudConfig.holdSeconds})");
                _log.AppendLine($"    fadeSeconds          {shippedHudConfig.SafeFadeSeconds}" +
                                $"   (raw field: {shippedHudConfig.fadeSeconds})");
                _log.AppendLine($"    hideOnCameraLowered  {shippedHudConfig.hideOnCameraLowered}");
                _log.AppendLine($"    total on screen      {shippedHudConfig.SafeVisibleSeconds}s");
                _log.AppendLine($"    countedColor         {shippedHudConfig.countedColor}");
                _log.AppendLine($"    missColor            {shippedHudConfig.missColor}");
                _log.AppendLine($"    placeholderColor     {shippedHudConfig.placeholderColor}");
                if (shippedHudConfig.TryGetConfigProblem(out string problem))
                    _log.AppendLine($"    ⚠ PROBLEM: {problem}");
                else
                    _log.AppendLine("    config reports no problem.");
            }

            _log.AppendLine();
            _log.AppendLine($"HUD canvas: renderMode {hudCanvas.renderMode}, sortingOrder {hudCanvas.sortingOrder}");
            _log.AppendLine("    Overlay is what makes AC5 structural rather than a matter of subscriber order.");
            _log.AppendLine($"    Panel screen rect: {HudRect()}");

            if (gradingConfig != null)
            {
                gradingConfig.ResolveTimingWindow(out float full, out float zero);
                _log.AppendLine($"GRADING: gate {gradingConfig.SafeMinSubjectHeight:0.###} frame-height; " +
                                $"timing full ±{full:0.##}s, zero ±{zero:0.##}s.");
            }

            _log.AppendLine();
        }

        // =====================================================================
        // PHASE 0 — settle the capture technique before trusting a single image
        // =====================================================================

        /// <summary>
        /// ⚠️ THIS PHASE GATES THE WHOLE RUN, AND IT EXISTS BECAUSE THIS PROJECT HAS ALREADY BEEN BURNED.
        /// <c>ScreenCapture.CaptureScreenshot</c> (the file-writing overload) produced TEN images of a UI
        /// overlay on blank white, because the Game View does not repaint its 3D content while the editor
        /// runs unattended (CLAUDE.md §Traps). <c>CaptureScreenshotAsTexture</c> is a different call and
        /// reads the backbuffer directly — but "different call, should be fine" is exactly the reasoning
        /// that produced ten blank images, so it is proved here instead of assumed.
        ///
        /// Three control shots, in order, each one a thing whose answer is already known:
        ///   1. The world with no marker — must NOT be uniform. A flat frame means the 3D never rendered.
        ///   2. A full-screen pure-magenta OVERLAY canvas — must dominate the capture. This is the one that
        ///      matters: it is the only proof that Overlay UI reaches the backbuffer at all, and if it fails
        ///      then nothing this rig produces about the HUD means anything.
        ///   3. The marker off again — magenta must go back to ~0, so shot 2 measured the marker rather
        ///      than something permanently magenta about the pipeline.
        /// </summary>
        private IEnumerator Phase0_SettleTheCaptureTechnique()
        {
            Section("PHASE 0 — is ScreenCapture.CaptureScreenshotAsTexture() telling the truth?");

            BuildMarkerCanvas();

            // Park the camera somewhere with actual scenery in it, so control 1 is a fair question.
            yield return AimAtSubjectOrForward();

            _markerImage.enabled = false;
            yield return null;

            Texture2D world = null, marked = null, cleared = null;
            try
            {
                yield return Grab(t => world = t);
                Save(world, "control_1_world.png");
                Stats(world, out float wMean, out float wStd, out float wWhite);
                float wMagenta = FractionNear(world, Marker, 0.25f);

                _log.AppendLine($"control 1 — the world, no marker      -> control_1_world.png");
                _log.AppendLine($"    mean luminance {wMean:0.000}   std-dev {wStd:0.000}   " +
                                $"near-white {wWhite:P1}   magenta {wMagenta:P2}");

                _markerImage.enabled = true;
                yield return null;
                yield return Grab(t => marked = t);
                Save(marked, "control_2_overlay_marker.png");
                float mMagenta = FractionNear(marked, Marker, 0.25f);
                _log.AppendLine($"control 2 — full-screen OVERLAY marker -> control_2_overlay_marker.png");
                _log.AppendLine($"    magenta {mMagenta:P2}   (an Overlay canvas the capture cannot see " +
                                "would read ~0%)");

                _markerImage.enabled = false;
                yield return null;
                yield return Grab(t => cleared = t);
                Save(cleared, "control_3_marker_off.png");
                float cMagenta = FractionNear(cleared, Marker, 0.25f);
                _log.AppendLine($"control 3 — marker off again           -> control_3_marker_off.png");
                _log.AppendLine($"    magenta {cMagenta:P2}");
                _log.AppendLine();

                // The three questions, answered by measurement rather than by inspection of a filename.
                bool worldRendered = wStd > 0.02f && wWhite < 0.9f;
                bool overlaySeen = mMagenta > 0.8f;
                bool markerReleased = cMagenta < 0.05f;

                _log.AppendLine($"    3D content actually rendered?   {(worldRendered ? "YES" : "NO")}" +
                                "   (a uniform or near-white frame is the known blank-capture trap)");
                _log.AppendLine($"    Overlay UI reaches the capture? {(overlaySeen ? "YES" : "NO")}");
                _log.AppendLine($"    ...and goes away again?         {(markerReleased ? "YES" : "NO")}");
                _log.AppendLine();

                _captureTrusted = worldRendered && overlaySeen && markerReleased;

                if (_captureTrusted)
                {
                    _log.AppendLine("=> THE TECHNIQUE IS TRUSTWORTHY. Every image below is a photograph of the");
                    _log.AppendLine("   real screen, Overlay UI included.");
                }
                else
                {
                    _log.AppendLine("=> ⚠⚠ THE TECHNIQUE IS NOT TRUSTWORTHY. STOP AND ESCALATE. Do not read any");
                    _log.AppendLine("   image below as evidence about the HUD, and do NOT substitute a readout of");
                    _log.AppendLine("   Text.text for a photograph of the screen — a label that is off-screen,");
                    _log.AppendLine("   behind the flash, at alpha 0, in a font missing the star glyph, or 6pt on");
                    _log.AppendLine("   a 4K display reads back perfectly correct and is a shipped bug.");
                }

                _log.AppendLine();
            }
            finally
            {
                if (world != null) Destroy(world);
                if (marked != null) Destroy(marked);
                if (cleared != null) Destroy(cleared);
            }
        }

        // =====================================================================
        // PHASE A — every state the readout can be in, photographed
        // =====================================================================

        /// <summary>One HUD state and how the rig reaches it. No "expected" field: the photograph and the
        /// grade the game actually produced are the result. The caption describes what the CAMERA DID —
        /// Story 1.10's captions were rewritten after one reading "just inside the gate" came back rejected,
        /// because a rig that states its own expectations produces confident, wrong evidence.</summary>
        private readonly struct State
        {
            public readonly string Name;
            public readonly string WhatTheCameraDid;
            public readonly float Distance;     // in subject heights
            public readonly float Yaw;          // degrees off the line to the subject
            public readonly bool Wall;
            public readonly Timing When;

            public State(string name, string whatTheCameraDid, float distance = 2.2f,
                         float yaw = 0f, bool wall = false, Timing when = Timing.Whenever)
            {
                Name = name; WhatTheCameraDid = whatTheCameraDid; Distance = distance;
                Yaw = yaw; Wall = wall; When = when;
            }
        }

        private enum Timing
        {
            /// <summary>Fire as soon as the framing is settled, wherever the lifecycle happens to be.</summary>
            Whenever,

            /// <summary>Hold until the actor is inside the peak WINDOW, then fire.</summary>
            InsidePeak,

            /// <summary>Hold until the shutter would land part-way down the timing curve.</summary>
            PartWayPastPeak,

            /// <summary>Hold until the shutter is far enough past the peak that timing is a hard zero —
            /// the COUNTED-BUT-0% state, which is the one AC2 exists for.</summary>
            WellPastPeak,
        }

        private static readonly State[] States =
        {
            new State("a_money_shot", "Two heights back, dead centre, shutter pulled inside the peak window.",
                      2.2f, when: Timing.InsidePeak),
            new State("b_mid_counted", "Same framing, shutter pulled part-way down the timing curve.",
                      2.2f, when: Timing.PartWayPastPeak),
            new State("c_counted_but_zero", "Same framing, shutter pulled well after the peak window ended.",
                      2.2f, when: Timing.WellPastPeak),
            new State("e_too_far", "Far down the road — sixteen subject-heights back.", 16f),
            new State("f_blocked", "A wall dropped between the camera and him.", 1.4f, wall: true),
            new State("g_out_of_frame", "Turned ninety degrees away from him.", 2.2f, yaw: 90f),
            // ⚠️ 0.05 heights, not 1.2. At 1.2 the box projects wholly past an edge and the grader
            // (correctly) calls that OutsideFrustum, so the first run produced two identical
            // "he was outside the frame" readouts and NO picture of the BehindCamera wording at all.
            // BehindCamera needs the subject's CENTRE behind the near plane, i.e. the camera inside him
            // facing away — which is reachable in play because the drunk prefab has no collider.
            new State("h_behind_you", "Camera inside him, facing the opposite way.", 0.05f, yaw: 180f),
        };

        private IEnumerator PhaseA_EveryHudState()
        {
            Section("PHASE A — every state the readout can be in");
            _log.AppendLine("Every capture below is photo.Capture() — the same method the player's shutter");
            _log.AppendLine("calls. Each state is photographed TWICE from the same camera position: once with");
            _log.AppendLine("the readout hidden and once with it up, and the HUD's own screen rect is diffed");
            _log.AppendLine("between them. That percentage is the honest answer to 'is the readout actually in");
            _log.AppendLine("this picture?' — a question a readout of Text.text cannot answer at all.");
            _log.AppendLine();
            _log.AppendLine("THE QUESTIONS ONLY THE PICTURES ANSWER:");
            _log.AppendLine("  · Can you read it in the time it is up?");
            _log.AppendLine("  · Can you tell a_money_shot from c_counted_but_zero AT A GLANCE?");
            _log.AppendLine("  · Does the miss line say something a player would act on?");
            _log.AppendLine("  · Do the editor debug overlay (top-left) and the player HUD (bottom) collide?");
            _log.AppendLine();

            // --- The empty street ------------------------------------------------------------------------
            //
            // ⚠️ THIS BLOCK IS LONGER THAN IT LOOKS LIKE IT SHOULD BE, AND THE SHORT VERSION LIED.
            // The first version of this rig did `manager.enabled = false` and captioned the shot "shutter
            // pressed with nobody in the world". That disables the MANAGER's Update — it does not despawn
            // anything — so in the real scene, where a drunk is already walking, the shot came back
            // "composition 93% of TownDrunk, seen 100%" under a caption promising an empty street. That is
            // Story 1.11's `f_behind_wall` failure exactly: a caption asserting what the code did not do,
            // which was one sentence away from being written up as a HUD defect.
            //
            // What actually produces GradeMiss.NoSubject is every live actor being inactive, because
            // GradeBestSubject skips `!actor.isActiveAndEnabled`. So the rig deactivates them, takes the
            // shot, and puts them straight back.
            manager.enabled = false;

            var parked = new List<GameObject>();
            foreach (EventActor a in manager.ActiveActors)
            {
                if (a == null || !a.gameObject.activeSelf) continue;
                a.gameObject.SetActive(false);
                parked.Add(a.gameObject);
            }

            yield return null;
            yield return null;

            yield return ShootState(new State("d_nobody_there",
                    $"Shutter pressed with the street genuinely empty — the manager is off AND all " +
                    $"{parked.Count} live actor(s) are deactivated, so nothing photographable exists."),
                requireActor: false);

            for (int i = 0; i < parked.Count; i++)
                if (parked[i] != null) parked[i].SetActive(true);

            manager.enabled = true;

            float waited = 0f;
            while (manager.ActiveActors.Count == 0 && waited < 60f) { waited += Time.deltaTime; yield return null; }
            if (manager.ActiveActors.Count == 0)
            {
                _log.AppendLine("No actor ever spawned — the rest of Phase A cannot run. SUSPECT THE RIG.");
                _log.AppendLine();
                yield break;
            }

            foreach (State s in States)
            {
                yield return ShootState(s, requireActor: true);
                yield return WaitForHudToClear();
            }

            yield return ShootPlaceholder();
        }

        private IEnumerator ShootState(State s, bool requireActor)
        {
            EventActor actor = null;

            if (requireActor)
            {
                float w = 0f;
                while (!TryGetActor(out actor) && w < 60f) { w += Time.deltaTime; yield return null; }
                if (actor == null)
                {
                    _log.AppendLine($"{s.Name}  —  SKIPPED: nobody in the world to photograph.");
                    _log.AppendLine();
                    yield break;
                }

                yield return HoldFramingUntil(s);

                // Re-read from the manager rather than reusing the reference from before the waits. Actors
                // are POOLED: a held reference does not go null when its event ends, it quietly becomes a
                // different event (ISubject's liveness contract).
                if (!TryGetActor(out actor))
                {
                    _log.AppendLine($"{s.Name}  —  SKIPPED: he despawned mid-pose (pooled reuse).");
                    _log.AppendLine();
                    yield break;
                }

                PlaceCamera(s, actor.Bounds);
            }
            else
            {
                yield return AimAtSubjectOrForward();
            }

            photo.SetPhotoMode(true);
            yield return null;

            string liveId = actor != null ? actor.SubjectId : "(nobody)";
            float liveOffset = actor != null ? actor.PeakOffset : float.NaN;

            yield return CaptureAndPhotograph(s.Name, s.WhatTheCameraDid, liveId, liveOffset);
        }

        /// <summary>
        /// The placeholder state, produced the honest way: a SECOND PhotoModeController with no
        /// GradingConfig, which is the supported Story 1.5 state that yields <c>ShotGrade.Placeholder</c>.
        ///
        /// ⚠️ NOT by raising the channel with a hand-made grade. That would prove the HUD can render a
        /// struct the rig built, which is not the same claim — the shipped path is a real capture whose
        /// grading was never configured, and that is what is driven here.
        ///
        /// Built INACTIVE and wired before activation: AddComponent on an already-active GameObject runs
        /// Awake immediately, before the rig could set the serialized fields, so the readiness flags would
        /// be resolved against an unwired component and the scenario would be testing the wrong thing.
        /// </summary>
        private IEnumerator ShootPlaceholder()
        {
            var go = new GameObject("RigPlaceholderPhoto");
            go.SetActive(false);
            _tempObjects.Add(go);

            var ph = go.AddComponent<PhotoModeController>();

            // Copy the real controller's wiring, then withhold exactly one thing: grading.
            CopyPrivate(photo, ph, "photoCamera");
            CopyPrivate(photo, ph, "cameraConfig");
            CopyPrivate(photo, ph, "captureConfig");
            CopyPrivate(photo, ph, "shotCapturedChannel");
            CopyPrivate(photo, ph, "captureAudioSource");
            CopyPrivate(photo, ph, "shutterClip");
            CopyPrivate(photo, ph, "captureFlash");
            CopyPrivate(photo, ph, "viewfinderRoot");
            SetPrivate(ph, "gradingConfig", null);
            SetPrivate(ph, "eventManager", null);

            _log.AppendLine("i_not_graded  —  a capture taken with grading unconfigured (the supported");
            _log.AppendLine("                 Story 1.5 state). The [Grading] error just above this in the");
            _log.AppendLine("                 console is this scenario arming itself, not a defect.");

            go.SetActive(true);
            _placeholderPhoto = ph;
            yield return null;

            if (TryGetActor(out EventActor a))
                PlaceCamera(new State("i", "", 2.2f), a.Bounds);

            ph.SetPhotoMode(true);
            yield return null;

            yield return CaptureAndPhotograph("i_not_graded",
                "Two heights back, dead centre — but grading is not configured at all.",
                a != null ? a.SubjectId : "(nobody)", float.NaN, shutter: ph);

            ph.SetPhotoMode(false);
            go.SetActive(false);
        }

        /// <summary>
        /// The core of Phase A: a reference frame with the readout hidden, the real shutter, then a frame
        /// with the readout up — and the HUD's own screen rect diffed between the two.
        /// </summary>
        private IEnumerator CaptureAndPhotograph(string name, string whatTheCameraDid, string liveId,
                                                 float liveOffset, PhotoModeController shutter = null)
        {
            shutter = shutter ?? photo;

            // Reference frame, same pose, readout hidden. Taken FIRST so it cannot contain the readout the
            // shutter is about to raise.
            Texture2D before = null, after = null, settled = null;
            try
            {
                yield return Grab(t => before = t);

                int storedBefore = service != null ? service.Shots.Count : 0;

                shutter.Capture();                              // the real shutter

                // One frame, so the HUD's handler has run and the canvas is up, but well inside the hold.
                yield return null;
                yield return Grab(t => after = t);

                Rect hudRect = HudRect();
                float changed = FractionDifferent(before, after, hudRect, 0.06f);

                Save(after, name + "_at_shutter.png");

                // ⚠️ AND AGAIN ONCE THE SHUTTER FLASH HAS GONE — because the first run's pictures were ALL
                // taken at the worst possible instant and I nearly read that as a legibility defect.
                // Capture pops the flash CanvasGroup to alpha 1 and eases it out over flashDuration
                // (0.12 s). The flash lives on a canvas BELOW this one, so it does not cover the readout —
                // but the readout's panel is translucent, so for that eighth of a second the white flash
                // shines THROUGH it and every photograph came back bleached. That instant is real and the
                // player sees it, so it is kept as `_at_shutter`; it is simply not the frame to judge
                // "can you read this?" from, because it is 0.12 s of a 2.8 s readout.
                float waited = 0f;
                while (waited < 0.5f) { waited += Time.unscaledDeltaTime; yield return null; }
                yield return Grab(t => settled = t);
                Save(settled, name + ".png");

                _log.AppendLine($"{name}  —  {whatTheCameraDid}");
                _log.AppendLine($"    grade at shutter:   {shutter.LastGrade}");
                _log.AppendLine($"    detail:             {shutter.LastDetail}");
                _log.AppendLine($"    live subject id / PeakOffset at shutter: '{liveId}' / " +
                                $"{(float.IsNaN(liveOffset) ? "n/a" : liveOffset.ToString("+0.00;-0.00;0.00") + "s")}");
                _log.AppendLine($"    HUD showing:        {hud.IsShowing}   canvas enabled: {hudCanvas.enabled}" +
                                $"   alpha {(hudGroup != null ? hudGroup.alpha.ToString("0.00") : "n/a")}");
                _log.AppendLine($"    pixels changed inside the HUD's rect: {changed:P1}" +
                                (changed < 0.02f
                                    ? "   ⚠ THE READOUT IS NOT IN THE PICTURE"
                                    : "   (the readout is genuinely on screen)"));
                _log.AppendLine($"    what the labels say:");
                _log.AppendLine($"        1| {ReadLabel("ratingLabel")}");
                _log.AppendLine($"        2| {ReadLabel("axesLabel")}");
                _log.AppendLine($"        3| {ReadLabel("whyLabel")}");

                if (service != null)
                    _log.AppendLine($"    gallery: {storedBefore} -> {service.Shots.Count} stored");

                _log.AppendLine($"    -> {name}.png            (0.5 s after the shutter — the flash has gone;");
                _log.AppendLine($"                                      THIS is the frame to judge legibility from)");
                _log.AppendLine($"    -> {name}_at_shutter.png (one frame after the shutter — the flash is still");
                _log.AppendLine($"                                      up and shines through the panel)");
                _log.AppendLine();
            }
            finally
            {
                if (before != null) Destroy(before);
                if (after != null) Destroy(after);
                if (settled != null) Destroy(settled);
            }
        }

        // =====================================================================
        // PHASE B — AC5: the readout must never reach a stored photograph
        // =====================================================================

        /// <summary>
        /// ⚠️ THIS IS THE ONE AC WHERE A STRUCTURAL ARGUMENT IS NOT ENOUGH. "Overlay canvases are composited
        /// after every camera" is true and is the reason the render mode was chosen — but
        /// <c>GalleryService.HandleShotCaptured</c> renders the camera SYNCHRONOUSLY inside the same channel
        /// raise this HUD subscribes to, on the same frame, in an order nobody controls. So the file gets
        /// looked at.
        ///
        /// The readout is repainted a colour nothing in this town produces, which turns "is there a HUD in
        /// this thumbnail?" from a judgement call into a count. Both at full alpha and mid-fade, because a
        /// translucent HUD leaking would look like nothing at all in a glance.
        /// </summary>
        private IEnumerator PhaseB_TheHudMustNeverReachAPhotograph()
        {
            Section("PHASE B — can any HUD pixel reach a stored photograph? (AC5)");

            if (service == null)
            {
                _log.AppendLine("No GalleryService in the scene — this phase cannot run. SUSPECT THE RIG.");
                _log.AppendLine();
                yield break;
            }

            _log.AppendLine("The readout is repainted PURE MAGENTA for this phase — panel and text — because");
            _log.AppendLine("nothing in this town is magenta, so 'did a HUD pixel land in this image' becomes a");
            _log.AppendLine("count instead of an opinion. Captures are taken with the readout at full alpha and");
            _log.AppendLine("again mid-fade.");
            _log.AppendLine();

            GradeHudConfig magenta = MakeConfig(hold: 3f, fade: 2f, hideOnLower: false);
            magenta.countedColor = Marker;
            magenta.missColor = Marker;
            magenta.placeholderColor = Marker;
            SetHudConfig(hud, magenta);
            if (hudPanel != null) hudPanel.color = Marker;

            service.ClearAll();
            photo.SetPhotoMode(true);

            int scanned = 0, contaminated = 0;
            float worstMagenta = 0f;
            int saved = 0;

            for (int i = 0; i < 8; i++)
            {
                if (TryGetActor(out EventActor a)) PlaceCamera(new State("b", "", 2.2f), a.Bounds);
                yield return null;

                photo.Capture();

                // i even: the readout is at FULL alpha on the very next frame.
                // i odd: let it get well into the fade first, so a translucent leak has its chance too.
                yield return null;
                if (i % 2 == 1)
                {
                    float t = 0f;
                    while (t < 3.4f && hud.IsShowing) { t += Time.unscaledDeltaTime; yield return null; }
                    if (TryGetActor(out EventActor b)) PlaceCamera(new State("b", "", 2.2f), b.Bounds);
                    photo.Capture();
                    yield return null;
                }

                yield return new WaitForEndOfFrame();
            }

            _log.AppendLine($"Captures taken: 12.  Gallery holds {service.Shots.Count}.");
            _log.AppendLine();
            _log.AppendLine("Every stored thumbnail, scanned for magenta:");

            foreach (CapturedShot shot in service.Shots)
            {
                if (!shot.HasImage)
                {
                    _log.AppendLine($"    {shot.Id}: NO PICTURE STORED");
                    continue;
                }

                scanned++;
                float m = FractionNear(shot.Image, Marker, 0.25f);
                if (m > worstMagenta) worstMagenta = m;
                if (m > 0.0005f) contaminated++;

                _log.AppendLine($"    {shot.Id}: {shot.Image.width}x{shot.Image.height}   magenta {m:P3}" +
                                (m > 0.0005f ? "   ⚠ HUD PIXELS IN A STORED PHOTOGRAPH" : ""));

                // Write a couple out so a human can confirm the scan is scanning photographs and not, say,
                // twelve identical black rectangles.
                if (saved < 3)
                {
                    SaveRaw(shot.Image, $"stored_thumbnail_{saved}.png");
                    saved++;
                }
            }

            _log.AppendLine();
            _log.AppendLine($"=> {scanned} thumbnails scanned, {contaminated} contaminated, " +
                            $"worst magenta fraction {worstMagenta:P3}.");
            _log.AppendLine("   LOOK AT stored_thumbnail_*.png as well as reading that number: a scan of blank");
            _log.AppendLine("   images would also report zero contamination.");
            _log.AppendLine();

            // Put the readout back to the shipped look for the phases that follow.
            SetHudConfig(hud, _prevHudConfig);
            if (hudPanel != null) hudPanel.color = _prevPanelColor;
            yield return null;
        }

        // =====================================================================
        // PHASE C — reuse, the second cycle onward, and the overlaps
        // =====================================================================

        private IEnumerator PhaseC_StressAndReuse()
        {
            Section("PHASE C — stress: this project's bugs live in reuse, never in the first cycle");

            photo.SetPhotoMode(true);
            if (TryGetActor(out EventActor a0)) PlaceCamera(new State("c", "", 2.2f), a0.Bounds);
            yield return null;

            // --- Ten captures in rapid succession while the readout is still up ------------------------
            _log.AppendLine("Ten captures in a row with the readout still up. The readout must always describe");
            _log.AppendLine("the LATEST shot, and no line may survive from the previous one.");
            _log.AppendLine();

            for (int i = 0; i < 10; i++)
            {
                if (TryGetActor(out EventActor a)) PlaceCamera(new State("c", "", 2.2f), a.Bounds);
                photo.Capture();
                yield return null;

                _log.AppendLine($"  capture {i + 1,2}: grade {photo.LastGrade.Percent01:P0} " +
                                $"{(photo.LastGrade.IsMiss ? "MISS:" + photo.LastGrade.MissReason : "counted")}" +
                                $"   |  1| {ReadLabel("ratingLabel")}");
                _log.AppendLine($"                 2| {ReadLabel("axesLabel")}");
                _log.AppendLine($"                 3| {ReadLabel("whyLabel")}");
            }

            yield return Photograph("stress_rapid_captures.png",
                "the readout after ten captures in a row — it must describe the tenth, not a mixture");

            // --- A capture landing DURING the fade -----------------------------------------------------
            _log.AppendLine();
            _log.AppendLine("A capture arriving mid-fade must restart the readout at full, not resume the");
            _log.AppendLine("remainder of the previous hold.");

            photo.Capture();
            yield return null;
            float waited = 0f;
            while (hud.IsShowing && hudGroup != null && hudGroup.alpha > 0.5f && waited < 8f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            _log.AppendLine($"    alpha just before the second shutter: " +
                            $"{(hudGroup != null ? hudGroup.alpha.ToString("0.00") : "n/a")}");

            photo.Capture();
            yield return null;
            _log.AppendLine($"    alpha one frame after it:             " +
                            $"{(hudGroup != null ? hudGroup.alpha.ToString("0.00") : "n/a")}   (wants 1.00)");

            yield return Photograph("stress_capture_during_fade.png", "a capture that landed mid-fade");

            // --- The camera lowered mid-hold -----------------------------------------------------------
            _log.AppendLine();
            _log.AppendLine($"Camera lowered mid-hold, with hideOnCameraLowered = " +
                            $"{(shippedHudConfig != null ? shippedHudConfig.hideOnCameraLowered.ToString() : "?")}.");

            photo.Capture();
            yield return null;
            _log.AppendLine($"    showing while raised: {hud.IsShowing}");
            photo.SetPhotoMode(false);
            yield return null;
            yield return null;
            _log.AppendLine($"    showing after lower:  {hud.IsShowing}   canvas enabled: {hudCanvas.enabled}");

            yield return Photograph("stress_camera_lowered.png",
                "one frame after the camera was lowered mid-hold");

            // --- The gallery opened mid-hold — the overlap hide-on-lower exists for -------------------
            if (view != null)
            {
                _log.AppendLine();
                _log.AppendLine("The gallery opened straight after a capture. The gallery is Screen Space -");
                _log.AppendLine("Camera and this readout is Overlay, so a lingering readout would draw OVER the");
                _log.AppendLine("gallery grid whatever the sorting orders say.");

                photo.SetPhotoMode(true);
                yield return null;
                if (TryGetActor(out EventActor a2)) PlaceCamera(new State("c", "", 2.2f), a2.Bounds);
                photo.Capture();
                yield return null;
                photo.SetPhotoMode(false);
                yield return null;

                view.Open();
                yield return null;
                yield return null;

                _log.AppendLine($"    gallery open: {view.IsOpen}   readout showing: {hud.IsShowing}");
                yield return Photograph("stress_gallery_over_hud.png",
                    "the gallery open immediately after a capture — no readout may be sitting on it");

                view.Close();
                yield return null;
            }

            // --- Pooled respawns: does the readout ever describe a stale subject? ----------------------
            _log.AppendLine();
            _log.AppendLine("Two pooled respawns. A pooled reference does NOT go null when its event ends — it");
            _log.AppendLine("quietly becomes a different event — so the question is whether the readout ever");
            _log.AppendLine("describes a lifecycle other than the one that was live at ITS shutter.");
            _log.AppendLine();

            Time.timeScale = 4f;
            photo.SetPhotoMode(true);

            for (int cycle = 1; cycle <= 2; cycle++)
            {
                float t = 0f;
                while (TryGetActor(out _) && t < 90f) { t += Time.unscaledDeltaTime; yield return null; }
                t = 0f;
                EventActor actor = null;
                while (!TryGetActor(out actor) && t < 90f) { t += Time.unscaledDeltaTime; yield return null; }
                if (actor == null)
                {
                    _log.AppendLine($"  cycle {cycle}: no new lifecycle within 90 s — phase cut short.");
                    break;
                }

                PlaceCamera(new State("c", "", 2.2f), actor.Bounds);
                yield return null;
                if (!TryGetActor(out actor)) continue;

                PlaceCamera(new State("c", "", 2.2f), actor.Bounds);
                string liveId = actor.SubjectId;
                int instance = actor.GetInstanceID();

                photo.Capture();
                yield return null;

                _log.AppendLine($"  cycle {cycle}: live actor instance {instance}, id '{liveId}'  ->  " +
                                $"grade names '{(photo.LastGrade.HasSubject ? photo.LastGrade.SubjectId : "(nobody)")}'" +
                                $"  {(photo.LastGrade.SubjectId == liveId ? "agrees" : "⚠ DISAGREES")}");
                _log.AppendLine($"            readout: 1| {ReadLabel("ratingLabel")}   " +
                                $"2| {ReadLabel("axesLabel")}   3| {ReadLabel("whyLabel")}");
            }

            Time.timeScale = _prevTimeScale;
            _log.AppendLine();
            _log.AppendLine("The instance id repeating across cycles is the pooling working as designed.");
            _log.AppendLine();
        }

        // =====================================================================
        // PHASE D — every tunable at its extremes, every reference missing
        // =====================================================================

        /// <summary>
        /// ⚠️ THIS PHASE USES THROWAWAY CANVASES, NOT THE SCENE'S. What it exercises is fail-soft CODE — the
        /// readiness flags resolved at Awake — and Awake has already run on the shipped HUD. What it does
        /// NOT prove is anything about the scene's layout; Phases A–C do that. Stated out loud so nobody
        /// reads a Phase D picture as evidence about the shipped canvas.
        ///
        /// Every configuration is an in-memory <c>CreateInstance</c>, so the shipped asset is never mutated
        /// and there is nothing to restore even if this throws (the pattern Story 1.11 used, deviation #6).
        /// </summary>
        private IEnumerator PhaseD_Boundaries()
        {
            Section("PHASE D — boundaries: zero, negative, huge, NaN, and absent");
            _log.AppendLine("What is being checked is not that these 'work' — it is that each disables ONLY its");
            _log.AppendLine("own function, logs once, and leaves capture, grading, flash, shutter and the");
            _log.AppendLine("gallery running (NFR8, AC5). The [GradeHud] errors in the console below are this");
            _log.AppendLine("phase doing its job.");
            _log.AppendLine();

            // Take the shipped HUD out of the way so exactly one readout is listening at a time.
            hud.gameObject.SetActive(false);
            yield return null;

            yield return Boundary("hold 0", MakeConfig(0f, 0.6f));
            yield return Boundary("hold negative", MakeConfig(-4f, 0.6f));
            yield return Boundary("hold huge (9999)", MakeConfig(9999f, 0.6f));
            yield return Boundary("hold NaN", MakeConfig(float.NaN, 0.6f));
            yield return Boundary("fade negative", MakeConfig(2.2f, -3f));
            yield return Boundary("fade NaN", MakeConfig(2.2f, float.NaN));
            yield return Boundary("fade huge (9999)", MakeConfig(2.2f, 9999f));
            yield return Boundary("every colour at alpha 0", InvisibleColours());
            yield return Boundary("config ASSET MISSING", null);
            yield return Boundary("channel UNASSIGNED", MakeConfig(2.2f, 0.6f), noChannel: true);
            yield return Boundary("labels UNASSIGNED", MakeConfig(2.2f, 0.6f), noLabels: true);
            yield return Boundary("CanvasGroup UNASSIGNED", MakeConfig(2.2f, 0.6f), noGroup: true);

            // Put the shipped readout back for Phase E.
            hud.gameObject.SetActive(true);
            yield return null;
            _log.AppendLine("Shipped HUD re-enabled for the remaining phase.");
            _log.AppendLine();
        }

        private IEnumerator Boundary(string label, GradeHudConfig cfg, bool noChannel = false,
                                     bool noLabels = false, bool noGroup = false)
        {
            _log.AppendLine($"{label}:");

            if (cfg != null && cfg.TryGetConfigProblem(out string problem))
                _log.AppendLine($"    config reports: {problem}");
            else if (cfg != null)
                _log.AppendLine("    config reports: no problem.");
            else
                _log.AppendLine("    config reports: (there is no config)");

            GradeHud probe = BuildThrowawayHud(cfg, noChannel ? null : channel, noLabels, noGroup);
            yield return null;

            photo.SetPhotoMode(true);
            if (TryGetActor(out EventActor a)) PlaceCamera(new State("d", "", 2.2f), a.Bounds);
            yield return null;

            int storedBefore = service != null ? service.Shots.Count : 0;
            photo.Capture();
            yield return null;

            _log.AppendLine($"    capture still graded:  {photo.LastGrade}");
            _log.AppendLine($"    readout showing:       {probe.IsShowing}");
            if (service != null)
                _log.AppendLine($"    gallery still stored:  {storedBefore} -> {service.Shots.Count}");

            // The one that actually needs a clock: a broken hold must not leave the readout up forever.
            float t = 0f;
            while (probe.IsShowing && t < 12f) { t += Time.unscaledDeltaTime; yield return null; }
            _log.AppendLine($"    readout cleared after: " +
                            (probe.IsShowing ? "STILL UP AFTER 12 s ⚠" : $"{t:0.00}s"));

            Destroy(probe.gameObject);
            yield return null;
            _log.AppendLine();
        }

        // =====================================================================
        // PHASE E — NFR2: measure the shutter, do not assert it
        // =====================================================================

        private IEnumerator PhaseE_MeasureTheShutter()
        {
            Section("PHASE E — how long the shutter takes with the readout listening (NFR2)");
            _log.AppendLine("Story 1.11 measured mean 6.036 ms / worst 7.321 ms with the gallery listening.");
            _log.AppendLine("That is the baseline. The readout formats three strings once per shutter press and");
            _log.AppendLine("should be lost in the noise; if it is not, that is the finding. Measured here, not");
            _log.AppendLine("re-asserted from 1.11 — the deferred item against Story 1.10 is exactly that");
            _log.AppendLine("somebody re-asserted a number instead of re-taking it.");
            _log.AppendLine();

            const int Samples = 40;

            photo.SetPhotoMode(true);
            if (TryGetActor(out EventActor a)) PlaceCamera(new State("m", "", 2.2f), a.Bounds);
            yield return null;

            yield return MeasureCaptures("HUD listening", Samples);

            // Disabling the component unsubscribes in OnDisable, which is also a direct test that a disabled
            // GradeHud leaves no live delegate on the channel asset.
            hud.enabled = false;
            yield return null;
            bool showedWhileDisabled = false;

            yield return MeasureCaptures("HUD disabled", Samples);
            showedWhileDisabled |= hud.IsShowing;

            _log.AppendLine($"    while disabled the readout showed itself: {showedWhileDisabled}" +
                            "   (wants False — a disabled HUD must leave no live delegate on the channel)");

            hud.enabled = true;
            yield return null;
            _log.AppendLine();
        }

        private IEnumerator MeasureCaptures(string label, int samples)
        {
            var sw = new Stopwatch();
            double first = 0d, total = 0d, worst = 0d;

            for (int i = 0; i < samples; i++)
            {
                if (TryGetActor(out EventActor a)) PlaceCamera(new State("m", "", 2.2f), a.Bounds);
                yield return null;

                sw.Restart();
                photo.Capture();          // the real shutter, end to end
                sw.Stop();

                double ms = sw.Elapsed.TotalMilliseconds;
                if (i == 0) first = ms;         // reported separately: the first call pays warm-up costs
                else { total += ms; if (ms > worst) worst = ms; }

                yield return new WaitForEndOfFrame();
            }

            double mean = samples > 1 ? total / (samples - 1) : first;
            _log.AppendLine($"  {label,-16}  first {first:0.000} ms   mean {mean:0.000} ms   worst {worst:0.000} ms" +
                            $"   (budget 200 ms → {(worst > 0 ? 200.0 / worst : 0):0}x margin at the worst sample)");
        }

        // =====================================================================
        // CAPTURE — the technique Phase 0 exists to validate
        // =====================================================================

        /// <summary>
        /// Grabs the whole screen, Overlay UI included.
        ///
        /// ⚠️ MUST run after <c>WaitForEndOfFrame</c> — before that the backbuffer holds the PREVIOUS
        /// frame, which is how a rig comes to photograph the state it was in before the thing it just did.
        /// The result is a Texture2D the caller owns and must Destroy.
        ///
        /// Written as a callback rather than a return value because a C# iterator cannot return one, and
        /// the alternative — a field — is how two phases end up sharing a texture one of them destroyed.
        /// </summary>
        private IEnumerator Grab(System.Action<Texture2D> onGrabbed)
        {
            yield return new WaitForEndOfFrame();
            onGrabbed(ScreenCapture.CaptureScreenshotAsTexture());
        }

        /// <summary>Grabs and writes one frame, for the phases whose evidence is simply "look at this".</summary>
        private IEnumerator Photograph(string file, string note)
        {
            Texture2D t = null;
            try
            {
                yield return Grab(x => t = x);
                Save(t, file);
                _log.AppendLine($"    -> {file} : {note}");
            }
            finally
            {
                if (t != null) Destroy(t);
            }
        }

        private void Save(Texture2D tex, string file)
        {
            if (tex == null) return;
            try
            {
                File.WriteAllBytes(Path.Combine(outputDir, file), tex.EncodeToPNG());
            }
            catch (IOException e)
            {
                UnityEngine.Debug.LogError($"[GradeHudShoot] Could not write {file}: {e.Message}");
            }
        }

        /// <summary>Writes a texture the rig does NOT own (a gallery thumbnail) without touching it.</summary>
        private void SaveRaw(Texture2D tex, string file) => Save(tex, file);

        /// <summary>The HUD panel's rect in SCREEN pixels. An Overlay canvas's world space IS screen space,
        /// so its world corners are already the number we want — no camera projection involved, which is
        /// also why this stays correct with no camera at all.</summary>
        private Rect HudRect()
        {
            RectTransform rt = hudPanel != null
                ? hudPanel.rectTransform
                : hud.GetComponent<RectTransform>();

            if (rt == null) return new Rect(0f, 0f, Screen.width, Screen.height);

            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            float xMin = Mathf.Min(corners[0].x, corners[2].x);
            float xMax = Mathf.Max(corners[0].x, corners[2].x);
            float yMin = Mathf.Min(corners[0].y, corners[2].y);
            float yMax = Mathf.Max(corners[0].y, corners[2].y);
            return new Rect(xMin, yMin, xMax - xMin, yMax - yMin);
        }

        // --- Image measurement ------------------------------------------------------------------------

        private static void Stats(Texture2D t, out float meanLuma, out float stdDev, out float nearWhite)
        {
            Color[] px = t.GetPixels();
            double sum = 0d, sumSq = 0d;
            int white = 0;

            for (int i = 0; i < px.Length; i++)
            {
                float l = px[i].r * 0.299f + px[i].g * 0.587f + px[i].b * 0.114f;
                sum += l;
                sumSq += l * l;
                if (l > 0.95f) white++;
            }

            meanLuma = (float)(sum / px.Length);
            stdDev = Mathf.Sqrt(Mathf.Max(0f, (float)(sumSq / px.Length) - meanLuma * meanLuma));
            nearWhite = white / (float)px.Length;
        }

        private static float FractionNear(Texture2D t, Color target, float tolerance)
        {
            Color[] px = t.GetPixels();
            int hits = 0;
            for (int i = 0; i < px.Length; i++)
            {
                if (Mathf.Abs(px[i].r - target.r) <= tolerance
                    && Mathf.Abs(px[i].g - target.g) <= tolerance
                    && Mathf.Abs(px[i].b - target.b) <= tolerance) hits++;
            }
            return hits / (float)px.Length;
        }

        /// <summary>Fraction of pixels inside <paramref name="rect"/> that differ between two captures. This
        /// is what turns "the readout is on screen" from an assumption into a measurement — and it is the
        /// only structural check in this rig that a mis-anchored, invisible or unfonted label cannot pass.</summary>
        private static float FractionDifferent(Texture2D a, Texture2D b, Rect rect, float tolerance)
        {
            if (a == null || b == null || a.width != b.width || a.height != b.height) return 0f;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(rect.xMin), 0, a.width - 1);
            int x1 = Mathf.Clamp(Mathf.CeilToInt(rect.xMax), 0, a.width);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(rect.yMin), 0, a.height - 1);
            int y1 = Mathf.Clamp(Mathf.CeilToInt(rect.yMax), 0, a.height);
            if (x1 <= x0 || y1 <= y0) return 0f;

            int w = x1 - x0, h = y1 - y0;
            Color[] pa = a.GetPixels(x0, y0, w, h);
            Color[] pb = b.GetPixels(x0, y0, w, h);

            int diff = 0;
            for (int i = 0; i < pa.Length; i++)
            {
                if (Mathf.Abs(pa[i].r - pb[i].r) > tolerance
                    || Mathf.Abs(pa[i].g - pb[i].g) > tolerance
                    || Mathf.Abs(pa[i].b - pb[i].b) > tolerance) diff++;
            }
            return diff / (float)pa.Length;
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        private GradeHudConfig MakeConfig(float hold, float fade, bool hideOnLower = true)
        {
            GradeHudConfig c = ScriptableObject.CreateInstance<GradeHudConfig>();
            c.holdSeconds = hold;
            c.fadeSeconds = fade;
            c.hideOnCameraLowered = hideOnLower;
            _tempConfigs.Add(c);
            return c;
        }

        private GradeHudConfig InvisibleColours()
        {
            GradeHudConfig c = MakeConfig(2.2f, 0.6f);
            c.countedColor = new Color(1f, 1f, 1f, 0f);
            c.missColor = new Color(1f, 0.5f, 0.4f, 0f);
            c.placeholderColor = new Color(0.6f, 0.6f, 0.6f, 0f);
            return c;
        }

        /// <summary>Builds a bare HUD for Phase D. Created INACTIVE and wired before activation, because
        /// AddComponent on an active GameObject runs Awake immediately — the readiness flags would then be
        /// resolved against an unwired component and every boundary would report the same thing.</summary>
        private GradeHud BuildThrowawayHud(GradeHudConfig cfg, ShotCapturedChannel ch, bool noLabels,
                                           bool noGroup)
        {
            var go = new GameObject("RigProbeHud", typeof(RectTransform));
            go.SetActive(false);
            _tempObjects.Add(go);

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 200;
            go.AddComponent<CanvasScaler>();
            if (!noGroup) go.AddComponent<CanvasGroup>();

            var probe = go.AddComponent<GradeHud>();
            SetPrivate(probe, "shotCapturedChannel", ch);
            SetPrivate(probe, "config", cfg);

            if (!noLabels)
            {
                SetPrivate(probe, "ratingLabel", MakeProbeLabel(go.transform, "R", 0f));
                SetPrivate(probe, "axesLabel", MakeProbeLabel(go.transform, "A", -40f));
                SetPrivate(probe, "whyLabel", MakeProbeLabel(go.transform, "W", -80f));
            }

            go.SetActive(true);
            return probe;
        }

        private static Text MakeProbeLabel(Transform parent, string name, float y)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, worldPositionStays: false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(900f, 36f);
            rt.anchoredPosition = new Vector2(0f, y);
            var text = go.AddComponent<Text>();
            text.raycastTarget = false;
            return text;
        }

        private string ReadLabel(string field)
        {
            var t = GetPrivate(hud, field) as Text;
            if (t == null) return "<no label>";
            return string.IsNullOrEmpty(t.text) ? "(blank)" : t.text;
        }

        private static GradeHudConfig GetHudConfig(GradeHud h) => GetPrivate(h, "config") as GradeHudConfig;

        private static void SetHudConfig(GradeHud h, GradeHudConfig cfg) => SetPrivate(h, "config", cfg);

        private bool TryGetActor(out EventActor actor)
        {
            actor = null;
            if (manager == null || manager.ActiveActors.Count == 0) return false;

            EventActor a = manager.ActiveActors[0];

            // ActiveActors tracks the LIFECYCLE, not activation: an inactive entry reports perfectly
            // plausible bounds while being invisible in the picture.
            if (a == null || !a.isActiveAndEnabled) return false;

            actor = a;
            return true;
        }

        /// <summary>Holds the framing on the live actor until the lifecycle reaches the moment this state is
        /// about. The actor is re-read on EVERY poll, never held across the wait — these waits run for tens
        /// of seconds and a cached reference would very likely become a different, pooled event.</summary>
        private IEnumerator HoldFramingUntil(State s)
        {
            if (s.When == Timing.Whenever) yield break;
            if (gradingConfig == null) yield break;

            gradingConfig.ResolveTimingWindow(out float full, out float zero);

            // Where the shutter should fall, in seconds of PeakOffset (positive early, negative late).
            float target = s.When == Timing.InsidePeak ? 0f
                         : s.When == Timing.PartWayPastPeak ? -(full + zero) * 0.5f
                         : -(zero + 1.5f);

            // --- Arm: wait for a lifecycle that has not yet passed the moment --------------------------
            // Without this the rig joins mid-lifecycle, finds the crossing already behind it and fires
            // immediately at the wrong instant — while writing a caption that says otherwise.
            const float Margin = 0.75f;
            float waited = 0f;
            bool armed = false;
            Time.timeScale = 3f;

            while (waited < 90f)
            {
                if (TryGetActor(out EventActor a))
                {
                    if (a.PeakOffset > target + Margin) { armed = true; break; }
                    PlaceCamera(s, a.Bounds);
                }
                waited += Time.unscaledDeltaTime;
                yield return null;
            }

            Time.timeScale = _prevTimeScale;

            if (!armed)
            {
                _log.AppendLine($"    (no lifecycle reached the arming point for {s.When} within 90 s — " +
                                "firing wherever it is)");
                yield break;
            }

            // --- Track him and stop at the moment ------------------------------------------------------
            float tracked = 0f;
            while (tracked < 90f)
            {
                if (!TryGetActor(out EventActor a)) yield break;

                PlaceCamera(s, a.Bounds);
                if (a.PeakOffset <= target) yield break;

                tracked += Time.deltaTime;
                yield return null;
            }
        }

        private IEnumerator WaitForHudToClear()
        {
            float t = 0f;
            while (hud.IsShowing && t < 12f) { t += Time.unscaledDeltaTime; yield return null; }
            yield return null;
        }

        private IEnumerator AimAtSubjectOrForward()
        {
            if (TryGetActor(out EventActor a))
            {
                PlaceCamera(new State("aim", "", 2.2f), a.Bounds);
            }
            else if (player != null)
            {
                // No subject: stand where the player is and look along the street, so the frame contains
                // real scenery rather than sky. Control shot 1 depends on there being something to see.
                Transform p = player.transform;
                cam.transform.position = p.position + Vector3.up * 6f;
                cam.transform.rotation = Quaternion.LookRotation(p.forward, Vector3.up);
            }

            yield return null;
            yield return null;
        }

        /// <summary>Positions and aims the camera so the subject's centre lands in the middle of the frame.
        /// Lifted from PhotoShootRunner/GalleryShootRunner — same geometry, same reasoning.</summary>
        private void PlaceCamera(State s, Bounds b)
        {
            float height = Mathf.Max(0.01f, b.size.y);

            cam.transform.position = b.center + Vector3.forward * (Mathf.Max(0.2f, s.Distance) * height);

            Vector3 camPos = cam.transform.position;
            Vector3 toSubject = b.center - camPos;
            if (toSubject.sqrMagnitude < 1e-6f) toSubject = Vector3.forward;

            cam.transform.rotation = Quaternion.LookRotation(toSubject, Vector3.up)
                                     * Quaternion.Euler(0f, s.Yaw, 0f);

            _wall.SetActive(s.Wall);
            if (s.Wall)
            {
                _wall.transform.position = Vector3.Lerp(camPos, b.center, 0.45f);
                _wall.transform.localScale = new Vector3(height * 5f, height * 5f, 0.4f);
                _wall.transform.rotation = Quaternion.LookRotation(b.center - camPos, Vector3.up);
            }

            Physics.SyncTransforms();
        }

        private void BuildMarkerCanvas()
        {
            _markerCanvas = new GameObject("RigControlMarker", typeof(RectTransform));
            _tempObjects.Add(_markerCanvas);

            var canvas = _markerCanvas.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            // Above everything, including the HUD at 100: the control shot is asking whether an Overlay
            // canvas reaches the capture AT ALL, and anything drawing over it would confuse the answer.
            canvas.sortingOrder = 900;
            _markerCanvas.AddComponent<CanvasScaler>();

            var imgGo = new GameObject("Marker", typeof(RectTransform));
            imgGo.transform.SetParent(_markerCanvas.transform, worldPositionStays: false);
            var rt = (RectTransform)imgGo.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = rt.offsetMax = Vector2.zero;

            _markerImage = imgGo.AddComponent<Image>();
            _markerImage.color = Marker;
            _markerImage.raycastTarget = false;
            _markerImage.enabled = false;
        }

        private void Section(string title)
        {
            _log.AppendLine();
            _log.AppendLine(new string('=', 78));
            _log.AppendLine(title);
            _log.AppendLine(new string('=', 78));
            _log.AppendLine();
        }

        // --- Private-field plumbing -------------------------------------------------------------------
        //
        // Reflection rather than SerializedObject, deliberately: several of these run on LIVE components
        // mid-play (swapping the HUD's config for Phase B), where a SerializedObject write competes with
        // the running instance rather than simply setting the field.

        private const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;

        private static void SetPrivate(object target, string field, object value)
        {
            FieldInfo f = target.GetType().GetField(field, Private);
            if (f == null)
            {
                UnityEngine.Debug.LogError($"[GradeHudShoot] No field '{field}' on {target.GetType().Name}.");
                return;
            }
            f.SetValue(target, value);
        }

        private static object GetPrivate(object target, string field)
        {
            FieldInfo f = target.GetType().GetField(field, Private);
            return f != null ? f.GetValue(target) : null;
        }

        private static void CopyPrivate(object from, object to, string field) =>
            SetPrivate(to, field, GetPrivate(from, field));

        /// <summary>Puts back everything the rig changed. Runs from Start's finally, so a throw mid-run
        /// cannot leave the editor on borrowed settings, the camera parked in a test pose, or the readout
        /// wearing the magenta marker.</summary>
        private void Restore()
        {
            if (hud != null && _prevHudConfig != null) SetHudConfig(hud, _prevHudConfig);
            if (hudPanel != null) hudPanel.color = _prevPanelColor;
            if (hud != null) { hud.enabled = true; hud.gameObject.SetActive(true); }

            if (cam != null)
            {
                cam.transform.position = _prevCamPos;
                cam.transform.rotation = _prevCamRot;
                cam.fieldOfView = _prevCamFov;
            }

            if (chaseCamera != null) chaseCamera.enabled = _prevChaseEnabled;
            if (player != null) player.SetInputSuppressed(false);
            if (photo != null) photo.SetPhotoMode(false);
            if (view != null) view.Close();
            if (manager != null) manager.enabled = true;

            for (int i = 0; i < _tempObjects.Count; i++)
                if (_tempObjects[i] != null) Destroy(_tempObjects[i]);
            _tempObjects.Clear();

            for (int i = 0; i < _tempConfigs.Count; i++)
                if (_tempConfigs[i] != null) Destroy(_tempConfigs[i]);
            _tempConfigs.Clear();

            Time.timeScale = _prevTimeScale;
            Application.runInBackground = _prevRunInBackground;

            // ⚠️ The SCENE ITSELF is not restored here and does not need to be: GradeHudShootRig reopens
            // SampleScene from disk when play mode ends, which is what removes the runner object as well.
        }

        private void Finish()
        {
            _log.AppendLine();
            _log.AppendLine(new string('=', 78));
            _log.AppendLine(_captureTrusted
                ? "Run complete. The capture technique was PROVEN in Phase 0, so the pictures are evidence."
                : "Run complete — but Phase 0 could NOT prove the capture technique. TREAT EVERY IMAGE HERE " +
                  "AS SUSPECT and escalate.");
            _log.AppendLine("The pictures are the evidence; this file is only the index.");

            try
            {
                Directory.CreateDirectory(outputDir);
                File.WriteAllText(Path.Combine(outputDir, "hud.txt"), _log.ToString());
                UnityEngine.Debug.Log($"[GradeHudShoot] Wrote {outputDir}/hud.txt");
            }
            catch (IOException e)
            {
                UnityEngine.Debug.LogError($"[GradeHudShoot] Could not write hud.txt: {e.Message}");
            }

            EditorApplication.isPlaying = false;
        }
    }
}
#endif
