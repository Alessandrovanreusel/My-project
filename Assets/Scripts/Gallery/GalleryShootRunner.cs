#if UNITY_EDITOR
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using CameraGame.Core;
using CameraGame.Events;
using CameraGame.Grading;
using CameraGame.PhotoMode;

namespace CameraGame.Gallery
{
    /// <summary>
    /// Fills a real gallery by pulling the REAL shutter in a private world, then writes every stored
    /// photograph out with its own verdict burned into the picture — so "does the grade match the shot?"
    /// becomes a question you answer by looking, not by trusting two columns in a text file to line up.
    /// Driven by <c>GalleryShootRig</c> (Tools > Gallery > Gallery Shoot).
    ///
    /// This is NOT a test. It asserts nothing and cannot pass or fail. It runs the real path end to end —
    /// real EventManager, real pooled EventActor with its Animator and NavMeshAgent live, real
    /// <c>PhotoModeController.Capture()</c>, real ShotGrader, real GalleryService — and produces evidence
    /// to be judged.
    ///
    /// ⚠️ WHY THIS LIVES IN THE RUNTIME ASSEMBLY, WRAPPED IN #if UNITY_EDITOR: Unity cannot resolve an
    /// editor-assembly MonoBehaviour attached to a scene object when entering play mode — the component
    /// comes back as "The referenced script (Unknown) is missing!" and the rig silently does nothing while
    /// the scene runs on around it. Paid for once already by PhotoShootRunner; not paying for it twice.
    ///
    /// ⚠️ THE SHOOT CAMERA HAS A RENDER TARGET BOUND FOR THE WHOLE RUN, exactly as PhotoShootRunner does.
    /// That is not incidental here — it IS the regression test for the target-texture save/restore inside
    /// <see cref="GalleryService"/>. A gallery that clobbers <c>cam.targetTexture</c> would break the one
    /// tool this project verifies grading with, so every capture below re-checks that the binding survived.
    /// </summary>
    public class GalleryShootRunner : MonoBehaviour
    {
        [HideInInspector] public PhotoModeController photo;
        [HideInInspector] public EventManager manager;
        [HideInInspector] public Camera cam;
        [HideInInspector] public CameraConfig cameraConfig;
        [HideInInspector] public GradingConfig gradingConfig;
        [HideInInspector] public GalleryConfig shippedConfig;
        [HideInInspector] public ShotCapturedChannel channel;
        [HideInInspector] public GalleryService service;
        [HideInInspector] public GalleryView view;
        [HideInInspector] public GalleryInput galleryInput;
        [HideInInspector] public GameObject playerObject;
        [HideInInspector] public string outputDir = "_bmad-output/verification/gallery";

        /// <summary>Size the shoot camera renders at. Deliberately NOT the thumbnail size: the gallery
        /// takes its own, smaller picture from a camera rendering at some other resolution, which is the
        /// arrangement the real game is in.</summary>
        // ⚠️ AND DELIBERATELY NOT 16:9. The gallery derives a thumbnail's width from the camera's aspect, so
        // shooting at 16:9 would make a broken derivation indistinguishable from a working one — the
        // thumbnail would come out 480x270 either way. 960x490 is 1.959, close to the real Game View, so a
        // stored picture that is 480x270 rather than ~529x270 is visibly wrong in the log.
        private const int ShotWidth = 960;
        private const int ShotHeight = 490;

        /// <summary>Contact-sheet page size: the thumbnail scaled up, plus a caption band under it.</summary>
        private const int SheetWidth = 720;
        private const int SheetHeight = 480;

        /// <summary>Picture pane height, derived from the shoot aspect rather than hard-coded, so the
        /// contact sheet never stretches the very framing it exists to show.</summary>
        private const int SheetPictureHeight = SheetWidth * ShotHeight / ShotWidth;

        /// <summary>The UI layer. The contact sheet lives here and the shoot camera is masked OFF it, so
        /// the sheet can never leak into a photograph of the world.</summary>
        private const int ContactLayer = 5;

        private readonly StringBuilder _log = new StringBuilder();

        private GameObject _wall;
        private RenderTexture _rt;
        private RenderTexture _sheetRt;
        private Camera _sheetCam;
        private RawImage _sheetPicture;
        private Text _sheetCaption;

        // Everything below is recorded BEFORE it is mutated and put back in Restore(). A rig is scaffolding;
        // leaving the editor on borrowed settings is how the next story gets developed against values
        // nobody chose.
        private bool _prevRunInBackground;
        private RenderTexture _prevTarget;
        private float _prevTimeScale;
        private int _prevCullingMask;

        private int _sheetPage;

        // Every throwaway config the rig makes, destroyed together in Restore(). Destroying one the moment
        // its scenario ends would leave the live service holding a destroyed ScriptableObject for the gap
        // before the next rebuild — a state where `== null` is true while the fields still read fine, which
        // is the exact trap the photo-shoot rig documents about assets loaded before a scene swap.
        private readonly List<GalleryConfig> _tempConfigs = new List<GalleryConfig>();

        private IEnumerator Start()
        {
            _prevRunInBackground = Application.runInBackground;
            _prevTimeScale = Time.timeScale;
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

            _prevTarget = cam.targetTexture;
            _prevCullingMask = cam.cullingMask;
            _rt = new RenderTexture(ShotWidth, ShotHeight, 24, RenderTextureFormat.ARGB32);
            cam.targetTexture = _rt;

            // Offer the same target to the editor-side recorder — see RigVideoFeed for why this is a
            // publish rather than a direct call. Withdrawn in Restore().
            RigVideoFeed.Publish(_rt, outputDir, "gallery-shoot");

            // Mask the shoot camera off the contact-sheet layer. Without this the sheet's own canvas could
            // appear inside a photograph of the world, and a rig whose evidence contains the rig is worthless.
            cam.cullingMask &= ~(1 << ContactLayer);

            BuildContactSheet();

            _wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _wall.name = "Wall";
            _wall.SetActive(false);

            Header();

            // The rig builds the service and the view in edit mode so they Awake fully wired. This is the
            // fallback for a hand-started runner, and it is deliberately loud: a silently self-built gallery
            // would mean the phases below were testing something other than what the rig assembled.
            if (service == null || view == null)
            {
                _log.AppendLine("NOTE: the rig supplied no gallery — one was built from the shipped config.");
                _log.AppendLine();
                yield return RebuildService(shippedConfig, cam, channel);
            }

            yield return PhaseA_ARollOfFilm();
            yield return PhaseB_PhotographTheGalleryItself();
            yield return PhaseC_EvictionAndTextureCount();
            yield return PhaseC2_AFullGalleryOnScreen();
            yield return PhaseD_Boundaries();
            yield return PhaseE_MeasureTheShutter();
            yield return PhaseF_PooledRespawnIdentity();
            yield return PhaseG_RealInputPath();
        }

