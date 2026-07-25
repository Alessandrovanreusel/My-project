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
    /// Tools > Grading > Photo Shoot (Play). Output: Temp/PhotoShoot/ (git-ignored) — one PNG per vantage
    /// point plus shots.txt pairing each picture with the verdict the game gave it.
    /// </summary>
    public static class PhotoShootRig
    {
        public const string OutputDir = "Temp/PhotoShoot";

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

            if (SceneManager.GetActiveScene().isDirty)
            {
                EditorUtility.DisplayDialog("Photo Shoot",
                    "The open scene has unsaved changes. Save it first — this swaps scenes.", "OK");
                return;
            }

            if (Directory.Exists(OutputDir)) Directory.Delete(OutputDir, true);
            Directory.CreateDirectory(OutputDir);

            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Load AFTER the scene swap. Opening a scene in Single mode unloads unused assets, destroying
            // the native half of anything loaded beforehand — a ScriptableObject in that state still reads
            // its public fields from managed memory while `== null` is simultaneously true.
            var grading = AssetDatabase.LoadAssetAtPath<GradingConfig>(ConfigPath);
            var channel = AssetDatabase.LoadAssetAtPath<ShotCapturedChannel>(ChannelPath);
            var captureCfg = AssetDatabase.LoadAssetAtPath<CaptureConfig>(CaptureCfgPath);
            var cameraCfg = AssetDatabase.LoadAssetAtPath<CameraConfig>(CameraCfgPath);
            var drunk = AssetDatabase.LoadAssetAtPath<GameObject>(DrunkPrefabPath);

            if (grading == null || channel == null || drunk == null)
            {
                Debug.LogError("[PhotoShoot] Missing GradingConfig / ShotCapturedChannel / drunk prefab.");
                return;
            }

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
            SetPrivate(manager, "actorPrefab", drunk.GetComponent<EventActor>());
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
                default: Debug.LogError($"[PhotoShoot] Unsupported type for '{field}'."); return;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
