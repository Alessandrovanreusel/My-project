using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

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

        /// <summary>
        /// Story 2.2 found every scatter collider in SampleScene pointing at a mesh the FBX no longer contained: 15,020
        /// MeshColliders loaded with no mesh and collided with nothing, with a clean console. This reads the scene FILE
        /// (no scene is opened) and checks that every MeshCollider mesh reference into the town map is a mesh the map
        /// actually holds, so a re-export or an importer change that renames them fails here instead of in play.
        /// </summary>
        [Test]
        public void EveryMapColliderInSampleScene_ReferencesAMeshTheMapContains()
        {
            string guid = AssetDatabase.AssetPathToGUID(MapFbx);
            var meshIds = new HashSet<long>();
            foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(MapFbx))
                if (o is Mesh && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string g, out long id)) meshIds.Add(id);
            Assert.Greater(meshIds.Count, 0, "the town map holds no meshes");
            var re = new Regex(@"m_Mesh: \{fileID: (-?\d+), guid: " + guid + @", type: 3\}");
            int refs = 0, broken = 0;
            bool inCollider = false;
            foreach (string line in File.ReadLines("Assets/Scenes/SampleScene.unity"))
            {
                if (line.StartsWith("--- !u!")) inCollider = line.StartsWith("--- !u!64 ");
                if (!inCollider) continue;
                Match m = re.Match(line);
                if (!m.Success) continue;
                refs++;
                if (!meshIds.Contains(long.Parse(m.Groups[1].Value))) broken++;
            }
            Assert.Greater(refs, 0, "no MeshCollider in SampleScene references the town map — the guard would prove nothing");
            Assert.AreEqual(0, broken, $"{broken} of {refs} map MeshColliders in SampleScene reference a mesh the FBX does not contain (they load with no mesh and collide with nothing): re-run Tools/Town/Apply Collider Rule to the Map");
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
