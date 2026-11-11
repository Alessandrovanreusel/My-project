using System.IO;
using NUnit.Framework;
using UnityEditor;

namespace CameraGame.Tests
{
    /// <summary>
    /// Story 2.2 structural invariants for the town map. Not expected results — guards that fail before something
    /// silent ships: a re-enabled camera import (AR8: a Blender camera once overrode Main Camera), and a scene or NavMesh
    /// file growing past GitHub's 50 MB warning (100 MB is rejected, the loop pushes master, and an LFS rule would be a
    /// money question).
    /// </summary>
    public class TownMapGuardTests
    {
        private const string MapFbx = "Assets/Models/game map .fbx";
        private const long FiftyMegabytes = 50L * 1024 * 1024;

        [Test]
        public void TownMap_DoesNotImportCameras()
        {
            var importer = AssetImporter.GetAtPath(MapFbx) as ModelImporter;
            Assert.IsNotNull(importer, "no ModelImporter for " + MapFbx);
            Assert.IsFalse(importer.importCameras, "camera import must stay off on the town map (AR8, Story 2.2)");
        }

        [Test]
        public void TownMap_DoesNotImportLights()
        {
            var importer = AssetImporter.GetAtPath(MapFbx) as ModelImporter;
            Assert.IsNotNull(importer, "no ModelImporter for " + MapFbx);
            Assert.IsFalse(importer.importLights, "light import is off on the town map (Story 2.2 decision): a re-export must not bring in the .blend's light");
        }

        [TestCase("Assets/Scenes/SampleScene.unity")]
        [TestCase("Assets/Scenes/SampleScene/NavMesh-game map.asset")]
        public void CommittedFile_IsUnderFiftyMegabytes(string path)
        {
            Assert.IsTrue(File.Exists(path), path + " is missing");
            long size = new FileInfo(path).Length;
            Assert.Less(size, FiftyMegabytes, $"{path} is {size / 1048576.0:0.0} MB — over GitHub's 50 MB warning; split it (never add an LFS rule)");
        }
    }
}
