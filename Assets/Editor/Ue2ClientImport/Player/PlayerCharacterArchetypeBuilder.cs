using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services.CharacterServices;
using L2Viewer.SceneDomain.Services.Utility;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

internal static class PlayerCharacterArchetypeBuilder
{
    internal readonly struct ImportResult
    {
        public ImportResult(string prefabPath, string archetypeAssetPath)
        {
            PrefabPath = prefabPath;
            ArchetypeAssetPath = archetypeAssetPath;
        }

        public string PrefabPath { get; }
        public string ArchetypeAssetPath { get; }
    }

    public static ImportResult Import(
        string clientRoot,
        string dbRoot,
        SceneCharacterBaseClass baseClass,
        SceneCharacterGender gender,
        SceneCharacterAppearanceOptionsData appearanceOptions,
        SceneCharacterEquipmentCatalogData equipmentCatalog,
        MapImportExecutionContext context,
        Action<string> log)
    {
        var importStopwatch = Stopwatch.StartNew();
        var baseRequest = new SceneCharacterAppearanceRequest
        {
            BaseClass = baseClass,
            Gender = gender
        };

        context?.Report("Player Archetype", "Resolve base appearance", 0.02f);
        var appearanceBuilder = new SceneCharacterAppearanceBuilder();
        var baseAppearance = appearanceBuilder.Build(clientRoot, baseRequest);
        appearanceOptions ??= new SceneCharacterAppearanceOptionsBuilder().Build(clientRoot, baseClass, gender);
        equipmentCatalog ??= new SceneCharacterEquipmentCatalogBuilder().Build(clientRoot, dbRoot, baseClass, gender);
        var packageIndex = BuildSkeletalPackageIndex(clientRoot);

        context?.Report("Player Archetype", "Resolve base skeleton asset", 0.08f);
        var resolver = new SceneSkeletalMeshResolver();
        var baseSharedAsset = BuildCanonicalSharedAsset(
            clientRoot,
            resolver.ResolveAsset(baseAppearance.SkeletonMeshLocation),
            packageIndex,
            EnumerateCanonicalMeshReferences(baseAppearance, appearanceOptions, equipmentCatalog),
            context);
        var characterName = $"{baseAppearance.Gender}_{baseAppearance.BaseClass}_{baseAppearance.VisualFamily}";
        var referenceText = $"{baseAppearance.SkeletonMeshLocation.Reference}.{baseAppearance.VisualFamily}";

        L2AssetManager.EnsureFolderExists(PlayerCharacterImportBuilder.AssetOutputRoot);
        L2AssetManager.EnsureFolderExists(PlayerCharacterImportBuilder.PrefabOutputRoot);

        var baseAsset = L2SkeletalCharacterAssetFactory.Build(characterName, baseSharedAsset);
        var characterAssetPath = L2AssetManager.BuildClientPackageAssetPath(
            PlayerCharacterImportBuilder.AssetOutputRoot,
            referenceText,
            "PC",
            "asset",
            "PlayerCharacters",
            "skeleton");
        baseAsset = UnityAssetDatabaseUtility.CreateOrReplaceAsset(baseAsset, characterAssetPath);
        log?.Invoke($"[PlayerArchetype] Base skeletal asset updated: {characterAssetPath}");

        context?.Report("Player Archetype", "Bake animation clips/controller", 0.14f);
        var buildContext = L2SkeletalAnimatorPrefabBuilder.CreateBuildContext(clientRoot);
        var sequenceNames = CreatureSkeletalImportUtility.GetAllSequenceNames(baseAsset);
        var clips = CreatureAnimationClipBuilder.Build(
            baseAsset,
            referenceText,
            PlayerCharacterImportBuilder.PrefabOutputRoot,
            sequenceNames,
            log,
            out _);
        var controller = CreatureAnimatorControllerBuilder.Build(
            baseAsset,
            referenceText,
            PlayerCharacterImportBuilder.PrefabOutputRoot,
            clips,
            log,
            out _);

        context?.Report("Player Archetype", "Scan skeletal package index", 0.20f);
        var partAssetCache = new Dictionary<string, SceneSkeletalAsset>(StringComparer.OrdinalIgnoreCase);
        var meshAssetCache = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);
        var slots = BuildSlotCatalogs(
            clientRoot,
            baseAppearance,
            appearanceOptions,
            equipmentCatalog,
            baseSharedAsset,
            packageIndex,
            buildContext,
            referenceText,
            partAssetCache,
            meshAssetCache,
            context,
            log);

