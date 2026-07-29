using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Services.CharacterServices;
using UnityEditor;
using UnityEngine;

internal static class CreatureMapImporter
{
    private const float UnrealUnitsToDegrees = 360f / 65536f;

    public static Task ImportAsync(MapImportRequest request, Ue2MapSource source, Action<string> log, MapImportExecutionContext context = null)
    {
        var importStopwatch = Stopwatch.StartNew();
        var dbRootPath = ConstInfo.L2DbRootPath?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(dbRootPath))
        {
            log("[Creatures] DB root path is empty. Creature import skipped.");
            return Task.CompletedTask;
        }

        context?.Report("Creatures", "Spawn analysis (SceneDomain)", 0.12f);
        log("[Creatures] START Spawn analysis");
        var spawnAnalysisStopwatch = Stopwatch.StartNew();
        var builder = new SceneCreatureMapBuilder();
        var spawns = builder.Build(dbRootPath, source.ClientPath, request.MapKey, source.UnrFile);
        spawnAnalysisStopwatch.Stop();
        log($"[Creatures] DONE Spawn analysis. SceneDomain returned {spawns.Length} spawn records for quadrant '{request.MapKey}'.");
        log($"[Creatures/Timing] Spawn analysis (plugin) took {spawnAnalysisStopwatch.Elapsed.TotalSeconds:F2}s");

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

