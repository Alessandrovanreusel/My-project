using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using CameraGame.EditorTools;
using CameraGame.Player;
using CameraGame.World;

/// <summary>
/// The way the town map's colliders are made: scene-level components on the <c>game map</c> prefab instance's children,
/// decided by ONE pure rule (<see cref="ColliderRule"/>, Story 2.2 1b). Both menu items run the same pass, so a re-run
/// never re-adds what the rule removed:
///   Keep  → a MeshCollider whose mesh IS the MeshFilter's (added if missing, REPAIRED if it points anywhere else — every
///           scatter collider used to reference a mesh the FBX no longer contains and collided with nothing);
///   Trunk → a pine: the MeshCollider is replaced by a 3 m CapsuleCollider round its trunk, measured from its own mesh;
///   Grass / LowScatter / Water / Buried → no collider.
/// It marks the scene dirty and never saves it: save in a separate step (an unsaved-changes dialog turns one menu call
/// into six). Writes a per-class report to the Story 2.2 evidence folder.
/// </summary>
public static class AddMeshColliders
{
    public const string TownBoundsConfigPath = "Assets/Data/World/TownBoundsConfig.asset";
    private const float TrunkHeight = 3f, TrunkProbe = 1f, TrunkMinRadius = 0.25f, TrunkMaxRadius = 1.2f;

    [MenuItem("Tools/Add MeshColliders to World")]
    static void AddColliders() => Apply();

    [MenuItem("Tools/Town/Apply Collider Rule to the Map")]
    public static string Apply()
    {
        GameObject world = GameObject.Find("game map");
        if (world == null) world = GameObject.Find("World");
        if (world == null) { Debug.LogError("No map object found in scene! Looking for 'game map' or 'World'."); return "no map"; }

        var cfg = AssetDatabase.LoadAssetAtPath<TownBoundsConfig>(TownBoundsConfigPath);
        float buriedTopY = cfg != null ? cfg.SafeBuriedTopY : TownBoundsConfig.DefaultBuriedTopY;
        var pc = AssetDatabase.LoadAssetAtPath<PlayerConfig>(PhotoShootRig.PlayerConfigPath);
        float step = pc != null ? pc.SafeStepOffset : PlayerConfig.DefaultStepOffset;

        Physics.SyncTransforms();
        Dictionary<Collider, int> kinds = TownCensus.Kinds(world);
        MeshRenderer[] renderers = world.GetComponentsInChildren<MeshRenderer>(true);
        var verdicts = new ColliderVerdict[renderers.Length];
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer r = renderers[i];
            Bounds b = r.bounds;
            float hag = float.NaN;
            if (ColliderRule.NeedsHeightAboveGround(r.name))
            {
                TownCensus.Column(kinds, b.center.x, b.center.z, out float sy, out _, out _, out _, out _);
                if (!float.IsNaN(sy)) hag = b.max.y - sy;
            }
            verdicts[i] = ColliderRule.Decide(r.name, r.sharedMaterial != null ? r.sharedMaterial.name : "", b.max.y, hag, step, buriedTopY);
        }

