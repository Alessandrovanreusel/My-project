using UnityEngine;
using UnityEditor;

public static class AddMeshColliders
{
    [MenuItem("Tools/Add MeshColliders to World")]
[MenuItem("Tools/Add MeshColliders to World")]
    static void AddColliders()
    {
        GameObject world = GameObject.Find("World");
        if (world == null)
        {
            Debug.LogError("World not found in scene!");
            return;
        }

        MeshFilter[] meshFilters = world.GetComponentsInChildren<MeshFilter>();
        int count = 0;

        foreach (MeshFilter mf in meshFilters)
        {
            if (mf.GetComponent<MeshCollider>() == null)
            {
                mf.gameObject.AddComponent<MeshCollider>();
                count++;
            }
        }

        Debug.Log($"Added MeshColliders to {count} objects.");
    }
}