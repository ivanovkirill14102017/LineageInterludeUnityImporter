using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using UnityEngine;

internal static class StaticMeshImportUtility
{
    private static readonly string[] GrassTokens =
    {
        "_grass_",
        "_grass",
        "grass_",
        "grass"
    };

    public static IReadOnlyDictionary<string, SceneStaticMeshDefinition> FilterMeshDefinitions(
        IReadOnlyDictionary<string, SceneStaticMeshDefinition> uniqueMeshes)
    {
        return uniqueMeshes
            .Where(pair =>
            {
                var isTree = pair.Key != null && pair.Key.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0;
                return isTree || !isTree;
            })
            .ToDictionary(pair => pair.Key, pair => pair.Value);
    }

    public static bool IsTreeInstance(SceneStaticMeshInstance instance)
    {
        return instance != null && IsTreeMeshReference(instance.MeshReference);
    }

    public static bool IsGrassInstance(SceneStaticMeshInstance instance)
    {
        return instance != null && IsGrassMeshReference(instance.MeshReference);
    }

    public static bool IsTerrainVegetationInstance(SceneStaticMeshInstance instance)
    {
        return IsGrassInstance(instance) || IsTreeInstance(instance);
    }

    public static bool IsTreeMeshReference(string meshReference)
    {
        return !string.IsNullOrWhiteSpace(meshReference) &&
               meshReference.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static bool IsGrassMeshReference(string meshReference)
    {
        if (string.IsNullOrWhiteSpace(meshReference))
        {
            return false;
        }

        for (var i = 0; i < GrassTokens.Length; i++)
        {
            if (meshReference.IndexOf(GrassTokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    public static Shader FindDefaultShader()
    {
        return L2MaterialUtility.FindBestLitShader();
    }

    public static bool NeedsAlpha(MaterialKnownTraits traits)
    {
        if (traits == null)
        {
            return false;
        }

        return traits.BlendModeHint == MaterialBlendModeHint.Translucent ||
               traits.BlendModeHint == MaterialBlendModeHint.Additive ||
               traits.BlendModeHint == MaterialBlendModeHint.Modulated;
    }

    public static string BuildBindingKey(string meshReference, int materialId)
    {
        return $"{meshReference}::{materialId}";
    }

    public static List<int> CollectMaterialIds(SceneTriangleMeshData meshData)
    {
        var materialIds = new List<int>();
        var seen = new HashSet<int>();

        foreach (var triangle in meshData.Triangles)
        {
            if (seen.Add(triangle.MaterialId))
            {
                materialIds.Add(triangle.MaterialId);
            }
        }

        return materialIds;
    }
}
