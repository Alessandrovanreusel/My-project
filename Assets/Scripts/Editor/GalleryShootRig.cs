using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.AI.Navigation;
using CameraGame.Events;
using CameraGame.Gallery;
using CameraGame.Grading;
using CameraGame.PhotoMode;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// Builds a private world — the drunk, a camera, a player object with real PlayerInput, and a real
    /// gallery — and enters play mode, where <see cref="GalleryShootRunner"/> fills the gallery with the
    /// real shutter and writes out what it actually holds.
    ///
    /// Deliberately NOT a test suite: it asserts nothing and cannot pass or fail. It produces photographs
    /// and measurements of the game running, which are then looked at and judged. Encoding expectations
    /// would only prove the code agrees with whatever was assumed while writing them.
    ///
    /// Tools > Gallery > Gallery Shoot (Play). Output: _bmad-output/verification/gallery/ (git-ignored,
    /// outside Assets/, and NOT wiped when Unity closes).
    ///
    /// Modelled directly on <see cref="PhotoShootRig"/>, including its two hard-won rules: validate assets
    /// by GUID BEFORE swapping the scene (an asset loaded beforehand has its native half unloaded), and
    /// remember the developer's scene in <c>SessionState</c> so leaving play mode puts them back in it.
    /// </summary>
    public static class GalleryShootRig
    {
        /// <summary>⚠️ NOT <c>Temp/</c>. Unity DELETES the whole Temp/ folder when the editor shuts down, and
        /// that cost a real handover on 2026-07-27 — photographs Alexv had been asked to review were gone
        /// before he opened the folder. Anything a human is asked to look at must outlive the editor session
        /// that produced it. <c>_bmad-output/</c> is outside Assets/ (so Unity never imports these PNGs) and
        /// git-ignored (so they never reach the repo), without the disappearing act.</summary>
        private const string VerificationRoot = "_bmad-output/verification";

        public const string OutputDir = VerificationRoot + "/gallery";

        // Its OWN SessionState key. PhotoShootRig hooks the same play-mode-exit event with its own key and
        // returns early when that key is empty, so the two restores cannot fight over the scene.
        private const string ReturnSceneKey = "GalleryShoot.ReturnScene";

        private const string GradingCfgPath = "Assets/Data/Grading/GradingConfig.asset";
        private const string GalleryCfgPath = "Assets/Data/Gallery/GalleryConfig.asset";
        private const string ChannelPath = "Assets/Data/Channels/ShotCapturedChannel.asset";
        private const string CaptureCfgPath = "Assets/Data/Camera/CaptureConfig.asset";
        private const string CameraCfgPath = "Assets/Data/Camera/CameraConfig.asset";
        private const string DrunkPrefabPath = "Assets/Prefabs/Events/EventActor_Drunk.prefab";
        private const string InputActionsPath = "Assets/Input/InputSystem_Actions.inputactions";

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
                Debug.Log($"[GalleryShoot] Shoot finished — reopened {previous}.");
            }
        }

        [MenuItem("Tools/Gallery/Gallery Shoot (Play)")]
        public static void Build()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[GalleryShoot] Already in play mode — stop first.");
                return;
            }

            // EVERY loaded scene, not just the active one: NewScene(..., Single) discards them all, and
            // checking only the active scene means unsaved work in an additively-loaded scene vanishes with
            // no prompt.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isDirty) continue;

                EditorUtility.DisplayDialog("Gallery Shoot",
                    $"Scene '{s.name}' has unsaved changes. Save it first — this swaps scenes.", "OK");
                return;
            }

            // A never-saved scene has an empty path, which would be written as the return key and then
            // short-circuit the restore — stranding the developer in the test world with no message.
            string previousScene = SceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(previousScene))
            {
                EditorUtility.DisplayDialog("Gallery Shoot",
                    "Save the current scene to disk first — the rig needs a path to return you to.", "OK");
                return;
            }

            // Validate by GUID BEFORE touching the scene. Checked by GUID rather than by loading, because an
            // asset loaded before NewScene(..., Single) has its native half unloaded — it then reads its
            // fields perfectly from managed memory while `== null` is simultaneously true.
            foreach (string required in new[] { GradingCfgPath, GalleryCfgPath, ChannelPath, DrunkPrefabPath })
            {
                if (!string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(required))) continue;

                Debug.LogError($"[GalleryShoot] Required asset missing: {required}. Nothing was changed.");
                return;
            }

            ResetDir(OutputDir);
            SessionState.SetString(ReturnSceneKey, previousScene);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // From here the developer's scene is CLOSED, so every failure path must put it back. Anything
            // that throws would otherwise leave them in an untitled test world with the return key still
            // armed — which then force-reopens a scene on their next unrelated play-mode exit and discards
            // whatever they had unsaved at that point.
            try
            {
                BuildWorldAndPlay();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[GalleryShoot] Build failed — restoring your scene.\n{e}");
                Abort(previousScene);
            }
        }

        private static void BuildWorldAndPlay()
        {
            // Load AFTER the scene swap, for the native-half reason above.
            var grading = AssetDatabase.LoadAssetAtPath<GradingConfig>(GradingCfgPath);
            var galleryCfg = AssetDatabase.LoadAssetAtPath<GalleryConfig>(GalleryCfgPath);
            var channel = AssetDatabase.LoadAssetAtPath<ShotCapturedChannel>(ChannelPath);
            var captureCfg = AssetDatabase.LoadAssetAtPath<CaptureConfig>(CaptureCfgPath);
            var cameraCfg = AssetDatabase.LoadAssetAtPath<CameraConfig>(CameraCfgPath);
            var actions = AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);
            var drunk = AssetDatabase.LoadAssetAtPath<GameObject>(DrunkPrefabPath);

            if (grading == null || galleryCfg == null || channel == null || drunk == null)
                throw new System.InvalidOperationException(
                    "GradingConfig / GalleryConfig / ShotCapturedChannel / drunk prefab failed to load after " +
                    "the scene swap.");

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
            // "Failed to create agent because there is no valid NavMesh" on every spawn — and a rig that
            // produces its own errors trains you to ignore errors. Baking also lets the actor genuinely
            // walk, so the photographs are of a moving subject.
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
            camGo.tag = "MainCamera";   // so Camera.main resolves for anything that falls back to it

            // Minimal viewfinder + flash so PhotoModeController finds everything it expects and the console
            // stays honest. Both Screen Space - Overlay, exactly as in SampleScene — which is also why
            // neither of them ever appears in a stored photograph.
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

            // --- The player object -----------------------------------------------------------------------
            // PhotoModeController and GalleryInput both live HERE, on the object carrying PlayerInput,
            // because Send Messages only calls OnXxx on its own GameObject. Putting either on the camera or
            // on the gallery canvas would make its input silently never fire — which is precisely what
            // Phase G exists to prove has not happened.
            var playerGo = new GameObject("Player");
            var photo = playerGo.AddComponent<PhotoModeController>();
            SetPrivate(photo, "photoCamera", cam);
            SetPrivate(photo, "cameraConfig", cameraCfg);
            SetPrivate(photo, "captureConfig", captureCfg);
            SetPrivate(photo, "shotCapturedChannel", channel);
            SetPrivate(photo, "gradingConfig", grading);
            SetPrivate(photo, "eventManager", manager);
            SetPrivate(photo, "viewfinderRoot", viewfinder);
            SetPrivate(photo, "captureFlash", flashGo.GetComponent<CanvasGroup>());

            var galleryInput = playerGo.AddComponent<GalleryInput>();

            PlayerInput playerInput = null;
            if (actions != null)
            {
                playerInput = playerGo.AddComponent<PlayerInput>();
                playerInput.actions = actions;
                playerInput.defaultActionMap = "Player";
                // BroadcastMessages, because that is what SampleScene's PlayerInput is actually set to.
                // The rig must reproduce the game's configuration, not a plausible-looking one: Broadcast
                // delivers OnXxx to the object AND its children while Send delivers only to the object, so
                // a rig on Send could pass while the real scene silently dropped every press (or vice
                // versa, if a handler ever ends up on a child).
                playerInput.notificationBehavior = PlayerNotifications.BroadcastMessages;
                playerInput.camera = cam;
            }
            else
            {
                Debug.LogWarning($"[GalleryShoot] {InputActionsPath} did not load — the real-input phase " +
                                 "will be skipped, everything else still runs.");
            }

            // --- The gallery itself ----------------------------------------------------------------------
            // Built HERE, in edit mode, rather than by the runner: components added to an already-active
            // GameObject run Awake immediately, before the rig could set their serialized fields, so a
            // runner-built gallery would Awake unwired and log its own missing-reference errors. A rig that
            // produces errors of its own teaches you to ignore errors. In edit mode Awake does not run at
            // all, so the fields below are simply in place when play mode starts — the same state the real
            // scene is in.
            var serviceGo = new GameObject("GalleryService");
            var service = serviceGo.AddComponent<GalleryService>();
            SetPrivate(service, "shotCapturedChannel", channel);
            SetPrivate(service, "galleryConfig", galleryCfg);
            SetPrivate(service, "photoCamera", cam);

            var viewGo = new GameObject("GalleryCanvas");
            var view = viewGo.AddComponent<GalleryView>();
            SetPrivate(view, "service", service);
            SetPrivate(view, "uiCamera", cam);
            SetPrivate(view, "photoMode", photo);

            SetPrivate(galleryInput, "view", view);

            // The runner lives in the RUNTIME assembly behind #if UNITY_EDITOR. It cannot live in the editor
            // assembly: Unity fails to resolve an editor-assembly MonoBehaviour on a scene object when
            // entering play mode, and the component comes back as "The referenced script (Unknown) is
            // missing!" while the rig silently does nothing and the scene runs on around it.
            var runnerGo = new GameObject("GalleryShootRunner");
            var runner = runnerGo.AddComponent<GalleryShootRunner>();
            runner.photo = photo;
            runner.manager = manager;
            runner.cam = cam;
            runner.cameraConfig = cameraCfg;
            runner.gradingConfig = grading;
            runner.shippedConfig = galleryCfg;
            runner.channel = channel;
            runner.galleryInput = galleryInput;
            runner.playerObject = playerGo;
            runner.service = service;
            runner.view = view;
            runner.outputDir = OutputDir;

            Debug.Log("[GalleryShoot] World built. Entering play mode — output lands in " + OutputDir);
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
            // verification root — cheap insurance against the one mistake in this file that could not be
            // undone. (Copied from PhotoShootRig for exactly the same reason.)
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

        /// <summary>Assigns a [SerializeField] private field by name, so the rig can wire the real
        /// components without widening their public API just for a bench.</summary>
        private static void SetPrivate(Object target, string field, object value)
        {
            var so = new SerializedObject(target);
            SerializedProperty p = so.FindProperty(field);
            if (p == null)
            {
                Debug.LogError($"[GalleryShoot] No serialized field '{field}' on {target.GetType().Name}.");
                return;
            }

            switch (value)
            {
                case int i: p.intValue = i; break;
                case float f: p.floatValue = f; break;
                case Object o: p.objectReferenceValue = o; break;
                case null: p.objectReferenceValue = null; break;

                default:
                    Debug.LogError($"[GalleryShoot] Unsupported type '{value.GetType().Name}' for '{field}'.");
                    return;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
