using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CameraGame.Events;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// Runs the real town for one full event lifecycle and records what the drunk's NavMeshAgent
    /// actually did — see <see cref="RouteTraceRunner"/> for why neither existing rig can answer
    /// that question.
    ///
    /// Puts the developer back in their own scene when play mode ends, exactly as the photo-shoot
    /// rig does: the return path is stored in SessionState because it must survive the domain
    /// reloads that entering and leaving play mode cause.
    /// </summary>
    public static class RouteTraceRig
    {
        private const string TownScenePath = "Assets/Scenes/SampleScene.unity";
        private const string OutputDir = "_bmad-output/verification/route-trace";
        private const string ReturnSceneKey = "RouteTrace.ReturnScene";

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
                Debug.Log($"[RouteTrace] Trace finished — reopened {previous}.");
            }
        }

        [MenuItem("Tools/Events/Trace Actor Route (Play)")]
        public static void Run() => Run(95f, OutputDir);

        // Story 2.1 review: a long run (7 pooled lifecycles) — bugs here hide in reuse, from the second cycle on.
        [MenuItem("Tools/Events/Trace Actor Route - 7 cycles (Play)")]
        public static void RunLong() =>
            Run(190f, "_bmad-output/verification/2-1-settle-the-players-scale-in-the-town/review/route-7cycles");

        // Story 2.2: the drunk before and after the whole-town bake — 3+ pooled lifecycles each, in 2.2's own folders
        // (2.1's evidence is never overwritten).
        [MenuItem("Tools/Events/Trace Actor Route - 2.2 before (Play)")]
        public static void Run22Before() =>
            Run(95f, "_bmad-output/verification/2-2-walk-the-whole-town/route/before");

        [MenuItem("Tools/Events/Trace Actor Route - 2.2 after (Play)")]
        public static void Run22After() =>
            Run(95f, "_bmad-output/verification/2-2-walk-the-whole-town/route/after");

        private static void Run(float seconds, string outputDir)
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[RouteTrace] Already in play mode — stop first.");
                return;
            }

            // Every loaded scene, not just the active one: opening the town discards them all.
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene s = SceneManager.GetSceneAt(i);
                if (!s.isDirty) continue;

                EditorUtility.DisplayDialog("Route Trace",
                    $"Scene '{s.name}' has unsaved changes. Save it first — this swaps scenes.", "OK");
                return;
            }

            string current = SceneManager.GetActiveScene().path;
            if (!string.IsNullOrEmpty(current))
                SessionState.SetString(ReturnSceneKey, current);

            EditorSceneManager.OpenScene(TownScenePath, OpenSceneMode.Single);

            var manager = Object.FindFirstObjectByType<EventManager>(FindObjectsInactive.Include);
            if (manager == null)
            {
                Debug.LogError("[RouteTrace] The town scene has no EventManager — nothing to trace.");
                SessionState.EraseString(ReturnSceneKey);
                return;
            }

            // Leave the player and chase camera alone: this rig only observes, it never poses
            // anything, so there is nothing for them to fight over.
            var go = new GameObject("RouteTraceRunner");
            var runner = go.AddComponent<RouteTraceRunner>();
            runner.manager = manager;
            runner.outputDir = outputDir;
            // Story 2.1: at least THREE consecutive pooled lifecycles (24.5 s + 2 s respawn each). Bugs in this
            // project have hidden in the second cycle onward, never the first.
            runner.seconds = seconds;

            Debug.Log($"[RouteTrace] Tracing the town for {runner.seconds}s — output in {outputDir}");
            EditorApplication.EnterPlaymode();
        }
    }
}
