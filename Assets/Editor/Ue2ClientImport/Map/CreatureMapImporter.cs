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
        log($"[Creatures] DONE Spawn analysis. Found {spawns.Length} spawn records for quadrant '{request.MapKey}'.");

        if (spawns.Length == 0)
        {
            log("[Creatures] No creature spawns were found for the requested map.");
            return Task.CompletedTask;
        }

        MapImportAssetPreparation.EnsureMapOutputFolderExists(request.OutputDir);
        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        UnitySceneObjectUtility.RemoveExistingObject($"{request.ObjectName}_Creatures");

        var creatureRoot = new GameObject($"{request.ObjectName}_Creatures");
        creatureRoot.transform.SetParent(mapRoot.transform, false);

        var prefabCache = BuildPrefabCache(spawns, source.ClientPath, log);
        PlaceSpawns(spawns, creatureRoot, prefabCache, log);
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
        var buildContext = L2SkeletalPrefabAssetBuilder.CreateBuildContext(clientRoot);
        var prefabCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        var prefabPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var uniquePrefabs = spawns
            .Where(x => x != null && x.MeshResource != null && !string.IsNullOrWhiteSpace(x.MeshResource.PackagePath) && !string.IsNullOrWhiteSpace(x.MeshResource.ObjectName))
            .GroupBy(BuildPrefabKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();

        log($"[Creatures] Building {uniquePrefabs.Length} unique prefab assets.");
        var buildStopwatch = Stopwatch.StartNew();
        UnityAssetDatabaseUtility.RunAssetEditingBatch(() =>
        {
            foreach (var spawn in uniquePrefabs)
            {
                try
                {
                    var sharedAsset = resolver.ResolveAssetNamed(spawn.MeshResource.PackagePath, spawn.MeshResource.ObjectName);
                    var build = L2SkeletalPrefabAssetBuilder.BuildFromResolvedAsset(
                        clientRoot,
                        sharedAsset,
                        L2AssetManager.ManagedCreaturePrefabsRoot,
                        L2AssetManager.SharedSkeletalCharactersRoot,
                        spawn.MeshResource.Reference,
                        MakeSafeAssetToken(spawn.DisplayName),
                        spawn.DisplayName,
                        log,
                        buildContext,
                        finalizeAssets: false);
                    prefabPaths[BuildPrefabKey(spawn)] = build.PrefabPath;
                }
                catch (Exception ex)
                {
                    log($"[Creatures] Failed to build prefab for '{spawn.DisplayName}' ({spawn.MeshResource.Reference}): {ex.Message}");
                }
            }
        });

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
        log($"[Creatures] Prefab batch complete. Requested={uniquePrefabs.Length}, loaded={prefabCache.Count} ({buildStopwatch.Elapsed.TotalSeconds:F2}s)");

        return prefabCache;
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

            visual.name = BuildSceneObjectName(spawn);
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

    private static string BuildSceneObjectName(SceneCreatureSpawnData spawn)
    {
        var displayName = string.IsNullOrWhiteSpace(spawn.DisplayName) ? "Creature" : spawn.DisplayName.Trim();
        return $"{spawn.SpawnLocationKey}_{spawn.SpawnId}_{displayName}";
    }

    private static string BuildPrefabKey(SceneCreatureSpawnData spawn)
    {
        return $"{spawn.VisualKey}::{spawn.DisplayName}";
    }

    private static string MakeSafeAssetToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Creature";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim()
            .Select(ch => invalid.Contains(ch) || ch == '/' || ch == '\\' || ch == ':' ? '_' : ch)
            .ToArray();
        return new string(chars);
    }
}
