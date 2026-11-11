using System.Diagnostics;
using System.IO;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using CameraGame.World;
using Debug = UnityEngine.Debug;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// Story 2.2 (AC5, 1g): <c>Tools/Town/Bake Town NavMesh</c> — re-runnable whole-town bake on the NavMeshSurface on
    /// <c>game map</c>, persisted INTO the existing <c>NavMesh-game map.asset</c> (EditorUtility.CopySerialized), so its GUID
    /// and the scene's reference are kept.
    ///   • Collect: Volume = the playable area's bounding box from <see cref="TownBoundsConfig"/>, y from bakeYMin (just under
    ///     the lowest walkable ground) to bakeYMax (over the highest) — far above the buried block, so no underground island.
    ///   • Layers: Default (the map) + WorldBounds (the barriers carve the edge). Physics colliders.
    ///   • Excluded: the player (a NavMeshModifier with ignoreFromBuild on its root — once the spawn stands on the ground the
    ///     capsule sits inside the volume and would carve a hole at spawn). No-go areas carry Not Walkable volumes.
    ///   • Voxel: 0.1 m, set EXPLICITLY (lesson 60: with the override off the agent settings' 0.1667 is used), read back.
    /// Agent type 0 is read before and after and never changed. Reports the time, the tiles and the file size; the file
    /// must stay under 50 MB (an EditMode test guards it). Marks the scene dirty and never saves it.
    /// </summary>
    public static class TownNavMeshBaker
    {
        public const string AssetPath = "Assets/Scenes/SampleScene/NavMesh-game map.asset";
        public const float Voxel = 0.1f;

        [MenuItem("Tools/Town/Bake Town NavMesh")]
        public static string Bake()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<TownBoundsConfig>(AddMeshColliders.TownBoundsConfigPath);
            GameObject map = GameObject.Find("game map");
            NavMeshSurface surface = map != null ? map.GetComponent<NavMeshSurface>() : null;
            if (cfg == null || surface == null) { Debug.LogWarning("[TownNavMesh] Needs TownBoundsConfig and the NavMeshSurface on 'game map'."); return "missing"; }
            var sb = new StringBuilder($"Story 2.2 whole-town NavMesh bake {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            NavMeshBuildSettings a0 = NavMesh.GetSettingsByID(0);
            sb.AppendLine($"agent type 0 before: radius {a0.agentRadius} height {a0.agentHeight} climb {a0.agentClimb} slope {a0.agentSlope}");
            sb.AppendLine($"surface before: collect {surface.collectObjects}, centre {surface.center}, size {surface.size}, layers {surface.layerMask.value}, " +
                          $"geometry {surface.useGeometry}, voxel override {surface.overrideVoxelSize} {surface.voxelSize}, tile {surface.overrideTileSize} {surface.tileSize}, minRegion {surface.minRegionArea}");
            long before = File.Exists(AssetPath) ? new FileInfo(AssetPath).Length : 0;

            Vector4 b2 = cfg.PlayableBounds2D();
            float y0 = cfg.SafeBakeYMin, y1 = cfg.SafeBakeYMax;
            surface.collectObjects = CollectObjects.Volume;
            surface.center = new Vector3((b2.x + b2.z) * 0.5f, (y0 + y1) * 0.5f, (b2.y + b2.w) * 0.5f) - map.transform.position;
            surface.size = new Vector3(b2.z - b2.x, y1 - y0, b2.w - b2.y);
            int bounds = LayerMask.NameToLayer(TownBoundsBuilder.Layer);
            surface.layerMask = (1 << 0) | (bounds >= 0 ? 1 << bounds : 0);
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.overrideVoxelSize = true;
            surface.voxelSize = Voxel;

            GameObject player = GameObject.Find("main characters");
            if (player != null)
            {
                NavMeshModifier mod = player.GetComponent<NavMeshModifier>();
                if (mod == null) mod = player.AddComponent<NavMeshModifier>();
                mod.ignoreFromBuild = true;
                sb.AppendLine("player root excluded (NavMeshModifier.ignoreFromBuild)");
            }

            var sw = Stopwatch.StartNew();
            surface.BuildNavMesh();
            double seconds = sw.Elapsed.TotalSeconds;
            NavMeshData built = surface.navMeshData;
            var asset = AssetDatabase.LoadAssetAtPath<NavMeshData>(AssetPath);
            if (asset != null && built != null && built != asset)
            {
                EditorUtility.CopySerialized(built, asset);
                surface.RemoveData();
                surface.navMeshData = asset;
                surface.AddData();
                Object.DestroyImmediate(built, true);
            }
            else if (asset == null && built != null)
            {
                AssetDatabase.CreateAsset(built, AssetPath);
                surface.navMeshData = built;
            }
            EditorUtility.SetDirty(surface.navMeshData);
            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(map.scene);

            long after = new FileInfo(AssetPath).Length;
            var so = new SerializedObject(surface.navMeshData);
            SerializedProperty tiles = so.FindProperty("m_NavMeshTiles");
            SerializedProperty cell = so.FindProperty("m_NavMeshBuildSettings.cellSize");
            sb.AppendLine($"surface after: collect {surface.collectObjects}, centre {surface.center}, size {surface.size} (x {b2.x}..{b2.z}, y {y0}..{y1}, z {b2.y}..{b2.w}), " +
                          $"layers {surface.layerMask.value}, voxel {surface.voxelSize} (override {surface.overrideVoxelSize})");
            sb.AppendLine($"bake {seconds:0.0} s; tiles {(tiles != null ? tiles.arraySize : -1)}; cellSize read back {(cell != null ? cell.floatValue.ToString("0.###") : "?")}; " +
                          $"file {before / 1048576.0:0.00} MB → {after / 1048576.0:0.00} MB ({after} bytes); GUID {AssetDatabase.AssetPathToGUID(AssetPath)}");
            NavMeshBuildSettings a1 = NavMesh.GetSettingsByID(0);
            sb.AppendLine($"agent type 0 after: radius {a1.agentRadius} height {a1.agentHeight} climb {a1.agentClimb} slope {a1.agentSlope}");
            if (after >= 50L * 1024 * 1024) sb.AppendLine("⚠️ OVER 50 MB — do not commit; split the town into several surfaces.");
            string dir = TownCensus.Root + "/navmesh";
            Directory.CreateDirectory(dir);
            File.AppendAllText(Path.Combine(dir, "bake.txt"), sb.ToString() + "\n");
            Debug.Log("[TownNavMesh] " + sb.ToString().Replace("\n", " | "));
            return sb.ToString();
        }
    }
}
