using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using UnityEditor;
using UnityEngine;

internal static class CreatureMapImporter
{
    private const float UnrealUnitsToDegrees = 360f / 65536f;

    public static Task ImportAsync(MapImportRequest request, Ue2MapSource source, Action<string> log)
    {
        var dbRootPath = ConstInfo.L2DbRootPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(dbRootPath))
        {
            log("[Creatures] DB root path is empty. Creature import skipped.");
            return Task.CompletedTask;
        }

        log("[Creatures] START Spawn analysis");
        var builder = new SceneCreatureMapBuilder();
        var spawns = builder.Build(dbRootPath, source.ClientPath, request.MapKey, source.UnrFile);
        log($"[Creatures] DONE Spawn analysis. SceneDomain returned {spawns.Length} spawn records for quadrant '{request.MapKey}'.");

        if (spawns.Length == 0)
        {
            log("[Creatures] No creature spawns were found for the requested map.");
            return Task.CompletedTask;
        }

        var selectedPrefabKeys = spawns
            .Where(x => x != null && x.MeshResource != null && !string.IsNullOrWhiteSpace(x.MeshResource.PackagePath) && !string.IsNullOrWhiteSpace(x.MeshResource.ObjectName))
            .GroupBy(BuildPrefabKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (selectedPrefabKeys.Count == 0)
        {
            log($"[Creatures] No valid creature prefab keys were returned by SceneDomain for map '{request.MapKey}'.");
            return Task.CompletedTask;
        }

        var selectedSpawns = spawns
            .Where(x => x != null && selectedPrefabKeys.Contains(BuildPrefabKey(x)))
            .ToArray();

        MapImportAssetPreparation.EnsureMapOutputFolderExists(request.OutputDir);
        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        UnitySceneObjectUtility.RemoveExistingObject($"{request.ObjectName}_Creatures");

        var creatureRoot = new GameObject($"{request.ObjectName}_Creatures");
        creatureRoot.transform.SetParent(mapRoot.transform, false);

        var prefabCache = BuildPrefabCache(selectedSpawns, source.ClientPath, log);
        PlaceSpawns(selectedSpawns, creatureRoot, prefabCache, log);
        MapImportFinalizer.Complete(mapRoot, log);
        log("[Creatures] Import finished.");
        return Task.CompletedTask;
    }