        context?.Report("Creatures", "Build prefab cache", 0.20f);
        var prefabCache = BuildPrefabCache(selectedSpawns, source.ClientPath, log, context);
        context?.Report("Creatures", "Place spawn instances", 0.92f);
        var placementStopwatch = Stopwatch.StartNew();
        PlaceSpawns(selectedSpawns, creatureRoot, prefabCache, log);
        placementStopwatch.Stop();
        log($"[Creatures/Timing] Spawn placement took {placementStopwatch.Elapsed.TotalSeconds:F2}s");
        context?.Report("Creatures", "Finalize scene objects", 0.98f);
        MapImportFinalizer.Complete(mapRoot, log);
        importStopwatch.Stop();
        log($"[Creatures/Timing] Total creature import took {importStopwatch.Elapsed.TotalSeconds:F2}s");
        log("[Creatures] Import finished.");
        return Task.CompletedTask;
    }

    private static Dictionary<string, GameObject> BuildPrefabCache(
        IReadOnlyList<SceneCreatureSpawnData> spawns,
        string clientRoot,
        Action<string> log,
        MapImportExecutionContext context)
    {
        var resolver = new SceneSkeletalMeshResolver();
        var buildContext = L2SkeletalAnimatorPrefabBuilder.CreateBuildContext(clientRoot);
        var prefabCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        var prefabPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var preparedBuilds = new List<(string PrefabKey, string DisplayName, L2SkeletalAnimatorPrefabBuilder.PreparedBuildData Build)>();
        var texturePlansByReference = new Dictionary<string, CreatureSkeletalMaterialImporter.TextureImportPlan>(StringComparer.OrdinalIgnoreCase);
        var uniquePrefabs = spawns
            .Where(x => x != null && x.MeshResource != null && !string.IsNullOrWhiteSpace(x.MeshResource.PackagePath) && !string.IsNullOrWhiteSpace(x.MeshResource.ObjectName))
            .GroupBy(BuildPrefabKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
        log($"[Creatures] Preparing {uniquePrefabs.Length} prefab assets for {spawns.Count} spawn instances.");
        var buildStopwatch = Stopwatch.StartNew();
        var resolveAssetStopwatch = TimeSpan.Zero;
        var prepareUnityAssetStopwatch = TimeSpan.Zero;
        var texturePlanStopwatch = TimeSpan.Zero;
        foreach (var spawn in uniquePrefabs)
        {
            context?.ThrowIfCancellationRequested();
            try
            {
                var prefabKey = BuildPrefabKey(spawn);
                context?.Report(
                    "Creatures",
                    $"Resolve {spawn.DisplayName} ({preparedBuilds.Count + prefabPaths.Count + 1}/{uniquePrefabs.Length})",
                    ComputeProgress(0.20f, 0.52f, preparedBuilds.Count + prefabPaths.Count, uniquePrefabs.Length));
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

                var resolveStart = Stopwatch.StartNew();
                var sharedAsset = resolver.ResolveAssetNamed(spawn.MeshResource.PackagePath, spawn.MeshResource.ObjectName);
                resolveStart.Stop();
                resolveAssetStopwatch += resolveStart.Elapsed;

                var prepareStart = Stopwatch.StartNew();
                var prepared = L2SkeletalAnimatorPrefabBuilder.PrepareFromResolvedAsset(
                    clientRoot,
                    sharedAsset,
                    L2AssetManager.ManagedCreaturePrefabsRoot,
                    L2AssetManager.SharedSkeletalCharactersRoot,
                    spawn.MeshResource.Reference,
                    log,
                    buildContext,
                    includeMaterials: false);
                ApplySpawnTextureOverrides(prepared.CharacterAsset, spawn);
                prepareStart.Stop();
                prepareUnityAssetStopwatch += prepareStart.Elapsed;

                var texturePlanStart = Stopwatch.StartNew();
                var texturePlans = CreatureSkeletalMaterialImporter.CollectTextureImportPlans(
                    prepared.CharacterAsset,
                    prepared.ReferenceText,
                    log,
                    buildContext);
                texturePlanStart.Stop();
                texturePlanStopwatch += texturePlanStart.Elapsed;
                foreach (var texturePlan in texturePlans)
                {
                    if (texturePlan != null && !string.IsNullOrWhiteSpace(texturePlan.TextureReference))
                    {
                        texturePlansByReference[texturePlan.TextureReference] = texturePlan;
                    }
                }

                preparedBuilds.Add((prefabKey, spawn.DisplayName, prepared));
            }
            catch (Exception ex)
            {
                log($"[Creatures] Failed to build prefab for '{spawn.DisplayName}' ({spawn.MeshResource.Reference}): {ex.Message}");
            }
        }

        log($"[Creatures/Timing] Resolve skeletal assets (plugin) took {resolveAssetStopwatch.TotalSeconds:F2}s");
        log($"[Creatures/Timing] Prepare shared Unity skeletal assets took {prepareUnityAssetStopwatch.TotalSeconds:F2}s");
        log($"[Creatures/Timing] Collect texture import plans took {texturePlanStopwatch.TotalSeconds:F2}s");

        context?.Report("Creatures", $"Import {texturePlansByReference.Count} texture plans", 0.56f);
        var textureImportStopwatch = Stopwatch.StartNew();
        CreatureSkeletalMaterialImporter.ImportTexturePlansBatch(
            texturePlansByReference.Values.ToArray(),
            L2AssetManager.SharedTexturesRoot);
        textureImportStopwatch.Stop();
        log($"[Creatures/Timing] Texture asset import batch took {textureImportStopwatch.Elapsed.TotalSeconds:F2}s");

        context?.Report("Creatures", "Save and refresh texture assets", 0.60f);
        var textureRefreshStopwatch = Stopwatch.StartNew();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        textureRefreshStopwatch.Stop();
        log($"[Creatures/Timing] Save/refresh after texture import took {textureRefreshStopwatch.Elapsed.TotalSeconds:F2}s");

        context?.Report("Creatures", $"Build materials for {preparedBuilds.Count} prefabs", 0.66f);
        var materialBatchStopwatch = Stopwatch.StartNew();
        UnityAssetDatabaseUtility.RunAssetEditingBatch(() =>
        {
            for (var i = 0; i < preparedBuilds.Count; i++)
            {
                context?.ThrowIfCancellationRequested();
                try
                {
                    var preparedWithMaterials = L2SkeletalAnimatorPrefabBuilder.MaterializePreparedBuildMaterials(
                        preparedBuilds[i].Build,
                        log);
                    preparedBuilds[i] = (preparedBuilds[i].PrefabKey, preparedBuilds[i].DisplayName, preparedWithMaterials);
                }
                catch (Exception ex)
                {
                    log($"[Creatures] Failed to build materials for '{preparedBuilds[i].DisplayName}' ({preparedBuilds[i].Build.ReferenceText}): {ex.Message}");
                }
            }
        });
        materialBatchStopwatch.Stop();
        log($"[Creatures/Timing] Material asset batch took {materialBatchStopwatch.Elapsed.TotalSeconds:F2}s");

        context?.Report("Creatures", "Save and refresh material assets", 0.72f);
        var materialRefreshStopwatch = Stopwatch.StartNew();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        materialRefreshStopwatch.Stop();
        log($"[Creatures/Timing] Save/refresh after material batch took {materialRefreshStopwatch.Elapsed.TotalSeconds:F2}s");

        context?.Report("Creatures", $"Build meshes/clips/controllers/prefabs for {preparedBuilds.Count} prefabs", 0.78f);
        var finalizePrefabBatchStopwatch = Stopwatch.StartNew();
        UnityAssetDatabaseUtility.RunAssetEditingBatch(() =>
        {
            for (var i = 0; i < preparedBuilds.Count; i++)
            {
                context?.ThrowIfCancellationRequested();
                try
                {
                    var build = L2SkeletalAnimatorPrefabBuilder.CompletePreparedBuild(
                        preparedBuilds[i].Build,
                        prefabNameSuffix: null,
                        displayLabel: preparedBuilds[i].DisplayName,
                        log,
                        finalizeAssets: false);
                    prefabPaths[preparedBuilds[i].PrefabKey] = build.PrefabPath;
                }
                catch (Exception ex)
                {
                    log($"[Creatures] Failed to complete prefab build for '{preparedBuilds[i].DisplayName}' ({preparedBuilds[i].Build.ReferenceText}): {ex.Message}");
                }
            }
        });
        finalizePrefabBatchStopwatch.Stop();
        log($"[Creatures/Timing] Final skeletal asset batch (mesh/clip/controller/prefab) took {finalizePrefabBatchStopwatch.Elapsed.TotalSeconds:F2}s");

        context?.Report("Creatures", "Save and refresh prefab assets", 0.86f);
        var prefabRefreshStopwatch = Stopwatch.StartNew();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        prefabRefreshStopwatch.Stop();
        log($"[Creatures/Timing] Save/refresh after prefab batch took {prefabRefreshStopwatch.Elapsed.TotalSeconds:F2}s");

        context?.Report("Creatures", $"Load {prefabPaths.Count} prefab assets", 0.90f);
        var prefabLoadStopwatch = Stopwatch.StartNew();
        foreach (var pair in prefabPaths)
        {
            context?.ThrowIfCancellationRequested();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(pair.Value);
            if (prefab != null)
            {
                prefabCache[pair.Key] = prefab;
            }
        }
        prefabLoadStopwatch.Stop();
        log($"[Creatures/Timing] Prefab asset loadback took {prefabLoadStopwatch.Elapsed.TotalSeconds:F2}s");
        buildStopwatch.Stop();
        log($"[Creatures] Prefab batch complete. RequestedTypes={uniquePrefabs.Length}, loaded={prefabCache.Count} ({buildStopwatch.Elapsed.TotalSeconds:F2}s)");

        return prefabCache;
    }

    private static float ComputeProgress(float start, float end, int completed, int total)
    {
        if (total <= 0)
        {
            return end;
        }

        var t = Mathf.Clamp01((completed + 1f) / total);
        return Mathf.Lerp(start, end, t);
    }

    private static void ApplySpawnTextureOverrides(L2SkeletalCharacterAsset asset, SceneCreatureSpawnData spawn)
    {
        if (asset == null || spawn?.TextureResources == null || spawn.TextureResources.Length == 0)
        {
            return;
        }

        var textureRefs = spawn.TextureResources
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Reference))
            .Select(x => x.Reference)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (textureRefs.Length == 0)
        {
            return;
        }

        asset.UsedTextures = spawn.TextureResources
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Reference))
            .Select(x => new L2SkeletalTextureRefData
            {
                Reference = x.Reference,
                ResolvedPackagePath = x.PackagePath ?? string.Empty
            })
            .ToArray();

        if (string.IsNullOrWhiteSpace(asset.PrimaryTextureReference))
        {
            asset.PrimaryTextureReference = textureRefs[0];
        }

        var bindings = asset.MaterialBindings ?? Array.Empty<L2SkeletalMaterialBindingData>();
        for (var i = 0; i < bindings.Length && i < textureRefs.Length; i++)
        {
            var binding = bindings[i];
            if (binding == null || !string.IsNullOrWhiteSpace(binding.TextureReference))
            {
                continue;
            }

            if (!TrySplitReference(textureRefs[i], out var packageName, out var objectName))
            {
                continue;
            }

            binding.PackageName = packageName;
            binding.ObjectName = objectName;
            binding.TextureReference = textureRefs[i];
            binding.ResolvedPackagePath = spawn.TextureResources[i]?.PackagePath ?? string.Empty;
        }
    }

    private static bool TrySplitReference(string reference, out string packageName, out string objectName)
    {
        packageName = null;
        objectName = null;
        if (string.IsNullOrWhiteSpace(reference))
        {
            return false;
        }

        var dotIndex = reference.LastIndexOf('.');
        if (dotIndex <= 0 || dotIndex >= reference.Length - 1)
        {
            return false;
        }

        packageName = reference.Substring(0, dotIndex);
        objectName = reference.Substring(dotIndex + 1);
        return true;
    }

    private static bool ShouldReuseExistingPrefab(GameObject prefab, SceneCreatureSpawnData spawn)
    {
        if (prefab == null || spawn?.MeshResource == null)
        {
            return false;
        }

        var characterAssetPath = BuildCreatureCharacterAssetPath(spawn);
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

    private static string BuildCreatureCharacterAssetPath(SceneCreatureSpawnData spawn)
    {
        var packageName = spawn?.MeshResource?.PackageName;
        var objectName = spawn?.MeshResource?.ObjectName;
        var objectRoot = L2AssetManager.BuildClientPackageObjectRoot(
            L2AssetManager.SharedSkeletalCharactersRoot,
            packageName,
            objectName,
            "SkeletalCharacters");
        return L2AssetManager.BuildAssetPathInFolder(
            objectRoot,
            "NPC",
            objectName ?? "SkeletalCharacter",
            "asset",
            "skeleton");
    }
}