        private void Header()
        {
            _log.AppendLine("GALLERY SHOOT — real shutter, real grader, real GalleryService.");
            _log.AppendLine("Nothing here asserts anything. These are photographs and measurements to be judged.");
            _log.AppendLine();
            _log.AppendLine($"Shoot camera renders {ShotWidth}x{ShotHeight}; the gallery takes its own, smaller");
            _log.AppendLine("picture from the same camera on the same frame — that difference is deliberate.");
            _log.AppendLine();

            if (shippedConfig == null)
            {
                _log.AppendLine("GALLERY CONFIG: NONE WIRED — Phase A is running with no config at all. (!)");
            }
            else
            {
                // Read back from the LIVE asset. Adding a field to a ScriptableObject class does NOT add it
                // to an existing asset, and this project hand-authors those assets as YAML — so a mistyped
                // or renamed key silently reads as 0, and a zero cap is a gallery that stores nothing while
                // the console stays clean. Printing them beside the results makes that visible, not inferred.
                _log.AppendLine("GALLERY CONFIG read back from the live asset:");
                _log.AppendLine($"    height       {shippedConfig.SafeThumbnailHeight}px" +
                                $"   (raw field: {shippedConfig.thumbnailHeight})");
                _log.AppendLine($"    width        derived from the window; ceiling " +
                                $"{shippedConfig.SafeMaxThumbnailWidth}px (raw field: {shippedConfig.maxThumbnailWidth})");
                _log.AppendLine($"    at this shoot's aspect {ShotWidth / (float)ShotHeight:0.###} that is " +
                                $"{shippedConfig.ThumbnailWidthFor(ShotWidth / (float)ShotHeight)}" +
                                $"x{shippedConfig.SafeThumbnailHeight}" +
                                (shippedConfig.WouldClampWidth(ShotWidth / (float)ShotHeight)
                                    ? "   (CLAMPED - stored shots will not match the graded frame)"
                                    : "   (not clamped)"));
                _log.AppendLine($"    maxStoredShots {shippedConfig.SafeMaxStoredShots}" +
                                $"   (raw field: {shippedConfig.maxStoredShots})");
                _log.AppendLine($"    budget       {shippedConfig.DescribeBudget()}");
                if (shippedConfig.TryGetConfigProblem(out string problem))
                    _log.AppendLine($"    ⚠ PROBLEM: {problem}");
            }

            if (gradingConfig != null)
            {
                gradingConfig.ResolveTimingWindow(out float full, out float zero);
                _log.AppendLine($"GRADING: gate {gradingConfig.SafeMinSubjectHeight:0.###} frame-height; " +
                                $"timing full ±{full:0.##}s, zero ±{zero:0.##}s.");
            }

            _log.AppendLine();
        }

        // =====================================================================
        // PHASE A — take a real roll of film and look at every frame of it
        // =====================================================================

        /// <summary>One vantage point. No "expected" field: the photograph is the result.</summary>
        private readonly struct Shot
        {
            public readonly string Name;
            public readonly float Distance;    // in subject heights
            public readonly float YawOffset;   // degrees off the line to the subject; 180 = looking away
            public readonly bool Zoom;
            public readonly bool Wall;
            public readonly float TargetX, TargetY;
            public readonly string Intent;

            public Shot(string name, float distance, string intent, float yaw = 0f, bool zoom = false,
                        bool wall = false, float targetX = 0.5f, float targetY = 0.5f)
            {
                Name = name; Distance = distance; Intent = intent; YawOffset = yaw;
                Zoom = zoom; Wall = wall; TargetX = targetX; TargetY = targetY;
            }
        }

        // Chosen so the roll contains COUNTED shots, WEAK shots and at least one MISS of more than one kind —
        // because the thing AC2 is really about is whether those three read differently in the gallery.
        private static readonly Shot[] RollOfFilm =
        {
            new Shot("a_portrait",     2.2f, "A few paces back, dead centre — the shot the game is about."),
            new Shot("b_close",        1.3f, "Standing right in front of him."),
            new Shot("c_far_zoomed",   6f,   "Across the street, zoomed all the way in.", zoom: true),
            new Shot("d_corner",       2.2f, "Same distance, shoved into the corner of the frame.",
                                             targetX: 0.12f, targetY: 0.85f),
            new Shot("e_too_far",      16f,  "Far down the road — should fail the size gate."),
            // ⚠️ `wall: true` is load-bearing and was MISSING on the first run (2026-07-30). The pose still
            // produced a perfectly plausible line — "counted, visible 100%" — under a caption promising a
            // wall, and it was within one sentence of being written up as a reproduction of Story 1.9's
            // deferred "occlusion gate appears inert" finding. The photograph is what caught it: there was
            // no wall in the picture. A caption that asserts what the code did not do is worse than no
            // caption at all.
            new Shot("f_behind_wall",  1.2f, "A wall dropped between the camera and him.", wall: true),
            new Shot("g_looking_away", 2f,   "Close, but facing the opposite way.", yaw: 180f),
        };

        private IEnumerator PhaseA_ARollOfFilm()
        {
            Section("PHASE A — a real roll of film");
            _log.AppendLine("Every capture below is photo.Capture() — the same method the player's shutter");
            _log.AppendLine("calls. Each stored shot is then written out as a contact-sheet page with the");
            _log.AppendLine("gallery's OWN record printed beside the gallery's OWN picture.");
            _log.AppendLine();

            service.ClearAll();

            // The empty world goes FIRST, while the manager is held off. Waiting for an empty street later
            // never works — the manager respawns shortly after each despawn.
            manager.enabled = false;
            cam.transform.position = new Vector3(0f, 8f, 30f);
            cam.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);
            photo.SetPhotoMode(true);
            photo.SetZoom(0f);
            yield return null;
            yield return null;

            int liveActors = manager.ActiveActors.Count;
            photo.Capture();
            yield return new WaitForEndOfFrame();
            _log.AppendLine($"h_nobody       —  Shutter pressed with nobody in the world at all.");
            _log.AppendLine($"                  live actors at shutter: {liveActors}");
            NoteTargetTextureIntact("h_nobody");

            manager.enabled = true;

            float waited = 0f;
            while (manager.ActiveActors.Count == 0 && waited < 40f) { waited += Time.deltaTime; yield return null; }
            if (manager.ActiveActors.Count == 0)
            {
                _log.AppendLine("No actor ever spawned — the rest of Phase A cannot run.");
                _log.AppendLine();
                yield break;
            }

            foreach (Shot s in RollOfFilm)
            {
                yield return TakeShot(s);
                yield return new WaitForSecondsRealtime(1.2f);
            }

            _log.AppendLine();
            _log.AppendLine($"The gallery now holds {service.Shots.Count} shot(s). Writing the contact sheet…");
            _log.AppendLine();

