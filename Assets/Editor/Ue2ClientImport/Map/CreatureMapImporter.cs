using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Services.CharacterServices;
using L2Viewer.SceneDomain.Services.Utility;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

internal static class CreatureMapImporter
{
    private const float UnrealUnitsToDegrees = 360f / 65536f;

    public static Task ImportAsync(
        MapImportRequest request,
        Ue2MapSource source,
        Action<string> log,
        MapImportExecutionContext context = null,
        bool finalizeScene = true)
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
        var creatureRootName = $"{request.ObjectName}_Creatures";
        if (UnitySceneObjectUtility.ObjectExists(creatureRootName))
        {
            log($"[Creatures] Skipping import because '{creatureRootName}' already exists.");
            return Task.CompletedTask;
        }

        var creatureRoot = new GameObject(creatureRootName);
        creatureRoot.transform.SetParent(mapRoot.transform, false);

        context?.Report("Creatures", "Build prefab cache", 0.20f);
        var prefabCache = BuildPrefabCache(selectedSpawns, source.ClientPath, dbRootPath, log, context);
        context?.Report("Creatures", "Place spawn instances", 0.92f);
        var placementStopwatch = Stopwatch.StartNew();
        PlaceSpawns(selectedSpawns, creatureRoot, prefabCache, log);
        placementStopwatch.Stop();
        log($"[Creatures/Timing] Spawn placement took {placementStopwatch.Elapsed.TotalSeconds:F2}s");
        if (finalizeScene)
        {
            context?.Report("Creatures", "Finalize scene objects", 0.98f);
            MapImportFinalizer.Complete(mapRoot, log);
        }
        importStopwatch.Stop();
        log($"[Creatures/Timing] Total creature import took {importStopwatch.Elapsed.TotalSeconds:F2}s");
        log("[Creatures] Import finished.");
        return Task.CompletedTask;
    }

    private static Dictionary<string, GameObject> BuildPrefabCache(
        IReadOnlyList<SceneCreatureSpawnData> spawns,
        string clientRoot,
        string dbRootPath,
        Action<string> log,
        MapImportExecutionContext context)
    {
        var resolver = new SceneSkeletalMeshResolver();
        var buildContext = L2SkeletalAnimatorPrefabBuilder.CreateBuildContext(clientRoot);
        var weaponCatalog = CreatureWeaponCatalog.Build(clientRoot, dbRootPath, log);
        var packageIndex = ScenePackageIndexer.BuildResourcePackageIndex(clientRoot);
        var prefabCache = new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        var prefabPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var preparedBuilds = new List<(string PrefabKey, string DisplayName, SceneCreatureSpawnData Spawn, L2SkeletalAnimatorPrefabBuilder.PreparedBuildData Build)>();
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
                if (ShouldReuseExistingPrefab(existingPrefab, spawn))
                {
                    prefabPaths[prefabKey] = expectedPrefabPath;
                    log($"[Creatures] Reusing existing prefab for '{spawn.DisplayName}': {expectedPrefabPath}");
                    continue;
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

                preparedBuilds.Add((prefabKey, spawn.DisplayName, spawn, prepared));
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
        var createdTextureAssets = CreatureSkeletalMaterialImporter.ImportTexturePlansBatch(
            texturePlansByReference.Values.ToArray(),
            L2AssetManager.SharedTexturesRoot);
        textureImportStopwatch.Stop();
        log($"[Creatures/Timing] Texture asset import batch took {textureImportStopwatch.Elapsed.TotalSeconds:F2}s");

        context?.Report("Creatures", "Refresh texture assets", 0.60f);
        var textureRefreshStopwatch = Stopwatch.StartNew();
        if (createdTextureAssets > 0)
        {
            AssetDatabase.Refresh();
        }
        textureRefreshStopwatch.Stop();
        log($"[Creatures/Timing] Refresh after texture import took {textureRefreshStopwatch.Elapsed.TotalSeconds:F2}s");

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
                    preparedBuilds[i] = (preparedBuilds[i].PrefabKey, preparedBuilds[i].DisplayName, preparedBuilds[i].Spawn, preparedWithMaterials);
                }
                catch (Exception ex)
                {
                    log($"[Creatures] Failed to build materials for '{preparedBuilds[i].DisplayName}' ({preparedBuilds[i].Build.ReferenceText}): {ex.Message}");
                }
            }
        });
        materialBatchStopwatch.Stop();
        log($"[Creatures/Timing] Material asset batch took {materialBatchStopwatch.Elapsed.TotalSeconds:F2}s");

        context?.Report("Creatures", $"Build meshes/clips/controllers/prefabs for {preparedBuilds.Count} prefabs", 0.78f);
        var finalizePrefabBatchStopwatch = Stopwatch.StartNew();
        for (var i = 0; i < preparedBuilds.Count; i++)
        {
            context?.ThrowIfCancellationRequested();
            try
            {
                var weaponSlots = BuildCreatureWeaponSlots(
                    preparedBuilds[i].Build,
                    preparedBuilds[i].Spawn,
                    clientRoot,
                    packageIndex,
                    weaponCatalog,
                    log,
                    context);
                var build = L2SkeletalAnimatorPrefabBuilder.CompletePreparedBuild(
                    preparedBuilds[i].Build,
                    prefabNameSuffix: null,
                    displayLabel: preparedBuilds[i].DisplayName,
                    log,
                    finalizeAssets: false,
                    extraSlots: weaponSlots);
                prefabPaths[preparedBuilds[i].PrefabKey] = build.PrefabPath;
            }
            catch (Exception ex)
            {
                log($"[Creatures] Failed to complete prefab build for '{preparedBuilds[i].DisplayName}' ({preparedBuilds[i].Build.ReferenceText}): {ex.Message}");
            }
        }
        finalizePrefabBatchStopwatch.Stop();
        log($"[Creatures/Timing] Final skeletal asset batch (mesh/clip/controller/prefab) took {finalizePrefabBatchStopwatch.Elapsed.TotalSeconds:F2}s");

        context?.Report("Creatures", "Save prefab assets", 0.86f);
        var prefabRefreshStopwatch = Stopwatch.StartNew();
        AssetDatabase.SaveAssets();
        prefabRefreshStopwatch.Stop();
        log($"[Creatures/Timing] Save after prefab batch took {prefabRefreshStopwatch.Elapsed.TotalSeconds:F2}s");

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

    private static IReadOnlyList<L2CharacterSlotCatalogData> BuildCreatureWeaponSlots(
        L2SkeletalAnimatorPrefabBuilder.PreparedBuildData prepared,
        SceneCreatureSpawnData spawn,
        string clientRoot,
        IReadOnlyDictionary<string, string> packageIndex,
        CreatureWeaponCatalog weaponCatalog,
        Action<string> log,
        MapImportExecutionContext context)
    {
        if (prepared?.SharedAsset == null || spawn == null || weaponCatalog == null)
        {
            return Array.Empty<L2CharacterSlotCatalogData>();
        }

        var slots = new Dictionary<string, L2CharacterSlotCatalogData>(StringComparer.OrdinalIgnoreCase);
        AddCreatureWeaponSlot(
            slots,
            SceneCharacterPaperdollSlot.RightHand,
            spawn.RightHandItemId,
            prepared,
            clientRoot,
            packageIndex,
            weaponCatalog,
            log,
            context);
        AddCreatureWeaponSlot(
            slots,
            SceneCharacterPaperdollSlot.LeftHand,
            spawn.LeftHandItemId,
            prepared,
            clientRoot,
            packageIndex,
            weaponCatalog,
            log,
            context);

        return slots.Values.ToArray();
    }

    private static void AddCreatureWeaponSlot(
        IDictionary<string, L2CharacterSlotCatalogData> slots,
        SceneCharacterPaperdollSlot sourceSlot,
        int itemId,
        L2SkeletalAnimatorPrefabBuilder.PreparedBuildData prepared,
        string clientRoot,
        IReadOnlyDictionary<string, string> packageIndex,
        CreatureWeaponCatalog weaponCatalog,
        Action<string> log,
        MapImportExecutionContext context)
    {
        if (itemId <= 0 || !weaponCatalog.TryGet(itemId, out var item))
        {
            return;
        }

        var slot = ResolveCreatureWeaponSlot(sourceSlot, item);
        var slotName = slot.ToString();
        if (slots.ContainsKey(slotName))
        {
            return;
        }

        try
        {
            CreatureSkeletalMaterialImporter.PreloadTextureReferences(
                (item.TextureResources ?? Array.Empty<SceneResourceReference>())
                .Where(x => !string.IsNullOrWhiteSpace(x?.Reference))
                .Select(x => x.Reference),
                log,
                prepared.Context);
            var variant = PlayerCharacterSlotCatalogBuilder.BuildVariant(
                slotName,
                $"{item.ItemId} {item.DisplayName}",
                $"{slotName.ToLowerInvariant()}_{item.ItemId}",
                item.ItemId,
                -1,
                item.MeshResources,
                item.TextureResources,
                new PlayerCharacterSlotCatalogBuilder.BuildArgs
                {
                    ClientRoot = clientRoot,
                    BaseSharedAsset = prepared.SharedAsset,
                    PackageIndex = packageIndex,
                    BuildContext = prepared.Context,
                    PartAssetCache = new Dictionary<string, SceneSkeletalAsset>(StringComparer.OrdinalIgnoreCase),
                    MeshAssetCache = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase),
                    Context = context,
                    AssetOutputRoot = L2AssetManager.SharedSkeletalCharactersRoot,
                    Log = log
                },
                progress: null);
            slots[slotName] = new L2CharacterSlotCatalogData
            {
                SlotName = slotName,
                DefaultVariantIndex = 0,
                Variants = new[] { variant }
            };
        }
        catch (Exception ex)
        {
            log?.Invoke($"[Creatures] Failed to build weapon {itemId} for '{prepared.CharacterName}': {ex.Message}");
        }
    }

    private static SceneCharacterPaperdollSlot ResolveCreatureWeaponSlot(
        SceneCharacterPaperdollSlot sourceSlot,
        SceneCharacterEquipmentCatalogItemData item)
    {
        var paperdollSlots = item?.PaperdollSlots ?? Array.Empty<SceneCharacterPaperdollSlot>();
        if (paperdollSlots.Contains(SceneCharacterPaperdollSlot.LeftRightHand))
        {
            return SceneCharacterPaperdollSlot.LeftRightHand;
        }

        if (paperdollSlots.Contains(SceneCharacterPaperdollSlot.LeftHand))
        {
            return SceneCharacterPaperdollSlot.LeftHand;
        }

        if (paperdollSlots.Contains(SceneCharacterPaperdollSlot.RightHand))
        {
            return SceneCharacterPaperdollSlot.RightHand;
        }

        return sourceSlot;
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

        if (!PrefabHasUsableAnimatorController(prefab))
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

    private static bool PrefabHasUsableAnimatorController(GameObject prefab)
    {
        var controller = prefab
            .GetComponentInChildren<L2CreatureWardrobe>(true)
            ?.Archetype
            ?.AnimatorController as AnimatorController;
        return controller != null &&
               controller.layers != null &&
               controller.layers.Length > 0 &&
               controller.layers[0].stateMachine != null;
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

            var visual = InstantiateSceneObject(prefab, parent.transform);
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

    private static GameObject InstantiateSceneObject(GameObject prefab, Transform parent)
    {
        return prefab == null
            ? null
            : UnityEngine.Object.Instantiate(prefab, parent, false);
    }

    private static string BuildPrefabKey(SceneCreatureSpawnData spawn)
    {
        var visualKey = spawn?.MeshResource?.Reference
            ?? spawn?.MeshResource?.ObjectName
            ?? spawn?.VisualKey
            ?? string.Empty;
        return $"{visualKey}|rhand={spawn?.RightHandItemId ?? 0}|lhand={spawn?.LeftHandItemId ?? 0}";
    }

    private static string BuildPrefabPath(SceneCreatureSpawnData spawn)
    {
        return L2AssetManager.BuildClientPackageAssetPath(
            L2AssetManager.ManagedCreaturePrefabsRoot,
            spawn.MeshResource.Reference,
            "PF",
            "prefab",
            "CreaturePrefabs",
            BuildWeaponPrefabSuffix(spawn));
    }

    private static string BuildWeaponPrefabSuffix(SceneCreatureSpawnData spawn)
    {
        var rightHandItemId = spawn?.RightHandItemId ?? 0;
        var leftHandItemId = spawn?.LeftHandItemId ?? 0;
        return rightHandItemId <= 0 && leftHandItemId <= 0
            ? null
            : $"weapon_r{rightHandItemId}_l{leftHandItemId}";
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

    private sealed class CreatureWeaponCatalog
    {
        private readonly Dictionary<int, SceneCharacterEquipmentCatalogItemData> _itemsById;

        private CreatureWeaponCatalog(Dictionary<int, SceneCharacterEquipmentCatalogItemData> itemsById)
        {
            _itemsById = itemsById ?? new Dictionary<int, SceneCharacterEquipmentCatalogItemData>();
        }

        public static CreatureWeaponCatalog Build(string clientRoot, string dbRootPath, Action<string> log)
        {
            if (string.IsNullOrWhiteSpace(clientRoot) || string.IsNullOrWhiteSpace(dbRootPath))
            {
                return new CreatureWeaponCatalog(new Dictionary<int, SceneCharacterEquipmentCatalogItemData>());
            }

            try
            {
                var catalog = new SceneCharacterEquipmentCatalogBuilder().Build(
                    clientRoot,
                    dbRootPath,
                    SceneCharacterBaseClass.HumanFighter,
                    SceneCharacterGender.Male);
                var items = new Dictionary<int, SceneCharacterEquipmentCatalogItemData>();
                foreach (var slot in catalog?.Slots ?? Array.Empty<SceneCharacterEquipmentCatalogSlotData>())
                {
                    if (slot == null || !PlayerCharacterSlotCatalogBuilder.IsWeaponSlot(slot.Slot))
                    {
                        continue;
                    }

                    foreach (var item in slot.Items ?? Array.Empty<SceneCharacterEquipmentCatalogItemData>())
                    {
                        if (item == null ||
                            item.ItemId <= 0 ||
                            !PlayerCharacterSlotCatalogBuilder.CanBuildEquipmentItem(slot.Slot, item))
                        {
                            continue;
                        }

                        items[item.ItemId] = item;
                    }
                }

                log?.Invoke($"[Creatures] Loaded {items.Count} weapon visuals from equipment catalog.");
                return new CreatureWeaponCatalog(items);
            }
            catch (Exception ex)
            {
                log?.Invoke($"[Creatures] Failed to load weapon visual catalog: {ex.Message}");
                return new CreatureWeaponCatalog(new Dictionary<int, SceneCharacterEquipmentCatalogItemData>());
            }
        }

        public bool TryGet(int itemId, out SceneCharacterEquipmentCatalogItemData item)
        {
            return _itemsById.TryGetValue(itemId, out item);
        }
    }
}
