using System;
using System.Collections.Generic;
using L2Viewer.SceneDomain.Models;
using UnityEditor;
using UnityEngine;

internal static class StaticMeshInstancePlacer
{
    private const string DefaultFlame01Token = "Default_Flame01";

    public static void PlaceInstances(
        IReadOnlyList<SceneStaticMeshInstance> instances,
        GameObject parent,
        IReadOnlyDictionary<string, GameObject> prefabCache,
        Action<string> log)
    {
        log($"Placing {instances.Count} instances on the scene...");
        var spawnedCount = 0;

        foreach (var instance in instances)
        {
            if (TryPlaceOverrideInstance(instance, parent, log))
            {
                spawnedCount++;
                continue;
            }

            if (string.IsNullOrEmpty(instance.MeshReference) || !prefabCache.TryGetValue(instance.MeshReference, out var prefab))
            {
                continue;
            }

            if (prefab == null)
            {
                continue;
            }

            var visual = PrefabUtility.InstantiatePrefab(prefab, parent.transform) as GameObject;
            if (visual == null)
            {
                continue;
            }

            visual.name = instance.StableName;
            visual.isStatic = true;
            visual.transform.localPosition = instance.WorldLocation.TransformFromUnrealToUnityWithScale();
            visual.transform.localRotation = instance.RotationEulerDegrees.ToEulerAngles();
            visual.transform.localScale = new Vector3(instance.Scale.X, instance.Scale.Z, instance.Scale.Y);
            ApplyPrePivotOffset(visual.transform, instance);

            spawnedCount++;
        }

        log($"Finished placing {spawnedCount} static meshes.");
    }

    private static bool TryPlaceOverrideInstance(
        SceneStaticMeshInstance instance,
        GameObject parent,
        Action<string> log)
    {
        if (instance == null || string.IsNullOrWhiteSpace(instance.MeshReference))
        {
            return false;
        }

        if (instance.MeshReference.IndexOf(DefaultFlame01Token, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        var prefab = StaticMeshOverridePrefabUtility.GetDefaultFlame01OverridePrefab();
        if (prefab == null)
        {
            throw new InvalidOperationException("Could not load Default_Flame01 override prefab.");
        }

        var visual = PrefabUtility.InstantiatePrefab(prefab, parent.transform) as GameObject;
        if (visual == null)
        {
            throw new InvalidOperationException("Unity failed to instantiate Default_Flame01 override prefab.");
        }

        visual.name = instance.StableName;
        visual.isStatic = true;
        visual.transform.localPosition = instance.WorldLocation.TransformFromUnrealToUnityWithScale();
        visual.transform.localRotation = instance.RotationEulerDegrees.ToEulerAngles();
        visual.transform.localScale = new Vector3(instance.Scale.X, instance.Scale.Z, instance.Scale.Y);

        log?.Invoke($"[StaticMesh/Override] Replaced '{instance.MeshReference}' with Default_Flame01 override prefab.");
        return true;
    }


    private static void ApplyPrePivotOffset(Transform visualRoot, SceneStaticMeshInstance instance)
    {
        if (visualRoot == null)
        {
            return;
        }

        var geometry = visualRoot.Find("Geometry");
        if (geometry == null)
        {
            geometry = visualRoot;
        }

        geometry.localPosition = instance.PrePivot == System.Numerics.Vector3.Zero
            ? Vector3.zero
            : ComputePrePivotOffset(instance.PrePivot, instance.Scale).TransformFromUnrealToUnityWithScale();
    }

    private static System.Numerics.Vector3 ComputePrePivotOffset(
        System.Numerics.Vector3 prePivot,
        System.Numerics.Vector3 scale)
    {
        return new System.Numerics.Vector3(
            -prePivot.X / SafeScale(scale.X),
            -prePivot.Y / SafeScale(scale.Y),
            -prePivot.Z / SafeScale(scale.Z));
    }

    private static float SafeScale(float value)
    {
        return Math.Abs(value) < 0.0001f ? 1f : value;
    }

}

internal static class StaticMeshRendererMaterialUtility
{
    private static Material _fallbackMaterial;

    public static Material[] BuildRendererMaterials(
        string meshReference,
        Mesh mesh,
        StaticMeshMaterialCatalog materialCatalog)
    {
        if (mesh == null || mesh.subMeshCount <= 0)
        {
            return null;
        }

        var resolved = new Material[mesh.subMeshCount];
        materialCatalog.MaterialsByMeshReference.TryGetValue(meshReference, out var importedMaterials);
        var fallback = GetFallbackMaterial();

        for (var i = 0; i < resolved.Length; i++)
        {
            if (importedMaterials != null && i < importedMaterials.Length && importedMaterials[i] != null)
            {
                resolved[i] = importedMaterials[i];
            }
            else
            {
                resolved[i] = fallback;
            }
        }

        return resolved;
    }

    private static Material GetFallbackMaterial()
    {
        if (_fallbackMaterial != null)
        {
            return _fallbackMaterial;
        }

        var shader = StaticMeshImportUtility.FindDefaultShader();
        _fallbackMaterial = new Material(shader)
        {
            name = "StaticMeshFallbackMaterial",
            enableInstancing = true
        };
        return _fallbackMaterial;
    }
}
