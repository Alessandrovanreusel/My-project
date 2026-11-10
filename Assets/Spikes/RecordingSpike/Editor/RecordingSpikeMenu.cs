// THROWAWAY SPIKE CODE (Story 1.14). Never referenced by game code. See ../README.md.
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CameraGame.Spikes.RecordingSpike.Editor
{
    /// <summary>
    /// Editor side of the recording spike: the two player builds (Task 2.6) and the in-Editor survey and
    /// control-shot runs (Tasks 1a, 2.3). Every run records what it changes and puts it back.
    /// </summary>
    public static class RecordingSpikeMenu
    {
        private const string Slug = "1-14-decide-how-the-game-records-video";
        private const string Scene = "Assets/Scenes/SampleScene.unity";
        private static string Root => Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        public static string Evidence => Path.Combine(Root, "_bmad-output", "verification", Slug).Replace('\\', '/');

        [MenuItem("Tools/Recording Spike/Build Spike Player")]
        public static void BuildSpike() => Build("spike", new[] { "CAMERA_RECORDING_SPIKE" });

        [MenuItem("Tools/Recording Spike/Build Plain Player")]
        public static void BuildPlain() => Build("plain", new string[0]);

        private static void Build(string flavour, string[] defines)
        {
            var log = new StringBuilder();
            var target = EditorUserBuildSettings.activeBuildTarget;
            string definesBefore = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
            log.AppendLine($"flavour={flavour} activeBuildTarget={target} definesBefore='{definesBefore}' extraScriptingDefines='{string.Join(";", defines)}'");
            if (target != BuildTarget.StandaloneWindows64)
            {
                // Never switch platforms: that reimports the whole project.
                log.AppendLine("REFUSED: active build target is not StandaloneWindows64.");
                Write(flavour, log); return;
            }
            string exe = Path.Combine(Root, "Builds", Slug, flavour, "My project.exe");
            var opts = new BuildPlayerOptions
            {
                scenes = new[] { Scene },
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None,
                locationPathName = exe,
                extraScriptingDefines = defines,
            };
            var report = BuildPipeline.BuildPlayer(opts);
            var s = report.summary;
            log.AppendLine($"result={s.result} duration={s.totalTime} totalSize={s.totalSize} errors={s.totalErrors} warnings={s.totalWarnings} output={s.outputPath}");
            foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if (m.type == LogType.Error || m.type == LogType.Warning || m.type == LogType.Exception)
                        log.AppendLine($"[{m.type}] {step.name}: {m.content}");
            string definesAfter = PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Standalone);
            log.AppendLine($"definesAfter='{definesAfter}' (unchanged={definesAfter == definesBefore})");
            string managed = Path.Combine(Path.GetDirectoryName(exe), "My project_Data", "Managed");
            if (Directory.Exists(managed))
            {
                log.AppendLine("--- Managed/ listing ---");
                foreach (var f in Directory.GetFiles(managed).Select(Path.GetFileName).OrderBy(n => n)) log.AppendLine(f);
            }
            Write(flavour, log);
        }

        private static void Write(string flavour, StringBuilder log)
        {
            string dir = Path.Combine(Evidence, "build");
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"BuildReport-{flavour}.txt"), log.ToString());
            Debug.Log($"[RecordingSpike] build {flavour}: " + log.ToString().Split('\n')[1]);
        }

        // ---------- in-Editor runs ----------
        private const string ReturnKey = "CameraGame.RecordingSpike.ReturnScene";

        [MenuItem("Tools/Recording Spike/Survey Peak and Vantages (Play)")]
        public static void Survey() => Play("-recordingSpike survey -spikeTag survey -timeout 200 -spikeOut " + Esc(Path.Combine(Evidence, "survey")));

        [MenuItem("Tools/Recording Spike/Control Shot (Play)")]
        public static void Control() => Play("-recordingSpike control -spikeTag control -timeout 60 -spikeOut " + Esc(Path.Combine(Evidence, "survey")) + " " + ExtraArgs());

        /// <summary>Shake-down run in the Editor with the arguments in editor-dev-args.txt (Editor numbers are NOT
        /// evidence for NFR1; they only prove the pipeline before a build is spent on it).</summary>
        [MenuItem("Tools/Recording Spike/Dev Run (Play)")]
        public static void Dev()
        {
            string p = Path.Combine(Evidence, "editor-dev-args.txt");
            string args = File.Exists(p) ? File.ReadAllText(p).Trim() : "-recordingSpike baseline";
            Play(args + " -spikeOut " + Esc(Path.Combine(Evidence, "dev-editor")) + " " + ExtraArgs());
        }

        /// <summary>Code review 2026-11-10: ghost-vs-live bone drift on EVERY tick of two takes across the peak (the
        /// story measured 3 frames, none in a crossfade). Editor play mode is fine here: this is animation
        /// fidelity, not frame cost. Output in review/.</summary>
        [MenuItem("Tools/Recording Spike/Review - Replay Fidelity Sweep (Play)")]
        public static void FidelityReview() => Play("-recordingSpike B -spikeTag review-fidelity -cycles 2 -fidelity -noexport -timeout 240 -spikeOut " + Esc(Path.Combine(Evidence, "review")) + " " + ExtraArgs());

        /// <summary>Extra arguments (for example the chosen vantage) kept in a text file next to the evidence.</summary>
        public static string ExtraArgs()
        {
            string p = Path.Combine(Evidence, "editor-extra-args.txt");
            return File.Exists(p) ? File.ReadAllText(p).Trim() : "";
        }

        private static string Esc(string path) => path.Replace('\\', '/').Replace(' ', '|');

        private static void Play(string args)
        {
            if (EditorApplication.isPlaying) return;
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != Scene || scene.isDirty)
            {
                Debug.LogWarning("[RecordingSpike] open SampleScene, un-dirtied, before running the spike (it never saves the scene).");
                return;
            }
            SessionState.SetString(SpikeArgs.SessionKey, args);
            SessionState.SetFloat(SpikeArgs.ArmedAtKey, (float)EditorApplication.timeSinceStartup);
            SessionState.SetString(ReturnKey, scene.path);
            EditorApplication.EnterPlaymode();
        }

        [InitializeOnLoadMethod]
        private static void Hook()
        {
            EditorApplication.playModeStateChanged += s =>
            {
                if (s != PlayModeStateChange.EnteredEditMode) return;
                if (string.IsNullOrEmpty(SessionState.GetString(SpikeArgs.SessionKey, ""))) return;
                // Disarm so a normal play session never runs the harness, and confirm the scene is clean.
                SessionState.EraseString(SpikeArgs.SessionKey);
                SessionState.EraseFloat(SpikeArgs.ArmedAtKey);
                string back = SessionState.GetString(ReturnKey, "");
                SessionState.EraseString(ReturnKey);
                var scene = EditorSceneManager.GetActiveScene();
                if (!string.IsNullOrEmpty(back) && scene.path != back) EditorSceneManager.OpenScene(back);
                Debug.Log($"[RecordingSpike] editor run finished; disarmed; scene={EditorSceneManager.GetActiveScene().path} dirty={EditorSceneManager.GetActiveScene().isDirty}");
            };
        }
    }
}