            yield return WriteContactSheet("roll");
            DescribeGallery("after the roll of film");
        }

        private IEnumerator TakeShot(Shot s)
        {
            if (!TryGetActor(out EventActor actor))
            {
                float w = 0f;
                while (!TryGetActor(out actor) && w < 40f) { w += Time.deltaTime; yield return null; }
                if (actor == null)
                {
                    _log.AppendLine($"{s.Name}  —  SKIPPED: nobody in the world to photograph.");
                    yield break;
                }
            }

            PlaceCamera(s, actor.Bounds);

            photo.SetPhotoMode(true);
            yield return null;
            photo.SetZoom(s.Zoom ? 1f : 0f);

            // Wait for the FOV to ARRIVE. Update eases toward the zoom target, so firing two frames after
            // asking for full zoom catches it mid-travel and quietly tests something other than full zoom.
            float target = cameraConfig != null ? (s.Zoom ? cameraConfig.teleFov : cameraConfig.wideFov) : 60f;
            float settled = 0f;
            while (Mathf.Abs(cam.fieldOfView - target) > 0.25f && settled < 4f)
            {
                settled += Time.deltaTime;
                yield return null;
            }

            // Re-read the actor from the manager rather than reusing the reference from before the yields.
            // Actors are POOLED: a held reference does not go null when its event ends, it quietly becomes a
            // different event. The settle loop above can wait four seconds.
            if (!TryGetActor(out actor))
            {
                _log.AppendLine($"{s.Name}  —  SKIPPED: he despawned mid-pose (pooled reuse).");
                yield break;
            }

            PlaceCamera(s, actor.Bounds, aimOnly: true);
            yield return null;

            string liveId = actor.SubjectId;
            int before = service.Shots.Count;

            photo.Capture();                       // the real shutter
            yield return new WaitForEndOfFrame();

            int after = service.Shots.Count;
            _log.AppendLine($"{s.Name}  —  {s.Intent}");
            _log.AppendLine($"    grade at shutter: {photo.LastGrade}");
            _log.AppendLine($"    detail:           {photo.LastDetail}");
            _log.AppendLine($"    live subject id at shutter: '{liveId}'");
            _log.AppendLine($"    gallery: {before} -> {after} stored");

            if (after > before)
            {
                CapturedShot stored = service.Shots[after - 1];
                _log.AppendLine($"    stored: {stored}");

                // The width should follow the window. Printed as a comparison rather than a bare size, so
                // "it derived correctly" is something you can read instead of something you assume.
                if (stored.HasImage)
                {
                    float camAspect = cam.aspect;
                    float imgAspect = stored.Image.width / (float)stored.Image.height;
                    _log.AppendLine($"    aspect: camera {camAspect:0.###} vs stored image {imgAspect:0.###}" +
                                    $" ({stored.Image.width}x{stored.Image.height})" +
                                    (Mathf.Abs(camAspect - imgAspect) <= 0.01f * camAspect
                                        ? "  MATCH"
                                        : "  ⚠ MISMATCH - the stored picture is not the graded frame"));
                }

                // The one cross-check worth making in text: the gallery recorded the id the LIVE actor was
                // reporting at the shutter, not one cached from an earlier frame or an earlier lifecycle.
                if (stored.SubjectId != liveId && photo.LastGrade.HasSubject)
                    _log.AppendLine($"    ⚠ SUBJECT ID DISAGREES with the live actor ('{stored.SubjectId}' vs '{liveId}').");
            }

            NoteTargetTextureIntact(s.Name);
            _log.AppendLine();
        }

        // =====================================================================
        // PHASE B — photograph the gallery itself, and prove the gating
        // =====================================================================

        private IEnumerator PhaseB_PhotographTheGalleryItself()
        {
            Section("PHASE B — the gallery as the player sees it");
            _log.AppendLine("AC2 asks whether the player can open the gallery and read their shots. That is a");
            _log.AppendLine("question about a picture, so this takes one. The gallery canvas is Screen Space -");
            _log.AppendLine("Camera precisely so cam.Render() can see it; an Overlay canvas could not be");
            _log.AppendLine("photographed by any rig at all.");
            _log.AppendLine();

            // --- It must NOT open while the camera is raised ------------------------------------------
            photo.SetPhotoMode(true);
            yield return null;
            view.Toggle();
            yield return null;
            _log.AppendLine($"Tab pressed with the camera RAISED  ->  gallery open = {view.IsOpen}   (AC2 wants false)");

            photo.SetPhotoMode(false);
            yield return null;

            // --- It opens in Walk ----------------------------------------------------------------------
            view.Toggle();
            yield return null;
            yield return null;
            _log.AppendLine($"Tab pressed in WALK                 ->  gallery open = {view.IsOpen}   (AC2 wants true)");

            SaveCameraFrame(Path.Combine(outputDir, "ui_open.png"));
            _log.AppendLine("    -> ui_open.png : LOOK AT THIS. Are the thumbnails real, different photographs?");
            _log.AppendLine("       Do the star glyphs render, or come back as empty boxes? Can you tell a");
            _log.AppendLine("       MISSED shot from a merely LATE one without reading anything else?");

            // --- While open, the camera cannot be raised and the shutter is inert ----------------------
            int storedBefore = service.Shots.Count;
            photo.SetPhotoMode(true);
            yield return null;
            _log.AppendLine($"RaiseCamera while the gallery is open -> IsPhotoMode = {photo.IsPhotoMode}   (wants false)");

            photo.Capture();
            yield return new WaitForEndOfFrame();
            _log.AppendLine($"Capture while the gallery is open     -> gallery {storedBefore} -> " +
                            $"{service.Shots.Count} stored   (wants no change)");

            float fovBefore = cam.fieldOfView;
            photo.SetZoom(1f);
            yield return null;
            yield return null;
            _log.AppendLine($"Zoom while the gallery is open        -> fov {fovBefore:0.0}° -> {cam.fieldOfView:0.0}°" +
                            "   (wants no zoom-in; Walk always targets wide)");

            // --- Close, and prove the camera comes back ------------------------------------------------
            view.Toggle();
            yield return null;
            SaveCameraFrame(Path.Combine(outputDir, "ui_closed.png"));
            _log.AppendLine($"Tab again                             -> gallery open = {view.IsOpen}   (wants false)");

            photo.SetPhotoMode(true);
            yield return null;
            _log.AppendLine($"RaiseCamera after closing             -> IsPhotoMode = {photo.IsPhotoMode}   (wants true)");
            photo.SetPhotoMode(false);
            yield return null;

            _log.AppendLine("    -> ui_closed.png : the same frame with the gallery shut. If the gallery is");
            _log.AppendLine("       visible in THIS one, the close path is not actually hiding the canvas.");
            _log.AppendLine();
        }

