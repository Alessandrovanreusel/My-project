using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CameraGame.World;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// Story 2.2's play-mode rigs (<c>Tools/Town/*</c>). Opens the real town FROM DISK, adds a
    /// <see cref="TownRigRunner"/> (runtime assembly, #if UNITY_EDITOR), optionally stages a change in the UNSAVED
    /// scene, and enters play mode. When play mode ends the previous scene is reopened from disk, which discards
    /// every staged change — this rig never saves a scene (do not add a save).
    ///
    /// The return path survives the play-mode domain reloads in SessionState, stamped with the time it was armed, so
    /// a refused EnterPlaymode cannot arm a later, unrelated Play press (lesson 37).
    /// </summary>
    public static class TownRig
    {
        private const string TownScenePath = "Assets/Scenes/SampleScene.unity";
        private const string ReturnKey = "TownRig.ReturnScene";
        private const string StampKey = "TownRig.ArmedAt";
        private const double ArmedLifetimeSeconds = 2 * 3600;

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
                Debug.LogWarning("[TownRig] Stale return key ignored (armed too long ago).");
                return;
            }
            if (File.Exists(previous))
            {
                EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
                Debug.Log($"[TownRig] Run finished — reopened {previous} from disk (staging discarded).");
            }
        }

        private static string Out(string sub) => (TownCensus.Root + "/" + sub).Replace('\\', '/');

        [MenuItem("Tools/Town/FPS Tour - before (Play)")] public static void FpsBefore() => Run("fps", Out("fps/before"));
        [MenuItem("Tools/Town/FPS Tour - after (Play)")] public static void FpsAfter() => Run("fps", Out("fps/after"));
        [MenuItem("Tools/Town/Walk Lines - before (Play)")] public static void WalkBefore() => Run("walklines", Out("walk/before"));
        [MenuItem("Tools/Town/Walk Lines - after (Play)")] public static void WalkAfter() => Run("walklines", Out("walk/after/lines"));
        [MenuItem("Tools/Town/Wall Press - before (Play)")] public static void WallsBefore() => Run("walls", Out("walls/before"));
        [MenuItem("Tools/Town/Wall Press - after (Play)")] public static void WallsAfter() => Run("walls", Out("walls/after"));
        [MenuItem("Tools/Town/Edge Walks (Play)")] public static void Edges() => Run("edges", Out("edges"));
        [MenuItem("Tools/Town/Fall Recovery (Play)")] public static void Fall() => Run("fall", Out("fall"));
        [MenuItem("Tools/Town/Fall Recovery - fail-soft no config (Play)")] public static void FallNoCfg() => Run("failsoft", Out("fall/failsoft-no-config"), "noconfig");
        [MenuItem("Tools/Town/Fall Recovery - fail-soft invalid config (Play)")] public static void FallBadCfg() => Run("failsoft", Out("fall/failsoft-invalid-config"), "badconfig");
        [MenuItem("Tools/Town/Fall Recovery - fail-soft floor above the ground (Play)")] public static void FallHighFloor() => Run("failsoft", Out("fall/failsoft-floor-above-ground"), "highfloor");
        [MenuItem("Tools/Town/NavMesh Paths (Play)")] public static void Paths() => Run("paths", Out("navmesh"));
        [MenuItem("Tools/Town/NavMesh Paths - chain walk (Play)")] public static void PathsChains() => Run("paths", Out("navmesh/chain-walk"), "chains");
        [MenuItem("Tools/Town/Warp Guard - provoked (Play)")] public static void Warp() => Run("warp", Out("navmesh/warp-guard"), "spawnoffmesh");
        [MenuItem("Tools/Town/Walk the Whole Town (Play)")] public static void Tour() => Run("tour", Out("walk/after/walk"), "walk");
        [MenuItem("Tools/Town/Walk the Whole Town - sprint (Play)")] public static void TourSprint() => Run("tour", Out("walk/after/sprint"), "sprint");
        [MenuItem("Tools/Town/Film Shore and Hill (Play)")] public static void Film() => Run("film", Out("film"));
        [MenuItem("Tools/Town/Review - FallRecovery spawn outside the area (Play)")] public static void ReviewOutside() => Run("review", Out("review/outside"), "outside");
        [MenuItem("Tools/Town/Review - FallRecovery ring and backstop (Play)")] public static void ReviewRing() => Run("review", Out("review/ring"), "ring");

        public static void Run(string mode, string outputDir, string staging = "")
        {
            if (EditorApplication.isPlaying) { Debug.LogWarning("[TownRig] Already in play mode — stop first."); return; }
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                if (!SceneManager.GetSceneAt(i).isDirty) continue;
                Debug.LogWarning($"[TownRig] Scene '{SceneManager.GetSceneAt(i).name}' has unsaved changes — save it first; nothing was changed.");
                return;
            }
            string previous = SceneManager.GetActiveScene().path;
            if (string.IsNullOrEmpty(previous)) previous = TownScenePath;
            SessionState.SetString(ReturnKey, previous);
            SessionState.SetFloat(StampKey, (float)EditorApplication.timeSinceStartup);
            try
            {
                EditorSceneManager.OpenScene(TownScenePath, OpenSceneMode.Single);   // from disk, every run
                var go = new GameObject("TownRigRunner (rig)");
                var runner = go.AddComponent<TownRigRunner>();
                runner.mode = mode;
                runner.staging = staging;
                runner.outputDir = outputDir;
                runner.dataDir = TownCensus.Root;
                Stage(runner, staging);
                Debug.Log($"[TownRig] {mode} {staging} staged — entering play mode, output {outputDir}");
                EditorApplication.EnterPlaymode();
            }
            catch (System.Exception e)
            {
                Debug.LogError("[TownRig] Staging failed — restoring your scene.\n" + e);
                SessionState.EraseString(ReturnKey);
                SessionState.EraseFloat(StampKey);
                if (File.Exists(previous)) EditorSceneManager.OpenScene(previous, OpenSceneMode.Single);
            }
        }

        /// <summary>Mutates the open (unsaved) town for a fail-soft scenario. Discarded when play mode ends.</summary>
        private static void Stage(TownRigRunner runner, string staging)
        {
            if (staging == "noconfig" || staging == "badconfig" || staging == "highfloor")
            {
                var fr = Object.FindFirstObjectByType<FallRecovery>(FindObjectsInactive.Include);
                if (fr == null) return;
                var so = new SerializedObject(fr);
                SerializedProperty p = so.FindProperty("config");
                if (staging == "noconfig") p.objectReferenceValue = null;
                else if (staging == "highfloor")
                {
                    // every value valid EXCEPT the floor of the world, typed above the plain (y ~0.9): accepted as typed it
                    // would put back a player standing on open ground every frame
                    var high = ScriptableObject.Instantiate((TownBoundsConfig)p.objectReferenceValue);
                    high.name = "TownBoundsConfig (rig: floor above the ground on purpose)";
                    high.belowWorldY = 50f;
                    p.objectReferenceValue = high;
                }
                else
                {
                    var bad = ScriptableObject.CreateInstance<TownBoundsConfig>();
                    bad.name = "TownBoundsConfig (rig: invalid on purpose)";
                    bad.belowWorldY = float.NaN;
                    bad.maxAirborneSeconds = float.PositiveInfinity;
                    bad.safeSampleInterval = float.NegativeInfinity;
                    bad.minRestoreAge = -1f;
                    bad.playableArea = new Vector2[] { Vector2.zero, Vector2.one };   // not a polygon
                    p.objectReferenceValue = bad;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else if (staging == "outside")
            {
                // review: a VALID polygon (the default shifted 2,000 m east) that does not contain the spawn
                var fr = Object.FindFirstObjectByType<FallRecovery>(FindObjectsInactive.Include);
                if (fr == null) return;
                var so = new SerializedObject(fr);
                SerializedProperty p = so.FindProperty("config");
                var shifted = ScriptableObject.Instantiate((TownBoundsConfig)p.objectReferenceValue);
                shifted.name = "TownBoundsConfig (rig: spawn outside the area on purpose)";
                Vector2[] poly = TownBoundsConfig.DefaultPlayableArea;
                shifted.playableArea = new Vector2[poly.Length];
                for (int i = 0; i < poly.Length; i++) shifted.playableArea[i] = poly[i] + new Vector2(2000f, 0f);
                p.objectReferenceValue = shifted;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            else if (staging == "spawnoffmesh")
            {
                GameObject spawn = GameObject.Find("SpawnPoint");
                if (spawn != null) spawn.transform.position = new Vector3(-380f, 0.5f, -229f);   // over the hole: no NavMesh
            }
        }
    }
}
