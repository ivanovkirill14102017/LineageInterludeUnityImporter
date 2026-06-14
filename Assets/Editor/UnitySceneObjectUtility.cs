using UnityEditor;
using UnityEngine;

internal static class UnitySceneObjectUtility
{
    public static void RemoveExistingObject(string objectName)
    {
        var existing = GameObject.Find(objectName);
        if (existing == null)
        {
            return;
        }

        Selection.activeObject = null;
        Object.DestroyImmediate(existing);
    }

    public static GameObject CreateMapRoot(string objectName)
    {
        var existingRoots = Object.FindObjectsByType<L2MapRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        for (var i = 0; i < existingRoots.Length; i++)
        {
            var existingRoot = existingRoots[i];
            if (existingRoot != null && existingRoot.gameObject.name == objectName)
            {
                return existingRoot.gameObject;
            }
        }

        var existing = GameObject.Find(objectName);
        if (existing != null)
        {
            var existingMarker = existing.GetComponent<L2MapRoot>();
            if (existingMarker != null)
            {
                return existing;
            }

            var root = new GameObject(objectName);
            root.transform.position = Vector3.zero;
            var marker = root.AddComponent<L2MapRoot>();
            marker.MapKey = objectName;

            if (existing.transform.parent == null)
            {
                if (existing.GetComponent<Terrain>() != null && existing.name == objectName)
                {
                    existing.name = objectName + "_Terrain";
                }

                existing.transform.SetParent(root.transform, true);
            }

            return root;
        }

        var createdRoot = new GameObject(objectName);
        createdRoot.transform.position = Vector3.zero;
        var createdMarker = createdRoot.AddComponent<L2MapRoot>();
        createdMarker.MapKey = objectName;
        return createdRoot;
    }
}
