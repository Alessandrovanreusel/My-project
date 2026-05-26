using UnityEngine;
using UnityEditor;

public static class AddMeshColliders
{
    [MenuItem("Tools/Add MeshColliders to World")]
    static void AddColliders()
    {
        // Try to find the map object — supports both the old "World" and new "game map" names
        GameObject world = GameObject.Find("game map");
        if (world == null) world = GameObject.Find("World");
        if (world == null)
        {
            Debug.LogError("No map object found in scene! Looking for 'game map' or 'World'.");
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