        // =====================================================================
        // PHASE C — eviction, and whether the memory actually goes back
        // =====================================================================

        private IEnumerator PhaseC_EvictionAndTextureCount()
        {
            Section("PHASE C — eviction under a small cap, measured not asserted");
            _log.AppendLine("This project's bugs live in REUSE and in the second cycle onward, never the first,");
            _log.AppendLine("so the gallery is filled well past its cap several times over. The texture count is");
            _log.AppendLine("the evidence: it must go FLAT once the cap is reached, not keep climbing. A count");
            _log.AppendLine("that climbs means the pictures are being dereferenced rather than destroyed, which");
            _log.AppendLine("is the NFR3 leak this story exists to avoid — and it is invisible in every log.");
            _log.AppendLine();

            const int Cap = 5;
            const int Rounds = 3;
            const int PerRound = 12;

            // An IN-MEMORY config, never the shipped asset. Mutating Assets/Data/Gallery/GalleryConfig.asset
            // would leave the project on values nobody chose if this run threw — so the rig simply never
            // touches it, and there is nothing to restore.
            GalleryConfig tight = MakeConfig(480, 270, Cap);

            yield return RebuildService(tight, cam, channel);

            // Park the camera on the subject so every capture is a real render of a real frame.
            if (TryGetActor(out EventActor actor))
                PlaceCamera(new Shot("stress", 2.2f, ""), actor.Bounds);

            photo.SetPhotoMode(true);
            yield return null;

            int baseline = CountTextures();
            _log.AppendLine($"live Texture2D count before any capture: {baseline}");
            _log.AppendLine();

            for (int round = 1; round <= Rounds; round++)
            {
                for (int i = 0; i < PerRound; i++)
                {
                    if (TryGetActor(out EventActor a)) PlaceCamera(new Shot("stress", 2.2f, ""), a.Bounds, aimOnly: true);
                    photo.Capture();
                    yield return null;

                    // Destroy is DEFERRED to the end of the frame, so counting in the same frame as the
                    // eviction would show the texture still alive and read as a leak that is not there.
                    // (A rig that reports a false leak is worse than one that reports none.)
                    yield return new WaitForEndOfFrame();

                    if (i == Cap - 1 || i == PerRound - 1)
                        _log.AppendLine($"  round {round}, capture {i + 1,2}: stored {service.Shots.Count}/{Cap}" +
                                        $"   live Texture2D {CountTextures()}  (+{CountTextures() - baseline} vs baseline)");
                }
            }

            _log.AppendLine();
            _log.AppendLine($"After {Rounds * PerRound} captures at a cap of {Cap}: " +
                            $"stored {service.Shots.Count}, live Texture2D {CountTextures()} " +
                            $"(+{CountTextures() - baseline} vs baseline).");
            _log.AppendLine("READ THIS AS: the +N figure must be roughly the cap, and must be the SAME at the end");
            _log.AppendLine("of round 3 as at the end of round 1. If it grew by ~36, every evicted picture leaked.");
            _log.AppendLine();

            DescribeGallery("after the eviction stress");

            // Prove the OLDEST went, not an arbitrary one: ids are monotonic, so the survivors must be the
            // last Cap ids issued, in order.
            _log.AppendLine("Surviving ids (oldest first) — these must be the LAST ids issued, contiguous:");
            for (int i = 0; i < service.Shots.Count; i++)
                _log.AppendLine($"    {service.Shots[i].Id}   image {(service.Shots[i].HasImage ? "present" : "MISSING")}");
            _log.AppendLine();

            yield return WriteContactSheet("evicted");
        }

        // =====================================================================
        // PHASE C2 — what a BUSY gallery actually looks like
        // =====================================================================

        /// <summary>
        /// Photographs the gallery holding a realistic number of shots, and again holding a full one.
        ///
        /// ⚠️ THIS PHASE EXISTS BECAUSE EIGHT SHOTS HID A DEFECT. The first run photographed the gallery
        /// with the eight shots of Phase A, and the newest row was already sliced off by the bottom of the
        /// screen — which was easy to read as a cosmetic margin problem. It was not: the grid flowed as many
        /// rows as it liked, so the failure got *worse* the more photographs the player had taken, and at
        /// the shipped cap of fifty they would have seen about six of them with nothing saying so. A bench
        /// that only ever tests the small case cannot see that shape of bug.
        /// </summary>
        private IEnumerator PhaseC2_AFullGalleryOnScreen()
        {
            Section("PHASE C2 — the gallery with a realistic number of shots in it");
            _log.AppendLine("Two pictures: a busy gallery and a completely full one. The question both are");
            _log.AppendLine("asked is the same — is every shot the header CLAIMS to be showing actually on");
            _log.AppendLine("screen, and is anything hidden admitted to rather than silently dropped?");
            _log.AppendLine();

            yield return RebuildService(shippedConfig, cam, channel);

            photo.SetPhotoMode(true);
            if (TryGetActor(out EventActor a0)) PlaceCamera(new Shot("full", 2.2f, ""), a0.Bounds);
            yield return null;

            foreach (int target in new[] { 30, shippedConfig != null ? shippedConfig.SafeMaxStoredShots : 50 })
            {
                while (service.Shots.Count < target)
                {
                    if (TryGetActor(out EventActor a)) PlaceCamera(new Shot("full", 2.2f, ""), a.Bounds, aimOnly: true);
                    photo.Capture();
                    yield return null;
                }

                photo.SetPhotoMode(false);
                yield return null;
                view.Close();
                view.Toggle();
                yield return null;
                yield return null;

                string file = $"ui_open_{service.Shots.Count:D2}shots.png";
                SaveCameraFrame(Path.Combine(outputDir, file));
                _log.AppendLine($"  {service.Shots.Count} shots stored -> {file}");
                _log.AppendLine($"     header reads: \"{ReadHeader()}\"");
                _log.AppendLine($"     cells actually enabled on screen: {CountVisibleCells()}");

                view.Toggle();
                yield return null;
                photo.SetPhotoMode(true);
                yield return null;
            }

            _log.AppendLine();
            _log.AppendLine("  LOOK AT THESE. Every enabled cell must be fully inside the frame — no row");
            _log.AppendLine("  clipped by the bottom edge — and the count in the header must match the number");
            _log.AppendLine("  of cells you can actually see.");
            _log.AppendLine();
        }

        /// <summary>Reads the gallery's own header text back, so the log records what the PLAYER was told
        /// rather than what the rig assumed they were told.</summary>
        private string ReadHeader()
        {
            foreach (Text t in view.GetComponentsInChildren<Text>(includeInactive: true))
                if (t.transform.parent == view.transform && t.text.StartsWith("GALLERY")) return t.text;
            return "<no header found>";
        }

