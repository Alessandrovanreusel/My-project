using System.Collections.Generic;
using System.IO;
using System.Text;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using CameraGame.World;

namespace CameraGame.EditorTools
{
    /// <summary>
    /// Story 2.2 (AC3, 1c): builds the invisible edges of the town from <see cref="TownBoundsConfig"/> — the ONE place the
    /// playable area is written down. <c>Tools/Town/Build Town Bounds</c>:
    ///   • ensures a <c>WorldBounds</c> layer (through the TagManager's SerializedObject — on-disk edits do not apply while
    ///     the editor runs), read back;
    ///   • (re)creates the scene root <c>TownBounds</c> with one BoxCollider per edge of the playable area and of every
    ///     no-go area (the pond), on that layer, with no renderer: each reaches from 2 m below the lowest ground along its
    ///     edge to <see cref="TownBoundsConfig.barrierClearance"/> above the highest, so nobody can stand on top of one;
    ///   • adds a NavMeshModifierVolume (Not Walkable) over each no-go area, so the bake never puts walkable mesh in it.
    /// The barriers sit where the world explains them: the water's edge and the ring's hillsides, never across open plain.
    /// Marks the scene dirty and never saves it (save in a separate call).
    /// </summary>
    public static class TownBoundsBuilder
    {
        public const string Layer = "WorldBounds";
        public const string RootName = "TownBounds";

        [MenuItem("Tools/Town/Build Town Bounds")]
        public static string Build()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<TownBoundsConfig>(AddMeshColliders.TownBoundsConfigPath);
            if (cfg == null) { Debug.LogWarning("[TownBounds] No TownBoundsConfig at " + AddMeshColliders.TownBoundsConfigPath); return "no config"; }
            int layer = EnsureLayer(Layer);
            if (layer < 0) { Debug.LogError("[TownBounds] Could not add the WorldBounds layer (no free user layer)."); return "no layer"; }

            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            var root = new GameObject(RootName) { layer = layer };
            GameObject map = GameObject.Find("game map");
            var sb = new StringBuilder($"Town bounds built {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}; layer {Layer} = {layer}\n");
            Physics.SyncTransforms();
            int n = Edges(root.transform, "Edge", cfg.SafePlayableArea, cfg, layer, map, sb);
            if (cfg.noGoAreas != null)
                foreach (TownBoundsConfig.Area a in cfg.noGoAreas)
                {
                    if (!TownBoundsConfig.IsUsablePolygon(a.points)) continue;
                    var group = new GameObject("NoGo " + a.name) { layer = layer };
                    group.transform.SetParent(root.transform, false);
                    n += Edges(group.transform, "Edge", a.points, cfg, layer, map, sb);
                    // the bake must not put walkable mesh inside it
                    Vector2 mn = a.points[0], mx = a.points[0];
                    foreach (Vector2 p in a.points) { mn = Vector2.Min(mn, p); mx = Vector2.Max(mx, p); }
                    var vol = group.AddComponent<NavMeshModifierVolume>();
                    vol.center = new Vector3((mn.x + mx.x) * 0.5f, (cfg.SafeBakeYMin + cfg.SafeBakeYMax) * 0.5f, (mn.y + mx.y) * 0.5f);
                    vol.size = new Vector3(mx.x - mn.x, cfg.SafeBakeYMax - cfg.SafeBakeYMin, mx.y - mn.y);
                    vol.area = 1;   // Not Walkable
                    sb.AppendLine($"  no-go '{a.name}': NavMeshModifierVolume Not Walkable centre {vol.center} size {vol.size}");
                }
            EditorSceneManager.MarkSceneDirty(root.scene);
            sb.AppendLine($"{n} barriers under {RootName}. Scene NOT saved.");
            string dir = TownCensus.Root + "/edges";
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "bounds-build.txt"), sb.ToString());
            Debug.Log("[TownBounds] " + n + " barriers built — " + dir + "/bounds-build.txt");
            return sb.ToString();
        }

        private static int Edges(Transform parent, string prefix, Vector2[] poly, TownBoundsConfig cfg, int layer, GameObject map, StringBuilder sb)
        {
            int made = 0;
            for (int i = 0; i < poly.Length; i++)
            {
                Vector2 a = poly[i], b = poly[(i + 1) % poly.Length];
                float len = Vector2.Distance(a, b);
                if (len < 0.01f) continue;
                float lo = float.MaxValue, hi = float.MinValue;
                for (float s = 0f; s <= len; s += 2f)
                {
                    Vector2 p = Vector2.Lerp(a, b, s / len);
                    if (!Ground(p, map, out float gy)) continue;
                    lo = Mathf.Min(lo, gy); hi = Mathf.Max(hi, gy);
                }
                if (lo == float.MaxValue) { lo = 0f; hi = 0f; }
                float bottom = lo - 2f, top = hi + cfg.SafeBarrierClearance;
                var go = new GameObject($"{prefix} {made:00}") { layer = layer };
                go.transform.SetParent(parent, false);
                Vector2 m = (a + b) * 0.5f, d = (b - a) / len;
                go.transform.SetPositionAndRotation(new Vector3(m.x, (bottom + top) * 0.5f, m.y),
                                                    Quaternion.LookRotation(new Vector3(d.x, 0f, d.y), Vector3.up));
                var box = go.AddComponent<BoxCollider>();
                box.size = new Vector3(cfg.SafeBarrierThickness, top - bottom, len + cfg.SafeBarrierThickness);
                sb.AppendLine($"  {parent.name}/{go.name}: ({a.x}, {a.y}) → ({b.x}, {b.y}), {len:0.0} m, ground {lo:0.0}..{hi:0.0}, box y {bottom:0.0}..{top:0.0}");
                made++;
            }
            return made;
        }

        /// <summary>Top surface of the map at (x, z) — map colliders only, so earlier barriers never count as ground.</summary>
        private static bool Ground(Vector2 p, GameObject map, out float y)
        {
            y = float.MinValue;
            foreach (RaycastHit h in Physics.RaycastAll(new Vector3(p.x, 400f, p.y), Vector3.down, 900f, ~0, QueryTriggerInteraction.Ignore))
                if (map == null || h.collider.transform.IsChildOf(map.transform)) y = Mathf.Max(y, h.point.y);
            return y > float.MinValue;
        }

        /// <summary>Returns the layer index, adding the layer in the first free user slot if needed (through the TagManager
        /// asset's SerializedObject, the editor API — editing ProjectSettings on disk does not apply while Unity runs).</summary>
        public static int EnsureLayer(string name)
        {
            int existing = LayerMask.NameToLayer(name);
            if (existing >= 0) return existing;
            var tm = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            SerializedProperty layers = tm.FindProperty("layers");
            for (int i = 8; i < layers.arraySize; i++)
            {
                SerializedProperty l = layers.GetArrayElementAtIndex(i);
                if (!string.IsNullOrEmpty(l.stringValue)) continue;
                l.stringValue = name;
                tm.ApplyModifiedPropertiesWithoutUndo();
                AssetDatabase.SaveAssets();
                return LayerMask.NameToLayer(name);
            }
            return -1;
        }
    }
}
