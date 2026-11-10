using System;
using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace CameraGame.Tests
{
    /// <summary>
    /// Story 1.14 fence guard: the throwaway recording spike (Assets/Spikes/RecordingSpike) must never become
    /// a dependency of game code. Only its own Editor assembly may reference it. A cheap structural check; the
    /// strong proof is the plain build's Managed/ listing (no spike DLL), kept in the story's evidence.
    /// </summary>
    public class RecordingSpikeFenceTests
    {
        private const string Spike = "CameraGame.Spikes.RecordingSpike";

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

        /// <summary>The asmdef level too: a reference declared in an .asmdef but never used leaves no trace in
        /// the compiled metadata (the compiler only emits references it needs), yet it still drags the spike
        /// into builds of that assembly. So the asmdef text itself must not name the spike.</summary>
        [TestCase("Assets/Scripts/CameraGame.asmdef")]
        [TestCase("Assets/Scripts/Editor/CameraGame.Editor.asmdef")]
        public void GameAsmdef_DoesNotNameTheSpike(string path)
        {
            Assert.That(System.IO.File.Exists(path), Is.True, path + " moved - the guard would pass vacuously.");
            Assert.That(System.IO.File.ReadAllText(path).Contains(Spike), Is.False, path + " names " + Spike);
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
