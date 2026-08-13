using System;
using System.Collections.Generic;
using L2Viewer.SceneDomain.Models;
using UnityEngine;

internal sealed class StaticMeshPlacementAsset
{
    public StaticMeshPlacementAsset(
        Mesh renderMesh,
        Material[] materials,
        Texture2D[][] flipbooks,
        GameObject prefab)
    {
        RenderMesh = renderMesh;
        Materials = materials;
        Flipbooks = flipbooks;
        Prefab = prefab;
    }

    public Mesh RenderMesh { get; }
    public Material[] Materials { get; }
    public Texture2D[][] Flipbooks { get; }
    public GameObject Prefab { get; }
    public bool HasPrefab => Prefab != null;
}

internal static class StaticMeshInstancePlacer
{
    private const string DefaultFlame01Token = "Default_Flame01";

    public static void PlaceInstances(
        IReadOnlyList<SceneStaticMeshInstance> instances,
        GameObject parent,
        IReadOnlyDictionary<string, StaticMeshPlacementAsset> assetCache,
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

            if (string.IsNullOrEmpty(instance.MeshReference) || !assetCache.TryGetValue(instance.MeshReference, out var asset))
            {
                continue;
            }

            if (asset == null)
            {
                continue;
            }

            var visual = StaticMeshSceneObjectFactory.InstantiateOrCreate(asset, parent.transform);
            if (visual == null)
            {
                continue;
            }

            visual.name = instance.StableName;
            visual.isStatic = true;
            visual.transform.localPosition = instance.WorldLocation.TransformFromUnrealToUnityWithScale();
            visual.transform.localRotation = instance.UnrealRotationRaw.ToUnityRotationFromUnrealRotator();
            visual.transform.localScale = new Vector3(instance.Scale.X, instance.Scale.Z, instance.Scale.Y);
            ApplyPrePivotOffset(visual.transform, instance, asset.HasPrefab);

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

        var visual = InstantiateSceneObject(prefab, parent.transform);
        if (visual == null)
        {
            throw new InvalidOperationException("Unity failed to instantiate Default_Flame01 override prefab.");
        }

        visual.name = instance.StableName;
        visual.isStatic = true;
        visual.transform.localPosition = instance.WorldLocation.TransformFromUnrealToUnityWithScale();
        visual.transform.localRotation = instance.UnrealRotationRaw.ToUnityRotationFromUnrealRotator();
        visual.transform.localScale = new Vector3(instance.Scale.X, instance.Scale.Z, instance.Scale.Y);

        log?.Invoke($"[StaticMesh/Override] Replaced '{instance.MeshReference}' with Default_Flame01 override prefab.");
        return true;
    }


    private static void ApplyPrePivotOffset(Transform visualRoot, SceneStaticMeshInstance instance, bool hasGeometryChild)
    {
        if (visualRoot == null)
        {
            return;
        }

        var offset = instance.PrePivot == System.Numerics.Vector3.Zero
            ? Vector3.zero
            : ComputePrePivotOffset(instance.PrePivot, instance.Scale).TransformFromUnrealToUnityWithScale();

        if (!hasGeometryChild)
        {
            visualRoot.localPosition += visualRoot.localRotation * Vector3.Scale(visualRoot.localScale, offset);
            return;
        }

        var geometry = visualRoot.Find("Geometry");
        if (geometry != null)
        {
            geometry.localPosition = offset;
        }
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

    private static GameObject InstantiateSceneObject(GameObject prefab, Transform parent)
    {
        return prefab == null
            ? null
            : UnityEngine.Object.Instantiate(prefab, parent, false);
    }

}

internal static class StaticMeshSceneObjectFactory
{
    public static GameObject InstantiateOrCreate(StaticMeshPlacementAsset asset, Transform parent)
    {
        if (asset == null)
        {
            return null;
        }

        if (asset.Prefab != null)
        {
            return UnityEngine.Object.Instantiate(asset.Prefab, parent, false);
        }

        if (asset.RenderMesh == null || asset.Materials == null || asset.Materials.Length == 0)
        {
            return null;
        }

        var visual = new GameObject(asset.RenderMesh.name);
        visual.transform.SetParent(parent, false);
        visual.isStatic = true;

        var filter = visual.AddComponent<MeshFilter>();
        filter.sharedMesh = asset.RenderMesh;

        var renderer = visual.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = asset.Materials;
        StaticMeshFlipbookUtility.ApplyFlipbooks(visual, renderer, asset.Flipbooks);

        return visual;
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
