using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.AI.Navigation;
using CameraGame.Events;
using CameraGame.Grading;
using CameraGame.PhotoMode;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// Builds a private photo-shoot world — just the drunk, a camera and some ground — and enters play
    /// mode, where <see cref="PhotoShootRunner"/> walks the camera to a series of vantage points and pulls
    /// the real shutter at each one.
    ///
    /// Deliberately NOT a test suite: it asserts nothing and cannot pass or fail. It produces photographs
    /// of the game running, which are then looked at and judged. Encoding expectations would only prove
    /// the code agrees with whatever was assumed while writing them; a photograph shows what it did.
    ///
    /// Tools > Grading > Photo Shoot (Play). Output: _bmad-output/verification/photo-shoot/ (git-ignored,
    /// outside Assets/, and NOT wiped when Unity closes) — one PNG per vantage point plus shots.txt
    /// pairing each picture with the verdict the game gave it.
    /// </summary>
    public static class PhotoShootRig
    {
        /// <summary>
        /// Where the photographs land.
        ///
        /// ⚠️ NOT <c>Temp/</c>, which is where this rig wrote until 2026-07-27. Unity DELETES the whole
        /// <c>Temp/</c> folder when the editor shuts down, so every photograph produced by a run evaporated
        /// the next time Unity was closed — including the ones Alexv had been asked to look at, which were
        /// simply gone by the time he opened the folder. Evidence that a human is asked to review has to
        /// outlive the editor session that produced it.
        ///
        /// <c>_bmad-output/</c> satisfies the original reason for choosing Temp/ (it is outside
        /// <c>Assets/</c>, so Unity never imports these PNGs as assets, and it is git-ignored, so they
        /// never reach the repo) without the disappearing act.
        /// </summary>
        private const string VerificationRoot = "_bmad-output/verification";

        public const string OutputDir = VerificationRoot + "/photo-shoot";
        public const string StudyOutputDir = VerificationRoot + "/photo-shoot-town";

        /// <summary>The real town. Photographed by the placement study so composition can be judged
        /// against actual scenery rather than the private world's empty plane.</summary>
        private const string TownScenePath = "Assets/Scenes/SampleScene.unity";

        // Survives the domain reloads that entering and leaving play mode cause, so the rig can put the
        // developer back in the scene they were working in instead of stranding them in the test world.
        private const string ReturnSceneKey = "PhotoShoot.ReturnScene";

        [InitializeOnLoadMethod]
        private static void HookPlayModeExit()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;

            string previous = SessionState.GetString(ReturnSceneKey, string.Empty);
            if (string.IsNullOrEmpty(previous)) return;

            SessionState.EraseString(ReturnSceneKey);
            if (File.Exists(previous))
            {
                EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                Debug.Log($"[PhotoShoot] Shoot finished — reopened {previous}.");
            }
        }

        private const string ConfigPath = "Assets/Data/Grading/GradingConfig.asset";
        private const string ChannelPath = "Assets/Data/Channels/ShotCapturedChannel.asset";
        private const string CaptureCfgPath = "Assets/Data/Camera/CaptureConfig.asset";
        private const string CameraCfgPath = "Assets/Data/Camera/CameraConfig.asset";
        private const string DrunkPrefabPath = "Assets/Prefabs/Events/EventActor_Drunk.prefab";

        [MenuItem("Tools/Grading/Photo Shoot (Play)")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[PhotoShoot] Already in play mode — stop first.");
                return;
            }

            // EVERY loaded scene, not just the active one: NewScene(..., Single) below discards them all, and
            // checking only GetActiveScene() meant unsaved work in an additively-loaded scene vanished with
            // no prompt.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isDirty) continue;

                EditorUtility.DisplayDialog("Photo Shoot",
                    $"Scene '{s.name}' has unsaved changes. Save it first — this swaps scenes.", "OK");
                return;
            }

            // A never-saved scene has an empty path, which would be written as the return key and then
            // short-circuit the restore — stranding you in the test world with no message.
            string previousScene = SceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(previousScene))
            {
                EditorUtility.DisplayDialog("Photo Shoot",
                    "Save the current scene to disk first — the rig needs a path to return you to.", "OK");
                return;
            }

            // Validate the required assets BEFORE touching the scene. Checked by GUID rather than by loading
            // them: an asset loaded before NewScene(..., Single) has its native half unloaded, which is the
            // very trap documented below. Getting this order wrong is what let the guard below fire while the
            // developer's scene was already gone.
            foreach (string required in new[] { ConfigPath, ChannelPath, DrunkPrefabPath })
            {
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(required))) continue;

                Debug.LogError($"[PhotoShoot] Required asset missing: {required}. Nothing was changed.");
                return;
            }

            ResetDir(OutputDir);

            // Remember where we came from so leaving play mode returns there. Without this the rig strands
            // you in an untitled test world and you have to find your way back by hand.
            SessionState.SetString(ReturnSceneKey, previousScene);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // From here on the developer's scene is CLOSED, so every failure path must put it back. Anything
            // that throws — or any early return — would otherwise leave them in an untitled test world with
            // the return key still armed, which then force-reopens a scene on their next unrelated play-mode
            // exit and discards whatever they had unsaved at that point.
            try
            {
                BuildWorldAndPlay();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PhotoShoot] Build failed — restoring your scene.\n{e}");
                Abort(previousScene);
            }
        }

        /// <summary>
        /// Photographs matched centred-vs-thirds pairs in the REAL TOWN (Story 1.10 review).
        ///
        /// The main shoot's private world is a featureless grey plane, so its thirds pose put the drunk in
        /// the corner of an empty frame — and the rule of thirds is precisely the rule that needs the rest
        /// of the frame to contain something. This runs the same grader and the same shutter against the
        /// actual town, using the scene's OWN PhotoModeController, EventManager and route rather than
        /// rebuilt stand-ins, so what is being judged is the real game.
        ///
        /// ⚠️ The town scene is opened and then MUTATED (a runner is added; the walk-camera scripts are
        /// switched off so the rig can own the camera). It is never SAVED, and the play-mode-exit hook
        /// reopens it from disk, which discards every one of those changes. Do not add a save here.
        /// </summary>
        [MenuItem("Tools/Grading/Photo Shoot — Placement Study (Town)")]
        public static void BuildPlacementStudy()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[PhotoShoot] Already in play mode — stop first.");
                return;
            }

            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isDirty) continue;

                EditorUtility.DisplayDialog("Photo Shoot",
                    $"Scene '{s.name}' has unsaved changes. Save it first — this reloads the scene.", "OK");
                return;
            }

            if (string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(TownScenePath)))
            {
                Debug.LogError($"[PhotoShoot] Town scene missing: {TownScenePath}. Nothing was changed.");
                return;
            }

            // Return the developer to whatever they had open. If that IS the town scene, reopening it from
            // disk is exactly what throws away the mutations below.
            string previousScene = SceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(previousScene)) previousScene = TownScenePath;

            ResetDir(StudyOutputDir);
            SessionState.SetString(ReturnSceneKey, previousScene);

            try
            {
                if (SceneManager.GetActiveScene().path != TownScenePath)
                    EditorSceneManager.OpenScene(TownScenePath, OpenSceneMode.Single);

                WireTownStudyAndPlay();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PhotoShoot] Placement study failed — restoring your scene.\n{e}");
                Abort(previousScene);
            }
        }

        private static void WireTownStudyAndPlay()
        {
            var photo = Object.FindFirstObjectByType<PhotoModeController>(FindObjectsInactive.Include);
            var manager = Object.FindFirstObjectByType<EventManager>(FindObjectsInactive.Include);

            if (photo == null || manager == null)
                throw new System.InvalidOperationException(
                    "The town scene has no PhotoModeController and/or no EventManager — nothing to drive.");

            // The camera the CONTROLLER grades through, not whatever Camera.main happens to be: grading and
            // the saved image have to be the same projection, and the controller is the authority on which
            // camera that is. Falls back to Camera.main only if the field was left unassigned (in which case
            // the controller itself falls back to exactly the same thing at Awake).
            var so = new SerializedObject(photo);
            var cam = so.FindProperty("photoCamera")?.objectReferenceValue as Camera;
            if (cam == null) cam = Camera.main;
            if (cam == null)
                throw new System.InvalidOperationException("No camera to shoot with in the town scene.");

            // Switch off everything that drives the camera or the player, or the rig and the game will
            // fight over the transform every frame and the shots will land wherever the race lands. These
            // are runtime-only edits to a scene that is never saved.
            foreach (var chase in Object.FindObjectsByType<ThirdPersonCamera>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                chase.enabled = false;

            foreach (var walk in Object.FindObjectsByType<ThirdPersonController>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                walk.enabled = false;

            var grading = AssetDatabase.LoadAssetAtPath<GradingConfig>(ConfigPath);

            var runnerGo = new GameObject("PhotoShootRunner");
            var runner = runnerGo.AddComponent<PhotoShootRunner>();
            runner.photo = photo;
            runner.manager = manager;
            runner.cam = cam;
            runner.cameraConfig = AssetDatabase.LoadAssetAtPath<CameraConfig>(CameraCfgPath);
            runner.gradingConfig = grading;
            runner.outputDir = StudyOutputDir;
            runner.placementStudy = true;

            Debug.Log("[PhotoShoot] Town placement study wired. Entering play mode — shots land in "
                      + StudyOutputDir);
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
            // ⚠️ This does a RECURSIVE DELETE, and since 2026-07-27 it points inside _bmad-output/, which
            // also holds every story file and planning artifact. A typo or a future caller passing the
            // wrong string would destroy work that is git-ignored and therefore unrecoverable. Refuse
            // anything that is not a subfolder of the verification root — cheap insurance against the one
            // mistake in this file that could not be undone.
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

        private static void BuildWorldAndPlay()
        {
            // Load AFTER the scene swap. Opening a scene in Single mode unloads unused assets, destroying
            // the native half of anything loaded beforehand — a ScriptableObject in that state still reads
            // its public fields from managed memory while `== null` is simultaneously true.
            var grading = AssetDatabase.LoadAssetAtPath<GradingConfig>(ConfigPath);
            var channel = AssetDatabase.LoadAssetAtPath<ShotCapturedChannel>(ChannelPath);
            var captureCfg = AssetDatabase.LoadAssetAtPath<CaptureConfig>(CaptureCfgPath);
            var cameraCfg = AssetDatabase.LoadAssetAtPath<CameraConfig>(CameraCfgPath);
            var drunk = AssetDatabase.LoadAssetAtPath<GameObject>(DrunkPrefabPath);

            // Existence was checked by GUID before the swap; this catches a load that failed for another
            // reason (a corrupt asset, a broken script reference on the prefab).
            if (grading == null || channel == null || drunk == null)
                throw new System.InvalidOperationException(
                    "GradingConfig / ShotCapturedChannel / drunk prefab failed to load after the scene swap.");

            // The manager needs the COMPONENT, not just the prefab GameObject — validated here rather than
            // discovered as a misleading "Unsupported type" from SetPrivate.
            var drunkActor = drunk.GetComponent<EventActor>();
            if (drunkActor == null)
                throw new System.InvalidOperationException(
                    $"{DrunkPrefabPath} has no EventActor component on its root.");

            var light = new GameObject("KeyLight").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.3f;
            light.transform.rotation = Quaternion.Euler(48f, 150f, 0f);

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = Vector3.one * 20f;

            // Bake a NavMesh. Without one, EventActor.Begin enables its agent off-mesh and Unity logs
            // "Failed to create agent because there is no valid NavMesh" on every spawn — four errors per
            // run in the first version of this rig. A rig that produces its own errors trains you to ignore
            // errors. Baking also lets the actor genuinely walk, so the pictures are of a moving subject.
            var surface = ground.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.BuildNavMesh();

            var camGo = new GameObject("PhotoCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.fieldOfView = cameraCfg != null ? cameraCfg.wideFov : 60f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 5000f;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.20f, 0.28f);

            // Minimal viewfinder + flash so PhotoModeController finds everything it expects and the console
            // stays honest — a rig that logs errors of its own teaches you to ignore errors.
            var canvasGo = new GameObject("UI", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var viewfinder = new GameObject("Viewfinder", typeof(CanvasGroup));
            viewfinder.transform.SetParent(canvasGo.transform, false);

            var flashGo = new GameObject("Flash", typeof(CanvasGroup), typeof(Image));
            flashGo.transform.SetParent(canvasGo.transform, false);
            var flashRect = flashGo.GetComponent<RectTransform>();
            flashRect.anchorMin = Vector2.zero;
            flashRect.anchorMax = Vector2.one;
            flashRect.offsetMin = flashRect.offsetMax = Vector2.zero;
            flashGo.GetComponent<Image>().raycastTarget = false;

            var managerGo = new GameObject("EventManager");
            var manager = managerGo.AddComponent<EventManager>();
            SetPrivate(manager, "actorPrefab", drunkActor);
            SetPrivate(manager, "maxConcurrent", 1);
            SetPrivate(manager, "respawnDelay", 0.5f);

            var photo = camGo.AddComponent<PhotoModeController>();
            SetPrivate(photo, "photoCamera", cam);
            SetPrivate(photo, "cameraConfig", cameraCfg);
            SetPrivate(photo, "captureConfig", captureCfg);
            SetPrivate(photo, "shotCapturedChannel", channel);
            SetPrivate(photo, "gradingConfig", grading);
            SetPrivate(photo, "eventManager", manager);
            SetPrivate(photo, "viewfinderRoot", viewfinder);
            SetPrivate(photo, "captureFlash", flashGo.GetComponent<CanvasGroup>());

            // The runner lives in the RUNTIME assembly behind #if UNITY_EDITOR. It cannot live here:
            // Unity fails to resolve an editor-assembly MonoBehaviour on a scene object when entering play
            // mode, and the component comes back as "The referenced script (Unknown) is missing!" — the rig
            // then silently does nothing while the scene runs on around it.
            var runnerGo = new GameObject("PhotoShootRunner");
            var runner = runnerGo.AddComponent<PhotoShootRunner>();
            runner.photo = photo;
            runner.manager = manager;
            runner.cam = cam;
            runner.cameraConfig = cameraCfg;
            runner.gradingConfig = grading;
            runner.outputDir = OutputDir;

            Debug.Log("[PhotoShoot] World built. Entering play mode — shots will land in " + OutputDir);
            EditorApplication.EnterPlaymode();
        }

        /// <summary>Assigns a [SerializeField] private field by name, so the rig can wire the real
        /// components without widening their public API just for a bench.</summary>
        private static void SetPrivate(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogError($"[PhotoShoot] No serialized field '{field}' on {target.GetType().Name}.");
                return;
            }

            switch (value)
            {
                case int i: p.intValue = i; break;
                case float f: p.floatValue = f; break;
                case Object o: p.objectReferenceValue = o; break;

                // C# type patterns do NOT match null, so a missing optional asset (CameraConfig,
                // CaptureConfig) used to fall through to `default:` and report "Unsupported type" — naming
                // the switch instead of the absent asset, on a path the rig deliberately tolerates.
                case null: p.objectReferenceValue = null; break;

                default:
                    Debug.LogError($"[PhotoShoot] Unsupported type '{value.GetType().Name}' for '{field}'.");
                    return;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
