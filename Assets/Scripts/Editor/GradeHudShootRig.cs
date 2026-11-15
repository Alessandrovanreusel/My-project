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

        /// <summary>
        /// ONE-SHOT override of the output folder (Story 1.19). Set it, then run a menu item; <see cref="Run"/>
        /// reads it and ERASES it straight away, so it can never leak into a later run.
        ///
        /// ⚠️ WHY IT EXISTS: this rig writes into <c>hud/</c> by default, and <c>hud/</c> is the evidence Story
        /// 1.12 closed on. "Rigs that write into a story's evidence folders overwrite the dev's evidence"
        /// (loop lessons, 2-2). A later story points its runs at its own folder with this key instead.
        /// It must still be under the verification root — <see cref="ResetDir"/> refuses anything else.
        /// </summary>
        public const string OutputDirKey = "GradeHudShoot.OutputDir";

        /// <summary>ONE-SHOT: when true, the legibility run also sweeps candidate reds and outline thicknesses
        /// (Story 1.19, decision 1d). Read and erased by <see cref="Run"/>, like <see cref="OutputDirKey"/>.</summary>
        public const string CandidatesKey = "GradeHudShoot.Candidates";

        /// <summary>The Game View size index the rig replaced, so it can be put back when play mode ends.
        /// Absent means the rig did not change it.</summary>
        private const string PrevGameViewKey = "GradeHudShoot.PrevGameViewSize";

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
            RestoreGameViewSize();
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

        /// <summary>
        /// Story 1.19, AC4: photographs the readout at the SHIPPING resolution on frozen frames, in four
        /// variants per frame (as shipped / glyphs painted marker magenta with the outline marker green /
        /// labels off / outline off), so tools/verification/measure_hud_legibility.py can measure text
        /// against outline and text against panel off the pixels actually rendered. Every Phase A state
        /// with the real shutter, then the brightest backgrounds a survey finds, with every readout colour
        /// raised through the real channel onto each.
        /// </summary>
        [MenuItem("Tools/HUD/Grade HUD — Legibility at 1080p (Play)")]
        public static void Legibility() => Run(OutputDir + "-legibility", recordOnly: false, legibility: true);

        private static void Run(string outputDir, bool recordOnly, bool legibility = false)
        {
            // One-shot options: read, then erased whatever happens next, so a refused run cannot arm the
            // next one (loop lessons: SessionState options survive domain reloads and earlier sessions).
            string overrideDir = SessionState.GetString(OutputDirKey, string.Empty);
            bool candidates = SessionState.GetBool(CandidatesKey, false);
            SessionState.EraseString(OutputDirKey);
            SessionState.EraseBool(CandidatesKey);
            if (!string.IsNullOrEmpty(overrideDir)) outputDir = overrideDir.Replace('\\', '/');
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

            // ⚠️ 1920x1080, THE SHIPPING RESOLUTION, FOR EVERY RUN (Story 1.19). The HUD canvas scales with
            // the screen, so a small Game View photographs the right LAYOUT at the wrong SIZE: the 1.12
            // evidence was 953x476, where the 22 px reference lines are ~10 px tall and a 1 px outline would
            // be 0.44 px. Legibility measured there is not legibility a player gets. The runner logs the
            // captured size and the canvas scale factor, and says so when they are not 1920x1080 and 1.0.
            SelectFullHdGameView();

            try
            {
                AddRunnerAndPlay(outputDir, recordOnly, legibility, candidates);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GradeHudShoot] Setup failed — restoring your scene.\n{e}");
                Abort(previousScene);
            }
        }

        private static void AddRunnerAndPlay(string outputDir, bool recordOnly, bool legibility, bool candidates)
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
            PhotoShootRig.ReadPlayerEye(out runner.eyeHeight, out _, out _);
            runner.player = player;
            runner.service = service;
            runner.view = view;
            runner.gradingConfig = AssetDatabase.LoadAssetAtPath<GradingConfig>(GradingCfgPath);
            runner.shippedHudConfig = AssetDatabase.LoadAssetAtPath<GradeHudConfig>(HudCfgPath);
            runner.channel = AssetDatabase.LoadAssetAtPath<ShotCapturedChannel>(ChannelPath);
            runner.outputDir = outputDir;
            runner.recordOnly = recordOnly;
            runner.legibilityOnly = legibility;
            runner.runCandidates = candidates;

            Debug.Log($"[GradeHudShoot] Runner added to SampleScene ({(recordOnly ? "recording" : "shoot")}). "
                      + $"Entering play mode — output lands in {outputDir}");
            EditorApplication.EnterPlaymode();
        }

        /// <summary>Puts the developer back where they started and disarms the play-mode-exit restore.</summary>
        private static void Abort(string previousScene)
        {
            SessionState.EraseString(ReturnSceneKey);
            RestoreGameViewSize();

            if (!string.IsNullOrEmpty(previousScene) && File.Exists(previousScene))
                EditorSceneManager.OpenScene(previousScene, OpenSceneMode.Single);
            else
                EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
        }

        // --- The Game View size -----------------------------------------------------------------------
        //
        // ⚠️ THE BUILT-IN "Full HD" PRESET IS SELECTED, NOT A CUSTOM SIZE ADDED. test-helper's
        // GameViewControlHelper.SetResolution (CLAUDE.md, Toolbox) calls PlayModeWindow.SetCustomRenderingResolution
        // on Unity 6, which ADDS a custom entry to the user's Game View size list — editor preferences that
        // live outside the project and that nothing here would remove. The Standalone group already ships a
        // 1920x1080 FixedResolution preset (measured 2026-11-15: index 3 of 7, all built in), so selecting it
        // and putting the old index back leaves nothing behind. It needs reflection because GameView is
        // internal; if that ever fails, the run goes ahead at whatever size the window is, and the runner's
        // resolution check says so in its output.

        private static System.Type GameViewType =>
            typeof(EditorWindow).Assembly.GetType("UnityEditor.GameView");

        private const System.Reflection.BindingFlags Any =
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic;

        private static Object GameViewWindow()
        {
            System.Type t = GameViewType;
            if (t == null) return null;
            Object[] all = Resources.FindObjectsOfTypeAll(t);
            return all.Length > 0 ? all[0] : null;
        }

        private static int FullHdIndex()
        {
            System.Type sizesType = typeof(EditorWindow).Assembly.GetType("UnityEditor.GameViewSizes");
            object sizes = typeof(ScriptableSingleton<>).MakeGenericType(sizesType)
                .GetProperty("instance").GetValue(null, null);
            object group = sizesType.GetProperty("currentGroup").GetValue(sizes, null);
            System.Type g = group.GetType();
            int total = (int)g.GetMethod("GetTotalCount").Invoke(group, null);
            for (int i = 0; i < total; i++)
            {
                object s = g.GetMethod("GetGameViewSize").Invoke(group, new object[] { i });
                System.Type st = s.GetType();
                if (st.GetProperty("sizeType").GetValue(s, null).ToString() == "FixedResolution"
                    && (int)st.GetProperty("width").GetValue(s, null) == 1920
                    && (int)st.GetProperty("height").GetValue(s, null) == 1080)
                    return i;
            }
            return -1;
        }

        private static void SelectFullHdGameView()
        {
            try
            {
                Object gv = GameViewWindow();
                int target = FullHdIndex();
                if (gv == null || target < 0)
                {
                    Debug.Log("[GradeHudShoot] No Game View or no built-in 1920x1080 preset — running at the " +
                              "window's size. The runner's resolution line will say so.");
                    return;
                }

                // A restore still pending from a run that never reached edit mode wins: overwriting it with
                // the Full HD index would "restore" the Game View to the rig's own setting.
                string pending = SessionState.GetString(PrevGameViewKey, string.Empty);
                int previous = string.IsNullOrEmpty(pending)
                    ? (int)GameViewType.GetProperty("selectedSizeIndex", Any).GetValue(gv, null)
                    : int.Parse(pending);
                SessionState.SetString(PrevGameViewKey, previous.ToString());

                GameViewType.GetMethod("SizeSelectionCallback", Any).Invoke(gv, new object[] { target, null });
                Debug.Log($"[GradeHudShoot] Game View set to the built-in 1920x1080 preset (index {target}); " +
                          $"index {previous} is put back when play mode ends.");
            }
            catch (System.Exception e)
            {
                Debug.Log($"[GradeHudShoot] Could not set the Game View to 1920x1080 ({e.GetType().Name}) — " +
                          "running at the window's size; the runner's resolution line will say so.");
            }
        }

        private static void RestoreGameViewSize()
        {
            string stored = SessionState.GetString(PrevGameViewKey, string.Empty);
            if (string.IsNullOrEmpty(stored)) return;
            SessionState.EraseString(PrevGameViewKey);

            try
            {
                Object gv = GameViewWindow();
                if (gv == null) return;
                int previous = int.Parse(stored);
                GameViewType.GetMethod("SizeSelectionCallback", Any).Invoke(gv, new object[] { previous, null });
                Debug.Log($"[GradeHudShoot] Game View size put back to index {previous}.");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[GradeHudShoot] Could not put the Game View size back ({e.Message}). " +
                                 "Choose it again in the Game View's resolution menu.");
            }
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
