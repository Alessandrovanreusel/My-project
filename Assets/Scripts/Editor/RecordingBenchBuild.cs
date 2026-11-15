using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Compilation;
using UnityEngine;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// Story 1.15, Task 11: builds the two Windows release players the AC3 measurement needs, into
    /// Builds/1-15-record-a-clip/ (git-ignored):
    ///   bench/RecordingBench.exe — with CAMERA_RECORDING_BENCH passed through extraScriptingDefines for THIS build
    ///                              only (Player Settings untouched), so the fenced bench assembly is compiled in;
    ///   plain/CameraGame.exe     — a normal build, whose Managed/ listing proves the fence (no bench DLL).
    /// Writes _bmad-output/verification/1-15-record-a-clip/bench/build-result.txt. A build re-serialises project
    /// settings (loop lessons, 1-14): restore them from HEAD afterwards.
    /// </summary>
    public static class RecordingBenchBuild
    {
        public const string Define = "CAMERA_RECORDING_BENCH";
        public const string BenchAssembly = "CameraGame.Bench.RecordingBench";
        private const string Root = "Builds/1-15-record-a-clip";
        private const string Result = "_bmad-output/verification/1-15-record-a-clip/bench/build-result.txt";

        [MenuItem("Tools/Recording/Build Recording Bench (Windows)")]
        public static void Build()
        {
            var log = new StringBuilder();
            Directory.CreateDirectory(Path.GetDirectoryName(Result));
            File.WriteAllText(Result, "building…\n");

            var player = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies);
            log.AppendLine($"Plain player compilation (current Player Settings defines) includes {BenchAssembly}: {player.Any(a => a.name == BenchAssembly)}");

            string[] scenes = { "Assets/Scenes/SampleScene.unity" };
            log.AppendLine(One(new BuildPlayerOptions
            {
                scenes = scenes,
                target = BuildTarget.StandaloneWindows64,
                locationPathName = Root + "/bench/RecordingBench.exe",
                options = BuildOptions.None,
                extraScriptingDefines = new[] { Define },
            }, "bench"));
            log.AppendLine(One(new BuildPlayerOptions
            {
                scenes = scenes,
                target = BuildTarget.StandaloneWindows64,
                locationPathName = Root + "/plain/CameraGame.exe",
                options = BuildOptions.None,
            }, "plain"));

            foreach (string kind in new[] { "bench/RecordingBench_Data", "plain/CameraGame_Data" })
            {
                string managed = Path.Combine(Root, kind, "Managed");
                string[] dlls = Directory.Exists(managed)
                    ? Directory.GetFiles(managed, "CameraGame*.dll").Select(Path.GetFileName).OrderBy(n => n).ToArray()
                    : new string[0];
                log.AppendLine($"{kind}/Managed CameraGame*.dll: {string.Join(", ", dlls)}");
            }
            File.WriteAllText(Result, log.ToString());
            Debug.Log("[RecordingBenchBuild] " + log.ToString().Replace("\n", " | "));
        }

        private static string One(BuildPlayerOptions o, string label)
        {
            BuildReport r = BuildPipeline.BuildPlayer(o);
            return $"{label}: {r.summary.result}, {r.summary.totalSize / 1048576.0:0.0} MB, {r.summary.totalTime.TotalSeconds:0} s, " +
                   $"errors {r.summary.totalErrors}, defines [{string.Join(" ", o.extraScriptingDefines ?? new string[0])}] -> {o.locationPathName}";
        }
    }
}