        /// <summary>Counts the cells the view actually turned on — the honest denominator for "is the
        /// header telling the truth?".</summary>
        private int CountVisibleCells()
        {
            int n = 0;
            foreach (RawImage img in view.GetComponentsInChildren<RawImage>(includeInactive: true))
                if (img.gameObject.activeInHierarchy) n++;
            return n;
        }

        // =====================================================================
        // PHASE D — push every boundary and confirm each fails SOFT
        // =====================================================================

        private IEnumerator PhaseD_Boundaries()
        {
            Section("PHASE D — boundaries: zero, negative, huge, and absent");
            _log.AppendLine("Every tunable at its extremes, and every reference missing in turn. What is being");
            _log.AppendLine("checked is not that these 'work' — it is that each disables ONLY its own function,");
            _log.AppendLine("logs once, and leaves capture and grading running (NFR8, AC5).");
            _log.AppendLine();

            yield return Boundary("thumbnail 0x0", MakeConfig(0, 0, 10), cam, channel);
            yield return Boundary("thumbnail negative", MakeConfig(-64, -64, 10), cam, channel);
            yield return Boundary("thumbnail huge (4096)", MakeConfig(4096, 4096, 10), cam, channel);

            // The failure mode that only exists now the width is derived: a ceiling too narrow for the
            // window clamps every thumbnail, so no stored picture matches the frame it was graded in.
            yield return Boundary("width ceiling below height (64 wide, 270 tall)",
                                  MakeConfig(64, 270, 10), cam, channel);
            yield return Boundary("width ceiling exactly 16:9 on a 1.96 window",
                                  MakeConfig(480, 270, 10), cam, channel);
            yield return Boundary("maxStoredShots 0", MakeConfig(480, 270, 0), cam, channel);
            yield return Boundary("maxStoredShots negative", MakeConfig(480, 270, -5), cam, channel);
            yield return Boundary("maxStoredShots huge (100000)", MakeConfig(480, 270, 100000), cam, channel);
            yield return Boundary("config ASSET MISSING", null, cam, channel);
            yield return Boundary("camera UNASSIGNED", MakeConfig(480, 270, 10), null, channel);
            yield return Boundary("channel UNASSIGNED", MakeConfig(480, 270, 10), cam, null);

            // The view's own missing-reference paths, which are separate from the service's.
            _log.AppendLine("view UNASSIGNED on the input adapter:");
            var orphanGo = new GameObject("OrphanGalleryInput");
            orphanGo.SetActive(false);
            orphanGo.AddComponent<GalleryInput>();      // no view assigned
            orphanGo.SetActive(true);                   // Awake logs once, then the key is inert
            yield return null;
            _log.AppendLine("    a GalleryInput with no view logged once at Awake and did nothing further.");
            Destroy(orphanGo);
            _log.AppendLine();

            // Put the shipped configuration back for the phases that follow.
            yield return RebuildService(shippedConfig, cam, channel);
            _log.AppendLine("Shipped configuration restored for the remaining phases.");
            _log.AppendLine();
        }

        private GalleryConfig MakeConfig(int maxWidth, int height, int maxShots)
        {
            GalleryConfig c = ScriptableObject.CreateInstance<GalleryConfig>();
            c.maxThumbnailWidth = maxWidth;
            c.thumbnailHeight = height;
            c.maxStoredShots = maxShots;
            _tempConfigs.Add(c);
            return c;
        }

        private IEnumerator Boundary(string label, GalleryConfig cfg, Camera camera, ShotCapturedChannel ch)
        {
            _log.AppendLine($"{label}:");

            if (cfg != null && cfg.TryGetConfigProblem(out string problem))
                _log.AppendLine($"    config reports: {problem}");
            else if (cfg != null)
                _log.AppendLine("    config reports: no problem.");

            yield return RebuildService(cfg, camera, ch);

            photo.SetPhotoMode(true);
            if (TryGetActor(out EventActor a)) PlaceCamera(new Shot("b", 2.2f, ""), a.Bounds);
            yield return null;

            ShotGrade before = photo.LastGrade;
            photo.Capture();
            yield return new WaitForEndOfFrame();

            _log.AppendLine($"    capture still graded: {photo.LastGrade}");
            _log.AppendLine($"    gallery recording: {service.IsRecording}   stored: {service.Shots.Count}" +
                            (service.Shots.Count > 0
                                ? $"   image: {(service.Shots[service.Shots.Count - 1].HasImage ? "present" : "none")}"
                                : string.Empty));
            NoteTargetTextureIntact(label);
            _log.AppendLine();
        }

        // =====================================================================
        // PHASE E — measure the shutter against the 0.2 s budget
        // =====================================================================

        private IEnumerator PhaseE_MeasureTheShutter()
        {
            Section("PHASE E — how long the shutter actually takes (NFR2)");
            _log.AppendLine("Reading pixels back from the GPU is a SYNCHRONISE-AND-STALL operation — the first");
            _log.AppendLine("thing in this project's capture path that is not pure arithmetic. Story 1.10's");
            _log.AppendLine("deferred list flags that the 0.0072 ms grading figure was re-asserted rather than");
            _log.AppendLine("re-measured; this measures instead, with the gallery listening and with it not.");
            _log.AppendLine();

            const int Samples = 40;

            photo.SetPhotoMode(true);
            if (TryGetActor(out EventActor a)) PlaceCamera(new Shot("m", 2.2f, ""), a.Bounds);
            yield return null;

            // WITH the gallery listening.
            yield return MeasureCaptures("gallery listening", Samples);

            // WITHOUT: disabling the component unsubscribes in OnDisable, which is also a direct test that
            // a disabled GalleryService leaves no live delegate on the channel asset (AC1).
            service.enabled = false;
            yield return null;
            int frozen = service.Shots.Count;

            yield return MeasureCaptures("gallery disabled", Samples);

            _log.AppendLine($"    while disabled the gallery stored {service.Shots.Count - frozen} further shots " +
                            "(wants 0 — a disabled service must leave no live delegate on the channel).");

            service.enabled = true;
            yield return null;
            _log.AppendLine();
        }

