using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using AssembliesType = UnityEditor.Compilation.AssembliesType;
using CompilationPipeline = UnityEditor.Compilation.CompilationPipeline;

namespace CameraGame.Tests
{
    /// <summary>
    /// Story 1.15 fence guard for the AC3 frame-rate bench (Assets/Bench/RecordingBench): the same four guards
    /// as the 1.14 spike's (RecordingSpikeFenceTests). The bench may reference game code; game code must never
    /// reference the bench, and a plain player build must not compile it. The build-level proof is the plain
    /// build's Managed/ listing (bench/build-result.txt in the story's evidence).
    /// </summary>
    public class RecordingBenchFenceTests
    {
        private const string Bench = "CameraGame.Bench.RecordingBench";

        private static Assembly Loaded(string name) =>
            AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);

        private static bool References(Assembly a, string target) =>
            a.GetReferencedAssemblies().Any(r => r.Name == target);

        [TestCase("CameraGame")]
        [TestCase("CameraGame.Editor")]
        public void GameAssembly_DoesNotReferenceTheBench(string game)
        {
            var asm = Loaded(game);
            Assert.That(asm, Is.Not.Null, $"{game} is not loaded - the guard would pass vacuously.");
            Assert.That(References(asm, Bench), Is.False, $"{game} references {Bench}: the bench is no longer fenced off.");
        }

        /// <summary>Declared references, resolved by Unity (a "GUID:" reference too). Positive control: the bench
        /// itself declares CameraGame, so the check must see a declared reference when there is one.</summary>
        [Test]
        public void NoAssembly_DeclaresAReferenceToTheBench()
        {
            var all = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            var bench = all.FirstOrDefault(a => a.name == Bench);
            Assert.That(bench, Is.Not.Null, $"{Bench} is not in the compilation - the guard would pass vacuously.");
            Assert.That(bench.assemblyReferences.Any(r => r.name == "CameraGame"), Is.True,
                "Positive control: the bench declares CameraGame, so the check must see it.");

            var offenders = all.Where(a => a.name != Bench && a.assemblyReferences.Any(r => r.name == Bench)).Select(a => a.name).ToArray();
            Assert.That(offenders, Is.Empty, $"These assemblies reference {Bench}: {string.Join(", ", offenders)}");
        }

        /// <summary>The fence: what a plain player build compiles under the CURRENT Player Settings defines. Red if
        /// the bench asmdef loses its define constraint or CAMERA_RECORDING_BENCH lands in Player Settings.</summary>
        [Test]
        public void PlainPlayerBuild_DoesNotCompileTheBench()
        {
            var player = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies);
            Assert.That(player.Any(a => a.name == "CameraGame"), Is.True, "CameraGame is not in the player compilation - the guard would pass vacuously.");
            Assert.That(player.Any(a => a.name == Bench), Is.False,
                $"A plain player build would compile {Bench}. Check Player Settings > Scripting Define Symbols for CAMERA_RECORDING_BENCH.");
        }

        /// <summary>Positive control for the metadata check: the bench really uses CameraGame types (ClipRecorder,
        /// PhotoModeController), so its compiled metadata references CameraGame.</summary>
        [Test]
        public void Control_TheCheckDetectsARealReference()
        {
            var bench = Loaded(Bench);
            Assert.That(bench, Is.Not.Null, $"{Bench} is not loaded in the editor.");
            Assert.That(References(bench, "CameraGame"), Is.True);
        }
    }
}
