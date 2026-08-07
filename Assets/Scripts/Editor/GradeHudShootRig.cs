using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using CameraGame.Events;
using CameraGame.Gallery;
using CameraGame.Grading;
using CameraGame.PhotoMode;
using CameraGame.UI;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// Photographs the grade-feedback HUD (Story 1.12) by pulling the REAL shutter and grabbing the REAL
    /// screen, then writes the pictures out to be looked at.
    ///
    /// Tools > HUD > Grade HUD Shoot (Play). Output: _bmad-output/verification/hud/ (git-ignored, outside
    /// Assets/, and NOT wiped when Unity closes).
    ///
    /// ================================================================================================
    /// TWO THINGS THIS RIG DOES DIFFERENTLY FROM ITS TWO PREDECESSORS, BOTH FORCED BY WHAT IS UNDER TEST
    /// ================================================================================================
    ///
    /// 1. IT RUNS IN SampleScene, NOT IN A PRIVATE WORLD. <see cref="PhotoShootRig"/> and
    ///    <see cref="GalleryShootRig"/> build a grey plane and a light, which is right for them: they
    ///    measure geometry, and a private world makes the measurement repeatable. This story's questions
    ///    are "can a person read this?" and "is the scene actually wired?", and neither survives the trip
    ///    into a private world:
    ///      · Story 1.11's ONLY defect that the rig could not see was produced solely by the real scene —
    ///        the gallery backdrop leaked the town through URP post-processing, invisible over a grey plane.
    ///        A readout that is legible over flat grey may be unreadable over a bright sky, and that is the
    ///        whole of AC3.
    ///      · A HUD canvas the rig builds itself proves the rig can build a canvas. The shipped thing is the
    ///        one authored in SampleScene, and "the labels are unassigned" / "the canvas is off" /
    ///        "the sorting order buries it" are exactly the silent failures Task 5 is about.
    ///    So the rig opens the real scene, adds one runner object, and puts the developer back afterwards.
    ///
    /// 2. IT LEAVES THE CAMERA RENDERING TO THE SCREEN. Both other rigs bind a RenderTexture to the camera
    ///    for the whole run and read that back. This one must not: the HUD is a Screen Space – Overlay
    ///    canvas, which is composited AFTER every camera and is therefore absent from <c>cam.Render()</c>
    ///    output by construction — that is precisely the guarantee AC5 needs, and it inverts this project's
    ///    most reliable verification technique. The runner uses
    ///    <c>ScreenCapture.CaptureScreenshotAsTexture()</c> instead, and proves the technique with control
    ///    shots before trusting a single HUD image (see GradeHudShootRunner, Phase 0).
    ///
    /// Deliberately NOT a test suite: it asserts nothing and cannot pass or fail. It produces photographs
    /// and measurements of the game running, which are then looked at and judged.
    /// </summary>
    public static class GradeHudShootRig
    {
        /// <summary>⚠️ NOT <c>Temp/</c>. Unity DELETES the whole Temp/ folder when the editor shuts down, and
        /// that cost a real handover on 2026-07-27 — photographs Alexv had been asked to review were gone
        /// before he opened the folder. Anything a human is asked to look at must outlive the editor session
        /// that produced it.</summary>
        private const string VerificationRoot = "_bmad-output/verification";

        public const string OutputDir = VerificationRoot + "/hud";

        // Its OWN SessionState key. The other two rigs hook the same play-mode-exit event with their own
        // keys and return early when theirs is empty, so the three restores cannot fight over the scene.
        private const string ReturnSceneKey = "GradeHudShoot.ReturnScene";

        private const string ScenePath = "Assets/Scenes/SampleScene.unity";
        private const string HudCfgPath = "Assets/Data/UI/GradeHudConfig.asset";
        private const string ChannelPath = "Assets/Data/Channels/ShotCapturedChannel.asset";
        private const string GradingCfgPath = "Assets/Data/Grading/GradingConfig.asset";

        [InitializeOnLoadMethod]
        private static void HookPlayModeExit()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        /// <summary>
        /// Puts the developer's scene back when play mode ends.
        ///
        /// ⚠️ THIS REOPENS FROM DISK EVEN WHEN THE PREVIOUS SCENE WAS SampleScene ITSELF, and that is the
        /// point rather than a wasted step: the runner object is added in EDIT mode, so Unity's own
        /// play-mode revert would faithfully restore it along with everything else and leave a rig
        /// component sitting in the scene the developer goes on to work in. Reloading from disk is what
        /// makes the scene byte-identical to what was committed.
        /// </summary>
        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;

            string previous = SessionState.GetString(ReturnSceneKey, string.Empty);
            if (string.IsNullOrEmpty(previous)) return;

            SessionState.EraseString(ReturnSceneKey);
            if (File.Exists(previous))
            {
                EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                Debug.Log($"[GradeHudShoot] Shoot finished — reopened {previous} from disk (rig object gone).");
            }
        }

        /// <summary>
        /// Records the readout's whole life as a frame sequence, for the questions a still cannot settle —
        /// is the fade smooth, and is it on screen long enough to read?
        ///
        /// A separate menu item rather than another phase of the shoot, for two reasons: it writes to its
        /// own folder so it cannot clobber the stills, and it is the one thing here worth re-running on its
        /// own every time the hold or fade is retuned.
        /// </summary>
        [MenuItem("Tools/HUD/Grade HUD — Record the readout (Play)")]
        public static void Record() => Run(OutputDir + "-motion", recordOnly: true);

        [MenuItem("Tools/HUD/Grade HUD Shoot (Play)")]
        public static void Build() => Run(OutputDir, recordOnly: false);

        private static void Run(string outputDir, bool recordOnly)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[GradeHudShoot] Already in play mode — stop first.");
                return;
            }

            // EVERY loaded scene, not just the active one: OpenScene(..., Single) discards them all, and
            // checking only the active scene means unsaved work in an additively-loaded scene vanishes with
            // no prompt.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isDirty) continue;

                EditorUtility.DisplayDialog("Grade HUD Shoot",
                    $"Scene '{s.name}' has unsaved changes. Save it first — this reloads scenes from disk.",
                    "OK");
                return;
            }

            // A never-saved scene has an empty path, which would be written as the return key and then
            // short-circuit the restore — stranding the developer with no message.
            string previousScene = SceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(previousScene))
            {
                EditorUtility.DisplayDialog("Grade HUD Shoot",
                    "Save the current scene to disk first — the rig needs a path to return you to.", "OK");
                return;
            }

            // Validate by GUID BEFORE touching the scene, and never by loading: an asset loaded before a
            // scene swap has its native half unloaded — it then reads its fields perfectly from managed
            // memory while `== null` is simultaneously true.
            foreach (string required in new[] { ScenePath, HudCfgPath, ChannelPath, GradingCfgPath })
            {
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(required))) continue;

                Debug.LogError($"[GradeHudShoot] Required asset missing: {required}. Nothing was changed.");
                return;
            }

            ResetDir(outputDir);
            SessionState.SetString(ReturnSceneKey, previousScene);

            if (SceneManager.GetActiveScene().path != ScenePath)
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            try
            {
                AddRunnerAndPlay(outputDir, recordOnly);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GradeHudShoot] Setup failed — restoring your scene.\n{e}");
                Abort(previousScene);
            }
        }

        private static void AddRunnerAndPlay(string outputDir, bool recordOnly)
        {
            // ⚠️ EVERY REFERENCE BELOW COMES OUT OF THE SHIPPED SCENE, not out of a world the rig built.
            // That is the whole point: if the scene is mis-wired, this throws HERE, loudly, instead of the
            // run producing a plausible set of photographs of nothing.
            var hud = Object.FindAnyObjectByType<GradeHud>(FindObjectsInactive.Include);
            if (hud == null)
                throw new System.InvalidOperationException(
                    "No GradeHud in SampleScene — Task 5's canvas is missing. Nothing to photograph.");

            var photo = Object.FindAnyObjectByType<PhotoModeController>(FindObjectsInactive.Include);
            var manager = Object.FindAnyObjectByType<EventManager>(FindObjectsInactive.Include);
            var service = Object.FindAnyObjectByType<GalleryService>(FindObjectsInactive.Include);
            var view = Object.FindAnyObjectByType<GalleryView>(FindObjectsInactive.Include);
            var player = Object.FindAnyObjectByType<ThirdPersonController>(FindObjectsInactive.Include);

            if (photo == null || manager == null)
                throw new System.InvalidOperationException(
                    "SampleScene has no PhotoModeController and/or no EventManager — cannot drive a capture.");

            Camera cam = Camera.main;
            if (cam == null)
                throw new System.InvalidOperationException("SampleScene has no MainCamera-tagged camera.");

            // The chase camera drives Main Camera's transform every LateUpdate. Left running it would fight
            // the rig for the camera and every framing below would be whatever the player happened to be
            // looking at — the rig would then caption its own photographs with poses it never achieved.
            // Recorded here and re-enabled by the runner's Restore().
            var chase = cam.GetComponent<ThirdPersonCamera>();

            var runnerGo = new GameObject("GradeHudShootRunner");
            var runner = runnerGo.AddComponent<GradeHudShootRunner>();

            runner.hud = hud;
            runner.hudCanvas = hud.GetComponent<Canvas>();
            runner.hudGroup = hud.GetComponent<CanvasGroup>();
            runner.hudPanel = hud.GetComponentInChildren<Image>(includeInactive: true);
            runner.photo = photo;
            runner.manager = manager;
            runner.cam = cam;
            runner.chaseCamera = chase;
            runner.player = player;
            runner.service = service;
            runner.view = view;
            runner.gradingConfig = AssetDatabase.LoadAssetAtPath<GradingConfig>(GradingCfgPath);
            runner.shippedHudConfig = AssetDatabase.LoadAssetAtPath<GradeHudConfig>(HudCfgPath);
            runner.channel = AssetDatabase.LoadAssetAtPath<ShotCapturedChannel>(ChannelPath);
            runner.outputDir = outputDir;
            runner.recordOnly = recordOnly;

            Debug.Log($"[GradeHudShoot] Runner added to SampleScene ({(recordOnly ? "recording" : "shoot")}). "
                      + $"Entering play mode — output lands in {outputDir}");
            EditorApplication.EnterPlaymode();
        }

        /// <summary>Puts the developer back where they started and disarms the play-mode-exit restore.</summary>
        private static void Abort(string previousScene)
        {
            SessionState.EraseString(ReturnSceneKey);

            if (!string.IsNullOrEmpty(previousScene) && File.Exists(previousScene))
                EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
            else
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        }

        /// <summary>Clears the output folder. Delete-then-create immediately is the classic Windows lazy
        /// deletion race (the directory handle lingers), so fall back to emptying it file by file.</summary>
        private static void ResetDir(string dir)
        {
            // ⚠️ This does a RECURSIVE DELETE inside _bmad-output/, which also holds every story file and
            // planning artifact. A typo or a future caller passing the wrong string would destroy work that
            // is git-ignored and therefore unrecoverable. Refuse anything that is not a subfolder of the
            // verification root. (Copied from the other two rigs for exactly the same reason.)
            string normalized = dir.Replace('\\', '/');
            if (!normalized.StartsWith(VerificationRoot + "/", System.StringComparison.Ordinal))
                throw new System.ArgumentException(
                    $"Refusing to clear '{dir}': rig output must live under '{VerificationRoot}/'.", nameof(dir));

            try
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
            catch (IOException)
            {
                if (Directory.Exists(dir))
                {
                    foreach (string f in Directory.GetFiles(dir))
                    {
                        try { File.Delete(f); } catch (IOException) { /* held open — will be overwritten */ }
                    }
                }
            }

            Directory.CreateDirectory(dir);
        }
    }
}