        private IEnumerator MeasureCaptures(string label, int samples)
        {
            var sw = new Stopwatch();
            double first = 0d, total = 0d, worst = 0d;

            for (int i = 0; i < samples; i++)
            {
                if (TryGetActor(out EventActor a)) PlaceCamera(new Shot("m", 2.2f, ""), a.Bounds, aimOnly: true);
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
            _log.AppendLine($"  {label,-18}  first {first:0.000} ms   mean {mean:0.000} ms   worst {worst:0.000} ms" +
                            $"   (budget 200 ms → {(worst > 0 ? 200.0 / worst : 0):0} x margin at the worst sample)");
        }

        // =====================================================================
        // PHASE F — pooled reuse: is the recorded subject the LIVE one?
        // =====================================================================

        private IEnumerator PhaseF_PooledRespawnIdentity()
        {
            Section("PHASE F — across pooled respawns, is the recorded subject the live one?");
            _log.AppendLine("A pooled reference does NOT go null when its event ends — it quietly becomes a");
            _log.AppendLine("different event (ISubject's liveness contract). So the question is not whether the");
            _log.AppendLine("id is non-empty, it is whether it is THIS lifecycle's id. Time scale is raised for");
            _log.AppendLine("this phase only, and put back in Restore().");
            _log.AppendLine();

            service.ClearAll();
            Time.timeScale = 4f;

            photo.SetPhotoMode(true);

            for (int cycle = 1; cycle <= 3; cycle++)
            {
                // Wait for the street to empty, then for the next lifecycle to begin. That transition is the
                // pooled reuse — the same instance coming back as a new event.
                float t = 0f;
                while (TryGetActor(out _) && t < 60f) { t += Time.unscaledDeltaTime; yield return null; }

                t = 0f;
                EventActor actor = null;
                while (!TryGetActor(out actor) && t < 60f) { t += Time.unscaledDeltaTime; yield return null; }
                if (actor == null)
                {
                    _log.AppendLine($"  cycle {cycle}: no new lifecycle within 60 s — phase cut short.");
                    break;
                }

                PlaceCamera(new Shot("p", 2.2f, ""), actor.Bounds);
                yield return null;

                if (!TryGetActor(out actor))
                {
                    _log.AppendLine($"  cycle {cycle}: he went again before the shutter — skipped.");
                    continue;
                }

                PlaceCamera(new Shot("p", 2.2f, ""), actor.Bounds, aimOnly: true);
                string liveId = actor.SubjectId;
                int instance = actor.GetInstanceID();

                photo.Capture();
                yield return new WaitForEndOfFrame();

                CapturedShot stored = service.Shots[service.Shots.Count - 1];
                bool agrees = stored.SubjectId == liveId;
                _log.AppendLine($"  cycle {cycle}: live actor instance {instance}, id '{liveId}'  ->  " +
                                $"stored id '{stored.SubjectId}'  {(agrees ? "agrees" : "⚠ DISAGREES")}");
                _log.AppendLine($"            {stored}");
            }

            Time.timeScale = _prevTimeScale;
            _log.AppendLine();
            _log.AppendLine("The instance id repeating across cycles is the pooling working as designed — what");
            _log.AppendLine("matters is that each stored shot names the lifecycle that was live at ITS shutter.");
            _log.AppendLine();

            yield return WriteContactSheet("respawn");
        }

        // =====================================================================
        // PHASE G — the real Send-Messages input path
        // =====================================================================

        private IEnumerator PhaseG_RealInputPath()
        {
            Section("PHASE G — does the Tab key actually reach OnGallery?");
            _log.AppendLine("Everything above drives GalleryView.Toggle() directly, exactly as the photo-shoot");
            _log.AppendLine("rig drives Capture(). That proves the behaviour but NOT the wiring, and the wiring");
            _log.AppendLine("is a documented silent-failure mode: under Send Messages, PlayerInput calls OnXxx");
            _log.AppendLine("only on its own GameObject, so a handler on the wrong object never fires and the");
            _log.AppendLine("console stays clean. This presses a real key through a real PlayerInput instead.");
            _log.AppendLine();

            if (galleryInput == null || playerObject == null)
            {
                _log.AppendLine("  No PlayerInput rig was built — phase skipped.");
                _log.AppendLine();
                yield break;
            }

            var playerInput = playerObject.GetComponent<PlayerInput>();
            if (playerInput == null)
            {
                _log.AppendLine("  The player object carries no PlayerInput — phase skipped.");
                _log.AppendLine();
                yield break;
            }

            _log.AppendLine($"  PlayerInput: map '{playerInput.currentActionMap?.name}', " +
                            $"notifications {playerInput.notificationBehavior}");
            _log.AppendLine($"  GalleryInput is on '{galleryInput.gameObject.name}'; PlayerInput is on " +
                            $"'{playerInput.gameObject.name}'  " +
                            $"{(galleryInput.gameObject == playerInput.gameObject ? "(same object — required)" : "(⚠ DIFFERENT OBJECTS — OnGallery can never fire)")}");

            Keyboard keyboard = Keyboard.current ?? InputSystem.AddDevice<Keyboard>();

            photo.SetPhotoMode(false);
            view.Close();
            yield return null;

            bool before = view.IsOpen;
            yield return PressKey(keyboard, Key.Tab);
            bool afterPress = view.IsOpen;

            yield return PressKey(keyboard, Key.Tab);
            bool afterSecond = view.IsOpen;

            _log.AppendLine($"  Tab (real key event): open {before} -> {afterPress} -> {afterSecond}");
            _log.AppendLine("  A Button action delivers exactly ONE call per press, so this must read");
            _log.AppendLine("  false -> true -> false. false -> false -> false means the action, the binding or");
            _log.AppendLine("  the handler's GameObject is wrong; true -> true would mean it was typed as Value");
            _log.AppendLine("  and fired on the release as well, toggling twice inside one tap.");
            _log.AppendLine();

            view.Close();
            yield return null;
        }

        private IEnumerator PressKey(Keyboard keyboard, Key key)
        {
            using (StateEvent.From(keyboard, out var eventPtr))
            {
                keyboard[key].WriteValueIntoEvent(1f, eventPtr);
                InputSystem.QueueEvent(eventPtr);
            }
            InputSystem.Update();
            yield return null;
            yield return null;

            using (StateEvent.From(keyboard, out var upPtr))
            {
                keyboard[key].WriteValueIntoEvent(0f, upPtr);
                InputSystem.QueueEvent(upPtr);
            }
            InputSystem.Update();
            yield return null;
            yield return null;
        }

        // =====================================================================
        // THE CONTACT SHEET — the gallery's own picture beside its own record
        // =====================================================================

        /// <summary>
        /// Builds a private canvas that draws one stored shot at a time — the picture as a
        /// <c>RawImage</c> (the same component the real gallery cell uses) with the gallery's own record
        /// underneath — rendered by its own camera into its own render target.
        ///
        /// Why a canvas rather than plotting pixels: the caption has to be READ, and a hand-rolled bitmap
        /// font is both a lot of code and a thing that can be subtly wrong in a way that makes the evidence
        /// wrong. This reuses the engine's text rendering, and as a side effect it exercises the very
        /// RawImage-takes-a-Texture2D path the gallery cell depends on.
        ///
        /// The sheet lives on the UI layer and the shoot camera is masked off that layer, so the rig can
        /// never photograph itself.
        /// </summary>
        private void BuildContactSheet()
        {
            _sheetRt = new RenderTexture(SheetWidth, SheetHeight, 24, RenderTextureFormat.ARGB32);

            var camGo = new GameObject("ContactCamera") { layer = ContactLayer };
            _sheetCam = camGo.AddComponent<Camera>();
            _sheetCam.clearFlags = CameraClearFlags.SolidColor;
            _sheetCam.backgroundColor = new Color(0.06f, 0.06f, 0.08f);
            _sheetCam.cullingMask = 1 << ContactLayer;
            _sheetCam.targetTexture = _sheetRt;
            _sheetCam.nearClipPlane = 0.1f;
            _sheetCam.transform.position = new Vector3(0f, -5000f, 0f);   // far from the world it must not see
            _sheetCam.enabled = false;                                    // rendered only on demand

            var canvasGo = new GameObject("ContactSheet", typeof(Canvas), typeof(CanvasScaler))
            { layer = ContactLayer };
            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _sheetCam;
            canvas.planeDistance = 1f;

            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

            var backdrop = NewSheetObject("Backdrop", canvasGo.transform);
            StretchFull(backdrop.GetComponent<RectTransform>());
            backdrop.AddComponent<Image>().color = new Color(0.06f, 0.06f, 0.08f);

            var pictureGo = NewSheetObject("Picture", canvasGo.transform);
            RectTransform pr = pictureGo.GetComponent<RectTransform>();
            pr.anchorMin = new Vector2(0f, 1f);
            pr.anchorMax = new Vector2(1f, 1f);
            pr.pivot = new Vector2(0.5f, 1f);
            pr.offsetMin = new Vector2(0f, -SheetPictureHeight);
            pr.offsetMax = Vector2.zero;
            _sheetPicture = pictureGo.AddComponent<RawImage>();

            var captionGo = NewSheetObject("Caption", canvasGo.transform);
            RectTransform cr = captionGo.GetComponent<RectTransform>();
            cr.anchorMin = Vector2.zero;
            cr.anchorMax = new Vector2(1f, 0f);
            cr.pivot = new Vector2(0.5f, 0f);
            cr.offsetMin = new Vector2(8f, 4f);
            cr.offsetMax = new Vector2(-8f, SheetHeight - SheetPictureHeight - 4f);

            _sheetCaption = captionGo.AddComponent<Text>();
            _sheetCaption.font = font;
            _sheetCaption.fontSize = 15;
            _sheetCaption.color = new Color(0.95f, 0.95f, 0.9f);
            _sheetCaption.alignment = TextAnchor.UpperLeft;
            _sheetCaption.horizontalOverflow = HorizontalWrapMode.Overflow;
            _sheetCaption.verticalOverflow = VerticalWrapMode.Overflow;
        }

        private static GameObject NewSheetObject(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform)) { layer = ContactLayer };
            go.transform.SetParent(parent, worldPositionStays: false);
            return go;
        }

        private static void StretchFull(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        /// <summary>Writes one PNG per stored shot, with the gallery's own record printed on it.</summary>
        private IEnumerator WriteContactSheet(string prefix)
        {
            IReadOnlyList<CapturedShot> shots = service.Shots;

            for (int i = 0; i < shots.Count; i++)
            {
                CapturedShot shot = shots[i];

                _sheetPicture.texture = shot.Image;
                _sheetPicture.color = shot.HasImage ? Color.white : new Color(0.2f, 0.1f, 0.1f);

                ShotGrade g = shot.Grade;
                string verdict = g.IsPlaceholder ? "NOT GRADED"
                    : g.IsMiss ? $"MISSED - {g.MissReason}"
                    : $"COUNTED - {g.Percent01:P0}";

                // The caption describes what the GALLERY RECORDED. It deliberately states no expectation:
                // Story 1.10's captions were rewritten to describe what the camera did, never what the grade
                // ought to be, after a caption reading "just inside the gate" came back rejected.
                _sheetCaption.text =
                    $"{shot.Id}    {StarsAscii(g.Stars)}  {g.Stars}/5    {verdict}\n" +
                    $"subject: {(shot.HasSubject ? shot.SubjectId : "(none recorded)")}    " +
                    $"taken: {shot.CapturedAtUtc.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture)} UTC\n" +
                    $"picture: {(shot.HasImage ? $"{shot.Image.width}x{shot.Image.height} {shot.Image.format}" : "NONE STORED")}" +
                    $"    axes: composition {g.Composition01:P0} x timing {g.Timing01:P0}, seen {g.Subject01:P0}";

                // Two frames so the canvas has certainly laid out and the Text has a mesh before it renders.
                Canvas.ForceUpdateCanvases();
                yield return null;

                RenderTexture prevActive = RenderTexture.active;
                var page = new Texture2D(SheetWidth, SheetHeight, TextureFormat.RGB24, false);
                try
                {
                    _sheetCam.Render();
                    RenderTexture.active = _sheetRt;
                    page.ReadPixels(new Rect(0f, 0f, SheetWidth, SheetHeight), 0, 0);
                    page.Apply();
                    File.WriteAllBytes(Path.Combine(outputDir, $"{prefix}_{_sheetPage:D2}_{shot.Id}.png"),
                                       page.EncodeToPNG());
                }
                finally
                {
                    RenderTexture.active = prevActive;
                    Destroy(page);
                }

                _sheetPage++;
            }

            _sheetPicture.texture = null;   // never leave the sheet pointing at a texture eviction may destroy
        }

        /// <summary>ASCII stars for the burned-in caption. The REAL glyphs (★/☆) are what the gallery cell
        /// uses; whether those render is answered by ui_open.png, not by guessing here — a caption that
        /// silently drew boxes would make every page unreadable at once.</summary>
        private static string StarsAscii(int stars)
        {
            int filled = Mathf.Clamp(stars, 0, 5);
            return "[" + new string('*', filled) + new string('-', 5 - filled) + "]";
        }

        // =====================================================================
        // HELPERS
        // =====================================================================

        /// <summary>Rebuilds the gallery service and view against a given configuration.
        ///
        /// The GameObject is created INACTIVE, its serialized fields are set, and only then is it enabled —
        /// because <c>Awake</c> resolves the readiness flags once and a field written afterwards would be
        /// read by nothing. Getting this order wrong produces a service that reports "not configured" while
        /// its config field visibly holds an asset.</summary>
        private IEnumerator RebuildService(GalleryConfig cfg, Camera camera, ShotCapturedChannel ch)
        {
            if (view != null)
            {
                // Close BEFORE destroying. An open view holds PhotoModeController's raise suppression on,
                // and destroying it would strand that flag true — the camera could then never be raised
                // again and every later phase would silently photograph nothing.
                view.Close();
                Destroy(view.gameObject);
            }

            if (photo != null) photo.SetRaiseSuppressed(false);

            if (service != null)
            {
                service.ClearAll();
                Destroy(service.gameObject);
            }

            yield return null;   // let OnDisable/OnDestroy run before the replacement subscribes

            var serviceGo = new GameObject("GalleryService");
            serviceGo.SetActive(false);
            var svc = serviceGo.AddComponent<GalleryService>();
            SetPrivate(svc, "shotCapturedChannel", ch);
            SetPrivate(svc, "galleryConfig", cfg);
            SetPrivate(svc, "photoCamera", camera);
            serviceGo.SetActive(true);
            service = svc;

            var viewGo = new GameObject("GalleryCanvas");
            viewGo.SetActive(false);
            var v = viewGo.AddComponent<GalleryView>();
            SetPrivate(v, "service", svc);
            SetPrivate(v, "uiCamera", cam);
            SetPrivate(v, "photoMode", photo);
            viewGo.SetActive(true);
            view = v;

            if (galleryInput != null) SetPrivate(galleryInput, "view", v);

            yield return null;
        }

        private void DescribeGallery(string when)
        {
            _log.AppendLine($"GALLERY CONTENTS {when} — {service.Shots.Count} shot(s):");
            for (int i = 0; i < service.Shots.Count; i++)
                _log.AppendLine($"    [{i}] {service.Shots[i]}");
            _log.AppendLine();
        }

        /// <summary>The direct regression check for Task 4's save/restore. A gallery that leaves the camera
        /// pointed somewhere else breaks the photo-shoot rig, and it would do so silently.</summary>
        private void NoteTargetTextureIntact(string label)
        {
            if (cam.targetTexture != _rt)
                _log.AppendLine($"    ⚠ cam.targetTexture WAS NOT RESTORED after '{label}' — the photo-shoot " +
                                "rig would be broken by this.");
        }

        private static int CountTextures() => Resources.FindObjectsOfTypeAll<Texture2D>().Length;

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

        /// <summary>Positions and aims the camera, putting the subject's centre at the shot's normalized
        /// screen target. Lifted from PhotoShootRunner.PlaceCamera — same geometry, same reasoning.</summary>
        private void PlaceCamera(Shot s, Bounds b, bool aimOnly = false)
        {
            float height = Mathf.Max(0.01f, b.size.y);

            if (!aimOnly)
                cam.transform.position = b.center + Vector3.forward * (s.Distance * height);

            Vector3 camPos = cam.transform.position;
            Vector3 toSubject = b.center - camPos;
            if (toSubject.sqrMagnitude < 1e-6f) toSubject = Vector3.forward;

            float tanV = Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float tanH = tanV * cam.aspect;

            float fx = (s.TargetX - 0.5f) * 2f;
            float fy = (s.TargetY - 0.5f) * 2f;

            // Yawing RIGHT slides the subject LEFT in frame, hence the negation; pitching DOWN slides him up.
            float yawDeg = -Mathf.Atan(fx * tanH) * Mathf.Rad2Deg;
            float pitchDeg = Mathf.Atan(fy * tanV) * Mathf.Rad2Deg;

            cam.transform.rotation = Quaternion.LookRotation(toSubject, Vector3.up)
                                     * Quaternion.Euler(pitchDeg, yawDeg + s.YawOffset, 0f);

            _wall.SetActive(s.Wall);
            if (s.Wall)
            {
                _wall.transform.position = Vector3.Lerp(camPos, b.center, 0.45f);
                _wall.transform.localScale = new Vector3(height * 5f, height * 5f, 0.4f);
                _wall.transform.rotation = Quaternion.LookRotation(b.center - camPos, Vector3.up);
            }

            Physics.SyncTransforms();
        }

        /// <summary>Renders the shoot camera and writes the frame out — used for the pictures OF the gallery
        /// UI, which is the only artefact that can answer "does this read as a gallery?".</summary>
        private void SaveCameraFrame(string path)
        {
            RenderTexture prevActive = RenderTexture.active;
            var tex = new Texture2D(ShotWidth, ShotHeight, TextureFormat.RGB24, false);
            try
            {
                cam.Render();
                RenderTexture.active = _rt;
                tex.ReadPixels(new Rect(0f, 0f, ShotWidth, ShotHeight), 0, 0);
                tex.Apply();
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = prevActive;
                Destroy(tex);
            }
        }

        private void Section(string title)
        {
            _log.AppendLine();
            _log.AppendLine(new string('=', 78));
            _log.AppendLine(title);
            _log.AppendLine(new string('=', 78));
            _log.AppendLine();
        }

        /// <summary>Assigns a [SerializeField] private field by name, so the rig can wire the real
        /// components without widening their API just for a bench.</summary>
        private static void SetPrivate(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                UnityEngine.Debug.LogError($"[GalleryShoot] No serialized field '{field}' on {target.GetType().Name}.");
                return;
            }

            switch (value)
            {
                case int i: p.intValue = i; break;
                case float f: p.floatValue = f; break;
                case Object o: p.objectReferenceValue = o; break;

                // C# type patterns do NOT match null, so a deliberately-absent reference would otherwise
                // fall through to `default:` and report "Unsupported type" — naming the switch instead of
                // the absent asset, on a path this rig deliberately exercises.
                case null: p.objectReferenceValue = null; break;

                default:
                    UnityEngine.Debug.LogError($"[GalleryShoot] Unsupported type '{value.GetType().Name}' for '{field}'.");
                    return;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>Puts back everything the rig changed. Runs from Start's finally, so a throw mid-run
        /// cannot leave the editor on borrowed settings or the camera rendering into a dead target.</summary>
        private void Restore()
        {
            // BEFORE _rt is released below — filming a released RenderTexture reads freed native memory.
            RigVideoFeed.Clear();

            if (cam != null)
            {
                cam.targetTexture = _prevTarget;
                cam.cullingMask = _prevCullingMask;
            }

            if (_sheetCam != null) _sheetCam.targetTexture = null;

            if (_rt != null) { _rt.Release(); Destroy(_rt); _rt = null; }
            if (_sheetRt != null) { _sheetRt.Release(); Destroy(_sheetRt); _sheetRt = null; }

            for (int i = 0; i < _tempConfigs.Count; i++)
                if (_tempConfigs[i] != null) Destroy(_tempConfigs[i]);
            _tempConfigs.Clear();

            Time.timeScale = _prevTimeScale;
            Application.runInBackground = _prevRunInBackground;
        }

        private void Finish()
        {
            _log.AppendLine();
            _log.AppendLine("Run complete. The pictures are the evidence; this file is only the index.");

            try
            {
                Directory.CreateDirectory(outputDir);
                File.WriteAllText(Path.Combine(outputDir, "gallery.txt"), _log.ToString());
                UnityEngine.Debug.Log($"[GalleryShoot] Wrote {outputDir}/gallery.txt");
            }
            catch (IOException e)
            {
                UnityEngine.Debug.LogError($"[GalleryShoot] Could not write gallery.txt: {e.Message}");
            }

            EditorApplication.isPlaying = false;
        }
    }
}
#endif