        context?.Report("Player Archetype", "Write archetype asset", 0.92f);
        var archetype = ScriptableObject.CreateInstance<L2PlayerCharacterArchetypeAsset>();
        archetype.ArchetypeName = characterName;
        archetype.BaseClass = baseClass.ToString();
        archetype.Gender = gender.ToString();
        archetype.VisualFamily = baseAppearance.VisualFamily.ToString();
        archetype.BaseAsset = baseAsset;
        archetype.AnimatorController = controller;
        archetype.Slots = slots;

        var archetypeAssetPath = L2AssetManager.BuildClientPackageAssetPath(
            PlayerCharacterImportBuilder.AssetOutputRoot,
            referenceText,
            "PCA",
            "asset",
            "PlayerCharacters",
            "archetype");
        archetype = UnityAssetDatabaseUtility.CreateOrReplaceAsset(archetype, archetypeAssetPath);

        context?.Report("Player Archetype", "Create wardrobe prefab", 0.96f);
        var prefabPath = L2AssetManager.BuildClientPackageAssetPath(
            PlayerCharacterImportBuilder.PrefabOutputRoot,
            referenceText,
            "PF",
            "prefab",
            "PlayerCharacterPrefabs",
            "wardrobe");
        CreateWardrobePrefab(baseAsset, archetype, controller, prefabPath, characterName, log);

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(archetypeAssetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab != null)
        {
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance != null)
            {
                instance.name = prefab.name;
                Selection.activeObject = instance;
                log?.Invoke("[PlayerArchetype] Wardrobe prefab instantiated into the current scene and selected.");
            }
        }

        importStopwatch.Stop();
        log?.Invoke($"[PlayerArchetype/Timing] Total archetype import took {importStopwatch.Elapsed.TotalSeconds:F2}s");
        context?.Report("Player Archetype", "Done", 1f);
        return new ImportResult(prefabPath, archetypeAssetPath);
    }

    private static L2PlayerCharacterSlotCatalogData[] BuildSlotCatalogs(
        string clientRoot,
        SceneCharacterAppearanceData baseAppearance,
        SceneCharacterAppearanceOptionsData appearanceOptions,
        SceneCharacterEquipmentCatalogData equipmentCatalog,
        SceneSkeletalAsset baseSharedAsset,
        IReadOnlyDictionary<string, string> packageIndex,
        L2SkeletalAnimatorPrefabBuilder.BuildContext buildContext,
        string referenceText,
        Dictionary<string, SceneSkeletalAsset> partAssetCache,
        Dictionary<string, Mesh> meshAssetCache,
        MapImportExecutionContext context,
        Action<string> log)
    {
        var assetRoot = $"{PlayerCharacterImportBuilder.AssetOutputRoot}/PlayerCharacters/Variants";
        L2AssetManager.EnsureFolderExists(assetRoot);
        var totalVariantCount = EstimateVariantCount(baseAppearance, appearanceOptions, equipmentCatalog);
        var progress = new VariantBuildProgress(totalVariantCount);

        return new[]
        {
            BuildFaceSlot(baseAppearance, appearanceOptions, baseSharedAsset, packageIndex, buildContext, referenceText, assetRoot, clientRoot, partAssetCache, meshAssetCache, progress, context, log),
            BuildHairSlot(appearanceOptions, baseSharedAsset, packageIndex, buildContext, referenceText, assetRoot, clientRoot, partAssetCache, meshAssetCache, progress, context, log),
            BuildEquipmentSlot(SceneCharacterPaperdollSlot.Chest, baseAppearance, equipmentCatalog, baseSharedAsset, packageIndex, buildContext, referenceText, assetRoot, clientRoot, partAssetCache, meshAssetCache, progress, context, log),
            BuildEquipmentSlot(SceneCharacterPaperdollSlot.Legs, baseAppearance, equipmentCatalog, baseSharedAsset, packageIndex, buildContext, referenceText, assetRoot, clientRoot, partAssetCache, meshAssetCache, progress, context, log),
            BuildEquipmentSlot(SceneCharacterPaperdollSlot.Gloves, baseAppearance, equipmentCatalog, baseSharedAsset, packageIndex, buildContext, referenceText, assetRoot, clientRoot, partAssetCache, meshAssetCache, progress, context, log),
            BuildEquipmentSlot(SceneCharacterPaperdollSlot.Feet, baseAppearance, equipmentCatalog, baseSharedAsset, packageIndex, buildContext, referenceText, assetRoot, clientRoot, partAssetCache, meshAssetCache, progress, context, log)
        };
    }

    private static L2PlayerCharacterSlotCatalogData BuildFaceSlot(
        SceneCharacterAppearanceData baseAppearance,
        SceneCharacterAppearanceOptionsData appearanceOptions,
        SceneSkeletalAsset baseSharedAsset,
        IReadOnlyDictionary<string, string> packageIndex,
        L2SkeletalAnimatorPrefabBuilder.BuildContext buildContext,
        string referenceText,
        string assetRoot,
        string clientRoot,
        Dictionary<string, SceneSkeletalAsset> partAssetCache,
        Dictionary<string, Mesh> meshAssetCache,
        VariantBuildProgress progress,
        MapImportExecutionContext context,
        Action<string> log)
    {
        var variants = new List<L2PlayerCharacterVariantData>();
        foreach (var face in appearanceOptions.FaceOptions ?? Array.Empty<SceneCharacterFaceOptionData>())
        {
            variants.Add(BuildVariant(
                clientRoot,
                "Face",
                $"Face {face.Id}",
                $"face_{face.Id:D2}",
                face.Id,
                -1,
                face.MeshResources,
                face.TextureResources,
                baseSharedAsset,
                packageIndex,
                buildContext,
                referenceText,
                assetRoot,
                partAssetCache,
                meshAssetCache,
                progress,
                context,
                log));
        }

        return new L2PlayerCharacterSlotCatalogData
        {
            SlotName = "Face",
            DefaultVariantIndex = ResolveDefaultFaceIndex(baseAppearance.FaceId, variants),
            Variants = variants.ToArray()
        };
    }

    private static L2PlayerCharacterSlotCatalogData BuildHairSlot(
        SceneCharacterAppearanceOptionsData appearanceOptions,
        SceneSkeletalAsset baseSharedAsset,
        IReadOnlyDictionary<string, string> packageIndex,
        L2SkeletalAnimatorPrefabBuilder.BuildContext buildContext,
        string referenceText,
        string assetRoot,
        string clientRoot,
        Dictionary<string, SceneSkeletalAsset> partAssetCache,
        Dictionary<string, Mesh> meshAssetCache,
        VariantBuildProgress progress,
        MapImportExecutionContext context,
        Action<string> log)
    {
        var variants = new List<L2PlayerCharacterVariantData>();
        foreach (var hairStyle in appearanceOptions.HairStyleOptions ?? Array.Empty<SceneCharacterHairStyleOptionData>())
        {
            foreach (var hairColor in hairStyle.HairColorOptions ?? Array.Empty<SceneCharacterHairColorOptionData>())
            {
                variants.Add(BuildVariant(
                    clientRoot,
                    "Hair",
                    $"Hair {hairStyle.Id} / Color {hairColor.Id}",
                    $"hair_{hairStyle.Id:D2}_{hairColor.Id:D2}",
                    hairStyle.Id,
                    hairColor.Id,
                    hairStyle.MeshResources,
                    hairColor.TextureResources,
                    baseSharedAsset,
                    packageIndex,
                    buildContext,
                    referenceText,
                    assetRoot,
                    partAssetCache,
                    meshAssetCache,
                    progress,
                    context,
                    log));
            }
        }

        return new L2PlayerCharacterSlotCatalogData
        {
            SlotName = "Hair",
            DefaultVariantIndex = 0,
            Variants = variants.ToArray()
        };
    }

    private static L2PlayerCharacterSlotCatalogData BuildEquipmentSlot(
        SceneCharacterPaperdollSlot slot,
        SceneCharacterAppearanceData baseAppearance,
        SceneCharacterEquipmentCatalogData equipmentCatalog,
        SceneSkeletalAsset baseSharedAsset,
        IReadOnlyDictionary<string, string> packageIndex,
        L2SkeletalAnimatorPrefabBuilder.BuildContext buildContext,
        string referenceText,
        string assetRoot,
        string clientRoot,
        Dictionary<string, SceneSkeletalAsset> partAssetCache,
        Dictionary<string, Mesh> meshAssetCache,
        VariantBuildProgress progress,
        MapImportExecutionContext context,
        Action<string> log)
    {
        var variants = new List<L2PlayerCharacterVariantData>();
        var basePart = baseAppearance.Parts?.FirstOrDefault(x => x != null && x.Slot == slot);
        if (basePart != null)
        {
            variants.Add(BuildVariant(
                clientRoot,
                slot.ToString(),
                "<Base>",
                $"{slot.ToString().ToLowerInvariant()}_base",
                -1,
                -1,
                basePart.MeshResources,
                basePart.TextureResources,
                baseSharedAsset,
                packageIndex,
                buildContext,
                referenceText,
                assetRoot,
                partAssetCache,
                meshAssetCache,
                progress,
                context,
                log));
        }

        var items = equipmentCatalog?.Slots?
            .FirstOrDefault(x => x.Slot == slot)?
            .Items?
            .Where(x => x != null && x.IsRenderableWithCurrentAppearanceBuilder)
            .ToArray() ?? Array.Empty<SceneCharacterEquipmentCatalogItemData>();
        foreach (var item in items)
        {
            variants.Add(BuildVariant(
                clientRoot,
                slot.ToString(),
                $"{item.ItemId} {item.DisplayName}",
                $"{slot.ToString().ToLowerInvariant()}_{item.ItemId}",
                item.ItemId,
                -1,
                item.MeshResources,
                item.TextureResources,
                baseSharedAsset,
                packageIndex,
                buildContext,
                referenceText,
                assetRoot,
                partAssetCache,
                meshAssetCache,
                progress,
                context,
                log));
        }

        return new L2PlayerCharacterSlotCatalogData
        {
            SlotName = slot.ToString(),
            DefaultVariantIndex = 0,
            Variants = variants.ToArray()
        };
    }

    private static int ResolveDefaultFaceIndex(int defaultFaceId, IReadOnlyList<L2PlayerCharacterVariantData> variants)
    {
        for (var i = 0; i < variants.Count; i++)
        {
            if (variants[i] != null && variants[i].VariantId == defaultFaceId)
            {
                return i;
            }
        }

        return 0;
    }

    private static L2PlayerCharacterVariantData BuildVariant(
        string clientRoot,
        string slotName,
        string displayName,
        string variantKey,
        int variantId,
        int auxVariantId,
        SceneResourceReference[] meshResources,
        SceneResourceReference[] textureResources,
        SceneSkeletalAsset baseSharedAsset,
        IReadOnlyDictionary<string, string> packageIndex,
        L2SkeletalAnimatorPrefabBuilder.BuildContext buildContext,
        string referenceText,
        string assetRoot,
        Dictionary<string, SceneSkeletalAsset> partAssetCache,
        Dictionary<string, Mesh> meshAssetCache,
        VariantBuildProgress progress,
        MapImportExecutionContext context,
        Action<string> log)
    {
        progress.Report(context, slotName, displayName);
        var parts = new List<L2PlayerCharacterVariantPartData>();
        var textures = textureResources?.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Reference)).ToArray()
                       ?? Array.Empty<SceneResourceReference>();
        var meshes = meshResources?.Where(x => x != null).ToArray() ?? Array.Empty<SceneResourceReference>();

        for (var i = 0; i < meshes.Length; i++)
        {
            context?.ThrowIfCancellationRequested();
            var meshReference = meshes[i];
            var location = ResolveMeshLocation(clientRoot, packageIndex, meshReference);
            var sharedAsset = ResolveOrBuildPartAsset(location, baseSharedAsset, partAssetCache);
            var tempAsset = L2SkeletalCharacterAssetFactory.Build(
                $"{slotName}_{variantKey}_{i:D2}",
                sharedAsset);
            ApplyPartTextureOverrides(tempAsset, textures);

            var materialPrefix = $"{referenceText}.{slotName}.{variantKey}.{i:D2}";
            var materials = CreatureSkeletalMaterialImporter.CreateMaterials(tempAsset, materialPrefix, assetRoot, log, buildContext);
            var mesh = ResolveOrBuildMeshAsset(tempAsset, materials, assetRoot, materialPrefix, meshAssetCache, log);

            parts.Add(new L2PlayerCharacterVariantPartData
            {
                Name = $"{slotName}_{variantKey}_{i:D2}",
                Mesh = mesh,
                Materials = materials
            });
        }

        return new L2PlayerCharacterVariantData
        {
            VariantKey = variantKey,
            DisplayName = displayName,
            VariantId = variantId,
            AuxVariantId = auxVariantId,
            Parts = parts.ToArray()
        };
    }

    private static SceneSkeletalAsset ResolveOrBuildPartAsset(
        SceneResourceLocation location,
        SceneSkeletalAsset baseSharedAsset,
        IDictionary<string, SceneSkeletalAsset> cache)
    {
        if (cache.TryGetValue(location.Reference, out var cached))
        {
            return cached;
        }

        var built = PlayerCharacterPartAssetBuilder.BuildMeshOnlyAsset(location, baseSharedAsset);
        cache[location.Reference] = built;
        return built;
    }

    private static Mesh ResolveOrBuildMeshAsset(
        L2SkeletalCharacterAsset tempAsset,
        Material[] materials,
        string assetRoot,
        string materialPrefix,
        IDictionary<string, Mesh> cache,
        Action<string> log)
    {
        var meshKey = $"{tempAsset.SourcePackagePath}|{tempAsset.MeshObjectName}";
        if (cache.TryGetValue(meshKey, out var cached))
        {
            return cached;
        }

        var mesh = CreatureSkinnedMeshBuilder.Build(tempAsset, materials, log, out _);
        var meshAssetPath = L2AssetManager.BuildClientPackageAssetPath(
            assetRoot,
            materialPrefix,
            "SM",
            "asset",
            "PlayerCharacterVariantMeshes",
            "mesh");
        mesh = UnityAssetDatabaseUtility.CreateOrReplaceAsset(mesh, meshAssetPath);
        cache[meshKey] = mesh;
        return mesh;
    }

    private static void ApplyPartTextureOverrides(L2SkeletalCharacterAsset asset, SceneResourceReference[] textureResources)
    {
        if (asset == null || textureResources == null || textureResources.Length == 0)
        {
            return;
        }

        asset.UsedTextures = textureResources
            .Select(x => new L2SkeletalTextureRefData
            {
                Reference = x.Reference,
                ResolvedPackagePath = string.Empty
            })
            .ToArray();
        asset.PrimaryTextureReference = textureResources[0].Reference;

        var bindings = asset.MaterialBindings ?? Array.Empty<L2SkeletalMaterialBindingData>();
        for (var i = 0; i < bindings.Length; i++)
        {
            var texture = textureResources[Math.Min(i, textureResources.Length - 1)];
            bindings[i].PackageName = texture.PackageName;
            bindings[i].ObjectName = texture.ObjectName;
            bindings[i].TextureReference = texture.Reference;
            bindings[i].ResolvedPackagePath = string.Empty;
        }
    }

    private static SceneResourceLocation ResolveMeshLocation(
        string clientRoot,
        IReadOnlyDictionary<string, string> packageIndex,
        SceneResourceReference meshReference)
    {
        if (!packageIndex.TryGetValue(meshReference.PackageName, out var packagePath))
        {
            throw new FileNotFoundException($"Package '{meshReference.PackageName}' for mesh '{meshReference.Reference}' was not found.");
        }

        return SceneReferenceUtilities.BuildResourceLocation(
            clientRoot,
            packagePath,
            meshReference.PackageName,
            meshReference.ObjectName,
            meshReference.ClassName);
    }

    private static readonly Dictionary<string, Dictionary<string, string>> SkeletalPackageIndexCache =
        new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

    private static SceneSkeletalAsset BuildCanonicalSharedAsset(
        string clientRoot,
        SceneSkeletalAsset baseSharedAsset,
        IReadOnlyDictionary<string, string> packageIndex,
        IEnumerable<SceneResourceReference> meshReferences,
        MapImportExecutionContext context)
    {
        var additionalSkeletons = new List<SceneSkeletalSkeleton>();
        var seenReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var meshReference in meshReferences ?? Array.Empty<SceneResourceReference>())
        {
            context?.ThrowIfCancellationRequested();
            if (meshReference == null ||
                string.IsNullOrWhiteSpace(meshReference.Reference) ||
                !seenReferences.Add(meshReference.Reference))
            {
                continue;
            }

            var location = ResolveMeshLocation(clientRoot, packageIndex, meshReference);
            var skeleton = PlayerCharacterPartAssetBuilder.LoadMeshSkeleton(location);
            additionalSkeletons.Add(skeleton);
        }

        return PlayerCharacterSkeletonMergeUtility.BuildCanonicalAsset(baseSharedAsset, additionalSkeletons);
    }

    private static IEnumerable<SceneResourceReference> EnumerateCanonicalMeshReferences(
        SceneCharacterAppearanceData baseAppearance,
        SceneCharacterAppearanceOptionsData appearanceOptions,
        SceneCharacterEquipmentCatalogData equipmentCatalog)
    {
        var supportedEquipmentSlots = new HashSet<SceneCharacterPaperdollSlot>
        {
            SceneCharacterPaperdollSlot.Chest,
            SceneCharacterPaperdollSlot.Legs,
            SceneCharacterPaperdollSlot.Gloves,
            SceneCharacterPaperdollSlot.Feet
        };

        foreach (var part in baseAppearance?.Parts ?? Array.Empty<SceneCharacterResolvedPartData>())
        {
            foreach (var mesh in part?.MeshResources ?? Array.Empty<SceneResourceReference>())
            {
                if (mesh != null)
                {
                    yield return mesh;
                }
            }
        }

        foreach (var face in appearanceOptions?.FaceOptions ?? Array.Empty<SceneCharacterFaceOptionData>())
        {
            foreach (var mesh in face?.MeshResources ?? Array.Empty<SceneResourceReference>())
            {
                if (mesh != null)
                {
                    yield return mesh;
                }
            }
        }

        foreach (var hairStyle in appearanceOptions?.HairStyleOptions ?? Array.Empty<SceneCharacterHairStyleOptionData>())
        {
            foreach (var mesh in hairStyle?.MeshResources ?? Array.Empty<SceneResourceReference>())
            {
                if (mesh != null)
                {
                    yield return mesh;
                }
            }
        }

        foreach (var slot in equipmentCatalog?.Slots ?? Array.Empty<SceneCharacterEquipmentCatalogSlotData>())
        {
            if (slot == null || !supportedEquipmentSlots.Contains(slot.Slot))
            {
                continue;
            }

            foreach (var item in slot?.Items ?? Array.Empty<SceneCharacterEquipmentCatalogItemData>())
            {
                foreach (var mesh in item?.MeshResources ?? Array.Empty<SceneResourceReference>())
                {
                    if (mesh != null)
                    {
                        yield return mesh;
                    }
                }
            }
        }
    }

    private static Dictionary<string, string> BuildSkeletalPackageIndex(string clientRoot)
    {
        if (SkeletalPackageIndexCache.TryGetValue(clientRoot, out var cached))
        {
            return cached;
        }

        var searchRoot = Path.Combine(clientRoot, "animations");
        if (!Directory.Exists(searchRoot))
        {
            searchRoot = clientRoot;
        }

        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var filePath in Directory.EnumerateFiles(searchRoot, "*.ukx", SearchOption.AllDirectories))
        {
            var packageName = Path.GetFileNameWithoutExtension(filePath);
            if (!string.IsNullOrWhiteSpace(packageName) && !index.ContainsKey(packageName))
            {
                index[packageName] = filePath;
            }
        }

        SkeletalPackageIndexCache[clientRoot] = index;
        return index;
    }

    private static int EstimateVariantCount(
        SceneCharacterAppearanceData baseAppearance,
        SceneCharacterAppearanceOptionsData appearanceOptions,
        SceneCharacterEquipmentCatalogData equipmentCatalog)
    {
        var count = 0;
        count += appearanceOptions?.FaceOptions?.Count ?? 0;
        count += (appearanceOptions?.HairStyleOptions ?? Array.Empty<SceneCharacterHairStyleOptionData>())
            .Sum(x => x?.HairColorOptions?.Count ?? 0);

        count += CountEquipmentVariants(SceneCharacterPaperdollSlot.Chest, baseAppearance, equipmentCatalog);
        count += CountEquipmentVariants(SceneCharacterPaperdollSlot.Legs, baseAppearance, equipmentCatalog);
        count += CountEquipmentVariants(SceneCharacterPaperdollSlot.Gloves, baseAppearance, equipmentCatalog);
        count += CountEquipmentVariants(SceneCharacterPaperdollSlot.Feet, baseAppearance, equipmentCatalog);
        return Math.Max(1, count);
    }

    private static int CountEquipmentVariants(
        SceneCharacterPaperdollSlot slot,
        SceneCharacterAppearanceData baseAppearance,
        SceneCharacterEquipmentCatalogData equipmentCatalog)
    {
        var count = baseAppearance?.Parts?.Any(x => x != null && x.Slot == slot) == true ? 1 : 0;
        count += equipmentCatalog?.Slots?
            .FirstOrDefault(x => x.Slot == slot)?
            .Items?
            .Count(x => x != null && x.IsRenderableWithCurrentAppearanceBuilder) ?? 0;
        return count;
    }

    private sealed class VariantBuildProgress
    {
        private readonly int _total;
        private int _completed;

        public VariantBuildProgress(int total)
        {
            _total = Math.Max(1, total);
        }

        public void Report(MapImportExecutionContext context, string slotName, string displayName)
        {
            _completed++;
            var progress = Mathf.Lerp(0.24f, 0.88f, Mathf.Clamp01(_completed / (float)_total));
            context?.Report("Player Archetype", $"Build {slotName}: {displayName} ({_completed}/{_total})", progress);
        }
    }

    private static void CreateWardrobePrefab(
        L2SkeletalCharacterAsset baseAsset,
        L2PlayerCharacterArchetypeAsset archetype,
        AnimatorController controller,
        string prefabPath,
        string characterName,
        Action<string> log)
    {
        var session = L2SceneSkeletalAssetBridge.CreateSession(baseAsset);
        var bindFrame = session.CaptureBindPoseDebugFrame();
        var bonePoses = CreatureSkeletalImportUtility.BuildBonePoses(bindFrame.Bones, baseAsset.Bones);

        var root = new GameObject($"PC_{characterName}");
        try
        {
            var skeletonRoot = new GameObject("Skeleton").transform;
            skeletonRoot.SetParent(root.transform, false);
            var boneTransforms = CreatureSkeletalImportUtility.CreateBoneHierarchy(baseAsset.Bones, bonePoses, skeletonRoot);
            var rootBone = CreatureSkeletalImportUtility.ResolveRootBone(baseAsset.Bones, boneTransforms);

            var slotNames = new[] { "Face", "Hair", "Chest", "Legs", "Gloves", "Feet" };
            var bindings = new List<L2PlayerCharacterWardrobe.SlotBinding>(slotNames.Length);
            foreach (var slotName in slotNames)
            {
                var slotRoot = new GameObject(slotName).transform;
                slotRoot.SetParent(root.transform, false);
                bindings.Add(new L2PlayerCharacterWardrobe.SlotBinding
                {
                    SlotName = slotName,
                    Root = slotRoot
                });
            }

            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            root.AddComponent<L2AnimationNotifyReceiver>();

            var metadata = root.AddComponent<L2PlayerCharacterDebugMetadata>();
            metadata.BaseClass = archetype.BaseClass;
            metadata.Gender = archetype.Gender;
            metadata.VisualFamily = archetype.VisualFamily;
            metadata.SkeletonReference = baseAsset.MeshObjectName;
            metadata.SkeletonUri = baseAsset.SourcePackagePath;

            var wardrobe = root.AddComponent<L2PlayerCharacterWardrobe>();
            wardrobe.Archetype = archetype;
            wardrobe.SkeletonRoot = skeletonRoot;
            wardrobe.RootBone = rootBone;
            wardrobe.Bones = boneTransforms;
            wardrobe.SlotBindings = bindings.ToArray();
            wardrobe.Animator = animator;
            wardrobe.ApplyAppearance();
            wardrobe.ApplyAnimation();

            L2AssetManager.EnsureParentFolderExists(prefabPath);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            log?.Invoke($"[PlayerArchetype] Wardrobe prefab updated: {prefabPath}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
