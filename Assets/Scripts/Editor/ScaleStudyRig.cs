using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CameraGame.Events;
using CameraGame.Player;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// Story 2.1's scale study. Opens the real town, STAGES one option in the UNSAVED scene, adds a
    /// <see cref="ScaleStudyRunner"/> and enters play mode. When play mode ends the town is reopened from disk,
    /// which discards every staged change — the scene is never saved here (do not add a save).
    ///
    /// Options:
    ///   C     — the town and the characters exactly as they are.
    ///   A     — the characters scaled to the town: the player's model ×1/F, its CharacterController set to the
    ///           candidate body, and the eye point/forward nudge staged as a "head bone" marker so the REAL
    ///           ThirdPersonCamera places the camera (it prefers a head bone over its 8.5 m fallback).
    ///   B     — the town scaled to the characters: the map root ×F about the origin, the route and spawn ×F.
    ///   After — whatever the project holds now (the applied option), staged with nothing.
    ///
    /// The return path survives the play-mode domain reloads in SessionState, stamped with the time it was
    /// armed so a refused EnterPlaymode cannot arm a later, unrelated Play press (lessons 37).
    /// </summary>
    public static class ScaleStudyRig
    {
        public const string Root = "_bmad-output/verification/2-1-settle-the-players-scale-in-the-town";
        private const string TownScenePath = "Assets/Scenes/SampleScene.unity";
        private const string DrunkPrefabPath = "Assets/Prefabs/Events/EventActor_Drunk.prefab";
        private const string ReturnKey = "ScaleStudy.ReturnScene";
        private const string StampKey = "ScaleStudy.ArmedAt";
        private const double ArmedLifetimeSeconds = 2 * 3600;

        /// <summary>The measured factor: the 8.86 m characters ÷ a 1.77 m person (Task 1a/1b).</summary>
        public const float Factor = 5f;

        // (A)'s candidate body, Task 1b. Everything in metres.
        public const float A_Height = 1.77f, A_Radius = 0.30f, A_Skin = 0.03f, A_Step = 0.35f, A_Eye = 1.65f;
        public const float TodayRadius = 0.95f, TodayNudge = 1f, TodayNear = 0.3f;
        public static float A_Nudge => TodayNudge * A_Radius / TodayRadius;
        public static float A_Near => TodayNear * A_Radius / TodayRadius;

        // the cue sweep, design metres (AC3)
        public static readonly float[] CueDesign = { 0.5f, 1f, 2f, 4f, 8f, 12f, 16f, 20f, 24f, 25f, 26f, 30f, 40f, 60f };

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredEditMode) return;
            string previous = SessionState.GetString(ReturnKey, string.Empty);
            float armedAt = SessionState.GetFloat(StampKey, -1f);
            SessionState.EraseString(ReturnKey);
            SessionState.EraseFloat(StampKey);
            if (string.IsNullOrEmpty(previous)) return;
            if (armedAt < 0f || EditorApplication.timeSinceStartup - armedAt > ArmedLifetimeSeconds)
            {
                Debug.LogWarning("[ScaleStudy] Stale return key ignored (armed too long ago).");
                return;
            }
            if (File.Exists(previous))
            {
                EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                Debug.Log($"[ScaleStudy] Run finished — reopened {previous} from disk (staging discarded).");
            }
        }

        [MenuItem("Tools/Scale/Measure Start (Play)")] public static void MeasureStart() => Run("measure", "C", "today", "start");
        [MenuItem("Tools/Scale/Photos C (Play)")] public static void PhotosC() => Run("photos", "C", "today", "options/C");
        [MenuItem("Tools/Scale/Photos A (Play)")] public static void PhotosA() => Run("photos", "A", "today", "options/A");
        [MenuItem("Tools/Scale/Photos B (Play)")] public static void PhotosB() => Run("photos", "B", "today", "options/B");
        [MenuItem("Tools/Scale/Photos After (Play)")] public static void PhotosAfter() => Run("photos", "After", "today", "before-after/after");
        [MenuItem("Tools/Scale/Crossing C (Play)")] public static void CrossC() => Run("crossing", "C", "today", "crossing/C-today");
        [MenuItem("Tools/Scale/Crossing A today (Play)")] public static void CrossA1() => Run("crossing", "A", "today", "crossing/A-today");
        [MenuItem("Tools/Scale/Crossing A human (Play)")] public static void CrossA2() => Run("crossing", "A", "human", "crossing/A-human");
        [MenuItem("Tools/Scale/Crossing B x5 (Play)")] public static void CrossB2() => Run("crossing", "B", "x5", "crossing/B-x5");
        [MenuItem("Tools/Scale/Crossing After (Play)")] public static void CrossAfter() => Run("crossing", "After", "today", "crossing/After");
        [MenuItem("Tools/Scale/Movement C (Play)")] public static void MoveC() => Run("movement", "C", "today", "movement/before");
        [MenuItem("Tools/Scale/Movement A (Play)")] public static void MoveA() => Run("movement", "A", "today", "movement/A");
        [MenuItem("Tools/Scale/Movement After (Play)")] public static void MoveAfter() => Run("movement", "After", "today", "movement/after");
        // Fail-soft (NFR8), provoked ON PURPOSE: each must warn exactly once and the game must keep running.
        [MenuItem("Tools/Scale/Fail-soft - no PlayerConfig (Play)")] public static void FailNone() => Run("movement", "FailNone", "today", "failsoft/no-config");
        [MenuItem("Tools/Scale/Fail-soft - eye above body (Play)")] public static void FailBody() => Run("movement", "FailBody", "today", "failsoft/eye-above-body");
        [MenuItem("Tools/Scale/Cue Sweep C (Play)")] public static void CueC() => Run("cue", "C", "today", "cue/before");
        [MenuItem("Tools/Scale/Cue Sweep After (Play)")] public static void CueAfter() => Run("cue", "After", "today", "cue/after");

        public static void Run(string mode, string option, string speeds, string subdir)
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[ScaleStudy] Already in play mode — stop first."); return; }
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (!SceneManager.GetSceneAt(i).isDirty) continue;
                Debug.LogWarning($"[ScaleStudy] Scene '{SceneManager.GetSceneAt(i).name}' has unsaved changes — save it first; nothing was changed.");
                return;
            }
            string spotsPath = Path.Combine(Root, "spots.json");
            if (!File.Exists(spotsPath)) { Debug.LogError("[ScaleStudy] No spots.json at " + spotsPath); return; }

            string previous = SceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(previous)) previous = TownScenePath;
            SessionState.SetString(ReturnKey, previous);
            SessionState.SetFloat(StampKey, (float)EditorApplication.timeSinceStartup);

            try
            {
                EditorSceneManager.OpenScene(TownScenePath, OpenSceneMode.Single);   // from disk, every run
                float worldScale = Stage(mode, option, speeds);

                var go = new GameObject("ScaleStudyRunner (rig)");
                var runner = go.AddComponent<ScaleStudyRunner>();
                runner.mode = mode;
                runner.option = option;
                runner.speedLabel = speeds;
                runner.outputDir = Path.Combine(Root, subdir).Replace('\\', '/');
                runner.spotsJson = File.ReadAllText(spotsPath);
                runner.worldScale = worldScale;
                runner.drunkPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DrunkPrefabPath);
                runner.drunkArmatureScale = option == "A" ? 0.01f / Factor : DrunkArmatureNow();
                runner.cueDistances = (float[])CueDesign.Clone();
                if (mode == "cue" && option == "C")
                    for (int i = 0; i < runner.cueDistances.Length; i++) runner.cueDistances[i] *= Factor;   // the old world's metres

                Debug.Log($"[ScaleStudy] {mode} / option {option} / speeds {speeds} staged — entering play mode, output {runner.outputDir}");
                EditorApplication.EnterPlaymode();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[ScaleStudy] Staging failed — restoring your scene.\n" + e);
                SessionState.EraseString(ReturnKey);
                SessionState.EraseFloat(StampKey);
                if (File.Exists(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            }
        }

        private static float DrunkArmatureNow()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DrunkPrefabPath);
            Transform arm = prefab != null ? prefab.transform.Find("Armature") : null;
            return arm != null ? arm.localScale.x : 0.01f;
        }

        /// <summary>Mutates the open (unsaved) town for one option. Returns the town's scale.</summary>
        private static float Stage(string mode, string option, string speeds)
        {
            GameObject player = GameObject.Find("main characters");
            var tpc = player.GetComponent<ThirdPersonController>();
            var cc = player.GetComponent<CharacterController>();
            Camera cam = Camera.main;
            var manager = Object.FindFirstObjectByType<EventManager>(FindObjectsInactive.Include);

            // Photos, crossings and movement want an empty street; measure and cue want the live drunk.
            if (manager != null) manager.enabled = mode == "measure" || mode == "cue";

            var so = new SerializedObject(tpc);
            float walk = so.FindProperty("walkSpeed") != null ? so.FindProperty("walkSpeed").floatValue : float.NaN;
            float sprint = so.FindProperty("sprintSpeed") != null ? so.FindProperty("sprintSpeed").floatValue : float.NaN;

            float worldScale = 1f;
            switch (option)
            {
                case "A":
                {
                    float s = 1f / Factor;
                    foreach (Transform c in player.transform) { c.localPosition *= s; c.localScale *= s; }
                    cc.height = A_Height;
                    cc.radius = A_Radius;
                    cc.center = new Vector3(0f, A_Height * 0.5f, 0f);
                    cc.skinWidth = A_Skin;
                    cc.stepOffset = A_Step;
                    SetFloat(so, "stepOffset", A_Step);
                    if (speeds == "human") { walk = 4f; sprint = 7f; }

                    // The REAL ThirdPersonCamera looks up this exact path first and uses it instead of its 8.5 m
                    // fallback; camera = marker + forward × 1 m (its literal), so the marker sits (nudge − 1) ahead.
                    Transform t = player.transform;
                    foreach (string n in new[] { "Armature", "Hips", "Spine", "Chest", "Neck", "Head", "Head_end" })
                    {
                        var child = new GameObject(n).transform;
                        child.SetParent(t, false);
                        t = child;
                    }
                    t.localPosition = new Vector3(0f, A_Eye, A_Nudge - TodayNudge);
                    cam.nearClipPlane = A_Near;
                    break;
                }
                case "FailNone":
                {
                    SetRef(so, "config", null);
                    SetRef(new SerializedObject(cam.GetComponent<ThirdPersonCamera>()), "config", null);
                    break;
                }
                case "FailBody":
                {
                    cc.height = 1.0f;                                   // the 1.65 m eye no longer fits the body
                    cc.center = new Vector3(0f, 0.5f, 0f);
                    break;
                }
                case "B":
                {
                    worldScale = Factor;
                    GameObject map = GameObject.Find("game map");
                    map.transform.localScale *= Factor;
                    GameObject route = GameObject.Find("DrunkRoute");
                    if (route != null) foreach (Transform w in route.transform) w.position *= Factor;
                    GameObject spawn = GameObject.Find("SpawnPoint");
                    if (spawn != null) spawn.transform.position *= Factor;
                    cam.farClipPlane *= Factor;
                    if (speeds == "x5") { walk *= Factor; sprint *= Factor; }
                    break;
                }
            }

            SetFloat(so, "walkSpeed", walk);
            SetFloat(so, "sprintSpeed", sprint);
            so.ApplyModifiedPropertiesWithoutUndo();
            return worldScale;
        }

        private static void SetRef(SerializedObject so, string name, Object v)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p == null) return;
            p.objectReferenceValue = v;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(SerializedObject so, string name, float v)
        {
            SerializedProperty p = so.FindProperty(name);
            if (p != null && !float.IsNaN(v)) p.floatValue = v;
        }
    }
}