    private static Dictionary<string, GameObject> BuildPrefabCache(
        IReadOnlyList<SceneCreatureSpawnData> spawns,
        string clientRoot,
        Action<string> log)
    {
        var resolver = new SceneSkeletalMeshResolver();
        var buildContext = L2SkeletalAnimatorPrefabBuilder.CreateBuildContext(clientRoot);
        var prefabCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        var prefabPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var uniquePrefabs = spawns
            .Where(x => x != null && x.MeshResource != null && !string.IsNullOrWhiteSpace(x.MeshResource.PackagePath) && !string.IsNullOrWhiteSpace(x.MeshResource.ObjectName))
            .GroupBy(BuildPrefabKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
        log($"[Creatures] Preparing {uniquePrefabs.Length} prefab assets for {spawns.Count} spawn instances.");
        var buildStopwatch = Stopwatch.StartNew();
        foreach (var spawn in uniquePrefabs)
        {
            try
            {
                var prefabKey = BuildPrefabKey(spawn);
                var expectedPrefabPath = BuildPrefabPath(spawn);
                var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(expectedPrefabPath);
                if (existingPrefab != null && ShouldReuseExistingPrefab(existingPrefab, spawn))
                {
                    prefabPaths[prefabKey] = expectedPrefabPath;
                    log($"[Creatures] Reusing existing prefab for '{spawn.DisplayName}': {expectedPrefabPath}");
                    continue;
                }

                if (existingPrefab != null)
                {
                    log($"[Creatures] Rebuilding prefab for '{spawn.DisplayName}' because cached materials are invalid or stale.");
                }

                var sharedAsset = resolver.ResolveAssetNamed(spawn.MeshResource.PackagePath, spawn.MeshResource.ObjectName);
                var build = L2SkeletalAnimatorPrefabBuilder.BuildFromResolvedAsset(
                    clientRoot,
                    sharedAsset,
                    L2AssetManager.ManagedCreaturePrefabsRoot,
                    L2AssetManager.SharedSkeletalCharactersRoot,
                    spawn.MeshResource.Reference,
                    prefabNameSuffix: null,
                    spawn.DisplayName,
                    log,
                    buildContext,
                    finalizeAssets: false);
                prefabPaths[prefabKey] = build.PrefabPath;
            }
            catch (Exception ex)
            {
                log($"[Creatures] Failed to build prefab for '{spawn.DisplayName}' ({spawn.MeshResource.Reference}): {ex.Message}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        foreach (var pair in prefabPaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pair.Value);
            if (prefab != null)
            {
                prefabCache[pair.Key] = prefab;
            }
        }
        buildStopwatch.Stop();
        log($"[Creatures] Prefab batch complete. RequestedTypes={uniquePrefabs.Length}, loaded={prefabCache.Count} ({buildStopwatch.Elapsed.TotalSeconds:F2}s)");

        return prefabCache;
    }

    private static bool ShouldReuseExistingPrefab(GameObject prefab, SceneCreatureSpawnData spawn)
    {
        if (prefab == null || spawn?.MeshResource == null)
        {
            return false;
        }

        var characterAssetPath = L2AssetManager.BuildClientPackageAssetPath(
            L2AssetManager.SharedSkeletalCharactersRoot,
            spawn.MeshResource.Reference,
            "NPC",
            "asset",
            "SkeletalCharacters");
        var characterAsset = AssetDatabase.LoadAssetAtPath<L2SkeletalCharacterAsset>(characterAssetPath);
        if (characterAsset == null)
        {
            return false;
        }

        if (!CharacterAssetExpectsTextures(characterAsset))
        {
            return true;
        }

        var renderer = prefab.GetComponentInChildren<SkinnedMeshRenderer>(true);
        if (renderer == null || renderer.sharedMaterials == null || renderer.sharedMaterials.Length == 0)
        {
            return false;
        }

        return renderer.sharedMaterials.All(MaterialHasRenderableTexture);
    }

    private static bool CharacterAssetExpectsTextures(L2SkeletalCharacterAsset asset)
    {
        if (asset == null)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(asset.PrimaryTextureReference))
        {
            return true;
        }

        if ((asset.MaterialBindings ?? Array.Empty<L2SkeletalMaterialBindingData>())
            .Any(x => x != null && !string.IsNullOrWhiteSpace(x.TextureReference)))
        {
            return true;
        }

        return (asset.UsedTextures ?? Array.Empty<L2SkeletalTextureRefData>())
            .Any(x => x != null && !string.IsNullOrWhiteSpace(x.Reference));
    }

    private static bool MaterialHasRenderableTexture(Material material)
    {
        if (material == null || material.shader == null)
        {
            return false;
        }

        if (string.Equals(material.shader.name, "Hidden/InternalErrorShader", StringComparison.Ordinal))
        {
            return false;
        }

        var texturePropertyName = L2MaterialUtility.GetPrimaryTexturePropertyName(material);
        if (!material.HasProperty(texturePropertyName))
        {
            return false;
        }

        return material.GetTexture(texturePropertyName) != null;
    }

    private static void PlaceSpawns(
        IReadOnlyList<SceneCreatureSpawnData> spawns,
        GameObject parent,
        IReadOnlyDictionary<string, GameObject> prefabCache,
        Action<string> log)
    {
        var placedCount = 0;
        var representativeOnlyCount = 0;

        foreach (var spawn in spawns)
        {
            if (spawn == null || string.IsNullOrWhiteSpace(spawn.DisplayName))
            {
                continue;
            }

            if (!prefabCache.TryGetValue(BuildPrefabKey(spawn), out var prefab) || prefab == null)
            {
                continue;
            }

            var visual = PrefabUtility.InstantiatePrefab(prefab, parent.transform) as GameObject;
            if (visual == null)
            {
                continue;
            }

            visual.name = spawn.StableName;
            visual.isStatic = false;
            visual.transform.localPosition = spawn.Position.TransformFromUnrealToUnityWithScale();
            visual.transform.localRotation = ConvertHeadingToRotation(spawn.Heading);
            visual.transform.localScale = Vector3.one;
            placedCount++;

            if (spawn.SpawnCount > 1 || spawn.RandomOffsetX != 0 || spawn.RandomOffsetY != 0)
            {
                representativeOnlyCount++;
            }
        }

        if (representativeOnlyCount > 0)
        {
            log($"[Creatures] {representativeOnlyCount} spawns were placed as one representative instance because SceneDomain currently exposes SpawnCount/RandomOffset, not exact per-creature positions.");
        }

        log($"[Creatures] Finished placing {placedCount} creature instances.");
    }

    private static Quaternion ConvertHeadingToRotation(int heading)
    {
        var yawDegrees = heading * UnrealUnitsToDegrees;
        return Quaternion.Euler(0f, -yawDegrees, 0f);
    }

    private static string BuildPrefabKey(SceneCreatureSpawnData spawn)
    {
        return spawn?.MeshResource?.Reference
            ?? spawn?.MeshResource?.ObjectName
            ?? spawn?.VisualKey
            ?? string.Empty;
    }

    private static string BuildPrefabPath(SceneCreatureSpawnData spawn)
    {
        return L2AssetManager.BuildClientPackageAssetPath(
            L2AssetManager.ManagedCreaturePrefabsRoot,
            spawn.MeshResource.Reference,
            "PF",
            "prefab",
            "CreaturePrefabs");
    }
}