        var tally = new SortedDictionary<string, int[]>();   // class → kept, added, repaired, removed, trunk
        int kept = 0, added = 0, repaired = 0, removed = 0, trunks = 0;
        for (int i = 0; i < renderers.Length; i++)
        {
            MeshRenderer r = renderers[i];
            MeshFilter mf = r.GetComponent<MeshFilter>();
            if (mf == null || mf.sharedMesh == null) continue;
            string cls = ColliderRule.ClassOf(r.name) + " → " + verdicts[i];
            if (!tally.TryGetValue(cls, out int[] t)) { t = new int[5]; tally[cls] = t; }
            MeshCollider mc = r.GetComponent<MeshCollider>();
            CapsuleCollider cap = r.GetComponent<CapsuleCollider>();
            switch (verdicts[i])
            {
                case ColliderVerdict.Keep:
                    if (cap != null) Object.DestroyImmediate(cap);
                    if (mc == null) { mc = r.gameObject.AddComponent<MeshCollider>(); mc.sharedMesh = mf.sharedMesh; added++; t[1]++; }
                    else if (mc.sharedMesh != mf.sharedMesh) { mc.sharedMesh = mf.sharedMesh; repaired++; t[2]++; }
                    else { kept++; t[0]++; }
                    break;
                case ColliderVerdict.Trunk:
                    if (mc != null) Object.DestroyImmediate(mc);
                    if (cap == null) cap = r.gameObject.AddComponent<CapsuleCollider>();
                    SizeTrunk(cap, mf);
                    trunks++; t[4]++;
                    break;
                default:
                    if (mc != null) { Object.DestroyImmediate(mc); removed++; t[3]++; }
                    if (cap != null) Object.DestroyImmediate(cap);
                    break;
            }
        }
        EditorSceneManager.MarkSceneDirty(world.scene);
        var sb = new StringBuilder($"Collider rule applied {System.DateTime.Now:yyyy-MM-dd HH:mm:ss} (step {step} m, buried below y {buriedTopY} m)\n");
        sb.AppendLine($"renderers {renderers.Length}: kept {kept}, added {added}, repaired {repaired}, removed {removed}, pine trunks {trunks}");
        sb.AppendLine("class → verdict: kept / added / repaired / removed / trunk");
        foreach (var kv in tally) sb.AppendLine($"  {kv.Key}: {kv.Value[0]} / {kv.Value[1]} / {kv.Value[2]} / {kv.Value[3]} / {kv.Value[4]}");
        string dir = TownCensus.Root + "/colliders";
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "report.txt"), sb.ToString());
        Debug.Log("[Colliders] " + sb.ToString().Split('\n')[1] + " — the scene is NOT saved; " + dir + "/report.txt");
        return sb.ToString();
    }

    /// <summary>A 3 m capsule round the pine's trunk, measured from this instance's own vertices: the axis through the
    /// centroid of the vertices in its lowest metre, the radius their 90th-percentile distance from it.</summary>
    private static void SizeTrunk(CapsuleCollider cap, MeshFilter mf)
    {
        Transform tr = mf.transform;
        Vector3[] v = mf.sharedMesh.vertices;
        float baseY = float.MaxValue;
        var world = new Vector3[v.Length];
        for (int i = 0; i < v.Length; i++) { world[i] = tr.TransformPoint(v[i]); baseY = Mathf.Min(baseY, world[i].y); }
        Vector3 c = Vector3.zero; int n = 0;
        foreach (Vector3 w in world) if (w.y - baseY < TrunkProbe) { c += w; n++; }
        if (n == 0) return;
        c /= n;
        var radii = new List<float>();
        foreach (Vector3 w in world) if (w.y - baseY < TrunkProbe) radii.Add(new Vector2(w.x - c.x, w.z - c.z).magnitude);
        radii.Sort();
        float r = Mathf.Clamp(radii[(int)(radii.Count * 0.9f)], TrunkMinRadius, TrunkMaxRadius);
        Vector3 up = tr.InverseTransformDirection(Vector3.up);
        int dir = Mathf.Abs(up.x) > Mathf.Abs(up.y) ? (Mathf.Abs(up.x) > Mathf.Abs(up.z) ? 0 : 2) : (Mathf.Abs(up.y) > Mathf.Abs(up.z) ? 1 : 2);
        Vector3 s = tr.lossyScale;
        float sAxis = Mathf.Abs(s[dir]), sSide = Mathf.Max(Mathf.Abs(s[(dir + 1) % 3]), Mathf.Abs(s[(dir + 2) % 3]));
        cap.direction = dir;
        cap.center = tr.InverseTransformPoint(new Vector3(c.x, baseY + TrunkHeight * 0.5f, c.z));
        cap.radius = r / Mathf.Max(1e-6f, sSide);
        cap.height = TrunkHeight / Mathf.Max(1e-6f, sAxis);
    }
}
