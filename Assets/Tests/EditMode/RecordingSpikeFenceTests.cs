using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using AssembliesType = UnityEditor.Compilation.AssembliesType;
using CompilationPipeline = UnityEditor.Compilation.CompilationPipeline;

namespace CameraGame.Tests
{
    /// <summary>
    /// Story 1.14 fence guard: the throwaway recording spike (Assets/Spikes/RecordingSpike) must never become
    /// a dependency of game code, and a plain player build must not compile it. Only its own Editor assembly
    /// may reference it. A cheap structural check; the build-level proof is the plain build's Managed/ listing
    /// (no spike DLL), kept in the story's evidence.
    /// </summary>
    public class RecordingSpikeFenceTests
    {
        private const string Spike = "CameraGame.Spikes.RecordingSpike";
        private const string SpikeEditor = Spike + ".Editor";

        private static Assembly Loaded(string name) =>
            AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == name);

        private static bool References(Assembly a, string target) =>
            a.GetReferencedAssemblies().Any(r => r.Name == target);

        [TestCase("CameraGame")]
        [TestCase("CameraGame.Editor")]
        public void GameAssembly_DoesNotReferenceTheSpike(string game)
        {
            var asm = Loaded(game);
            Assert.That(asm, Is.Not.Null, $"{game} is not loaded - the guard would pass vacuously.");
            Assert.That(References(asm, Spike), Is.False, $"{game} references {Spike}: the spike is no longer fenced off.");
        }

        /// <summary>The asmdef level too. A reference declared in an .asmdef but never used leaves no trace in the
        /// compiled metadata (the compiler only emits references it needs), so ask Unity's compilation pipeline,
        /// which resolves the declared references whatever form they take. An earlier version searched the
        /// asmdef TEXT for the spike's name and missed a <c>"GUID:…"</c> reference, the form the asmdef Inspector
        /// writes by default: the 2026-11-10 review added one to CameraGame.Editor, Unity resolved it, and all
        /// five guards stayed green. This checks EVERY assembly, so a future one (Recording, tools, tests) is
        /// covered as well.</summary>
        [Test]
        public void NoAssemblyExceptTheSpikesOwnEditorAssembly_DeclaresAReferenceToTheSpike()
        {
            var all = CompilationPipeline.GetAssemblies(AssembliesType.Editor);
            Assert.That(all.Any(a => a.name == Spike), Is.True, $"{Spike} is not in the compilation - the guard would pass vacuously.");
            var spikeEditor = all.FirstOrDefault(a => a.name == SpikeEditor);
            Assert.That(spikeEditor, Is.Not.Null, $"{SpikeEditor} is gone - the positive control below cannot run.");
            Assert.That(spikeEditor.assemblyReferences.Any(r => r.name == Spike), Is.True,
                "Positive control: the spike's own Editor assembly declares the reference, so the check must see it.");

            var offenders = all.Where(a => a.name != Spike && a.name != SpikeEditor && a.assemblyReferences.Any(r => r.name == Spike))
                               .Select(a => a.name).ToArray();
            Assert.That(offenders, Is.Empty, $"These assemblies reference {Spike}, so the spike is no longer fenced off: {string.Join(", ", offenders)}");
        }

        /// <summary>The fence itself: what a plain player build compiles, under the CURRENT Player Settings defines.
        /// It goes red if the spike asmdef loses its define constraint, or if CAMERA_RECORDING_SPIKE ends up in
        /// Player Settings > Scripting Define Symbols (the spike build passes it as an extra define for one build
        /// only). Positive control, run by hand in the 2026-11-10 review: with the define in the Standalone
        /// symbols, the spike appears in this list; with it cleared, it does not. The build-level proof is the
        /// plain build's Managed/ listing in the story's evidence.</summary>
        [Test]
        public void PlainPlayerBuild_DoesNotCompileTheSpike()
        {
            var player = CompilationPipeline.GetAssemblies(AssembliesType.PlayerWithoutTestAssemblies);
            Assert.That(player.Any(a => a.name == "CameraGame"), Is.True, "CameraGame is not in the player compilation - the guard would pass vacuously.");
            Assert.That(player.Any(a => a.name == Spike), Is.False,
                $"A plain player build would compile {Spike}. Check Player Settings > Scripting Define Symbols for CAMERA_RECORDING_SPIKE, " +
                "and the spike asmdef's defineConstraints.");
        }

        /// <summary>Positive control: the same metadata check DOES see a real reference (CameraGame.Editor uses
        /// CameraGame types), so a green result above means "no reference", not "the check is blind".
        /// (The spike's own Editor assembly is NOT a valid control: it only uses a const, which C# inlines, so
        /// no reference is emitted. The first version of this test used it and failed, correctly.)</summary>
        [Test]
        public void Control_TheCheckDetectsARealReference()
        {
            var editor = Loaded("CameraGame.Editor");
            Assert.That(editor, Is.Not.Null);
            Assert.That(References(editor, "CameraGame"), Is.True);
        }
    }
}
