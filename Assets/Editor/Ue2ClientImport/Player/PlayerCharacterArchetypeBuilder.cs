using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services.CharacterServices;
using L2Viewer.SceneDomain.Services.Utility;
using UnityEditor;
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
        var characterName = $"{baseAppearance.Gender}_{baseAppearance.BaseClass}_{baseAppearance.VisualFamily}";
        var skeletonPackageName = Path.GetFileNameWithoutExtension(baseAppearance.SkeletonMeshLocation.PackagePath);
        var skeletonObjectName = baseAppearance.SkeletonMeshLocation.ObjectName ?? characterName;
        var referenceText = L2AssetManager.BuildReferenceText(skeletonPackageName, skeletonObjectName, characterName);
        var skeletalAssetRoot = L2AssetManager.BuildClientPackageObjectRoot(
            PlayerCharacterImportBuilder.AssetOutputRoot,
            skeletonPackageName,
            skeletonObjectName,
            "PlayerCharacters");

        var characterAssetPath = L2AssetManager.BuildAssetPathInFolder(
            skeletalAssetRoot,
            "PC",
            skeletonObjectName,
            "asset",
            "skeleton");
        var archetypeFolder = $"{PlayerCharacterImportBuilder.PrefabOutputRoot}/Archetypes/{baseAppearance.BaseClass}/{baseAppearance.Gender}";
        var archetypeAssetPath = L2AssetManager.BuildAssetPathInFolder(
            archetypeFolder,
            "PCA",
            skeletonObjectName,
            "asset",
            "archetype");
        var prefabPath = L2AssetManager.BuildClientPackageAssetPath(
            PlayerCharacterImportBuilder.PrefabOutputRoot,
            referenceText,
            "PF",
            "prefab",
            "PlayerCharacterPrefabs",
            "wardrobe");

        if (AssetDatabase.LoadAssetAtPath<L2PlayerCharacterArchetypeAsset>(archetypeAssetPath) != null &&
            AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
        {
            log?.Invoke($"[PlayerArchetype] Reusing existing archetype/prefab: {prefabPath}");
            importStopwatch.Stop();
            log?.Invoke($"[PlayerArchetype/Timing] Total archetype import took {importStopwatch.Elapsed.TotalSeconds:F2}s");
            context?.Report("Player Archetype", "Done", 1f);
            return new ImportResult(prefabPath, archetypeAssetPath);
        }

        L2AssetManager.EnsureFolderExists(PlayerCharacterImportBuilder.AssetOutputRoot);
        L2AssetManager.EnsureFolderExists(PlayerCharacterImportBuilder.PrefabOutputRoot);

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

        var baseAsset = L2SkeletalCharacterAssetFactory.Build(characterName, baseSharedAsset);
        baseAsset = UnityAssetDatabaseUtility.CreateAssetIfMissing(baseAsset, characterAssetPath);
        log?.Invoke($"[PlayerArchetype] Base skeletal asset ready: {characterAssetPath}");

        context?.Report("Player Archetype", "Bake animation clips/controller", 0.14f);
        var buildContext = L2SkeletalAnimatorPrefabBuilder.CreateBuildContext(clientRoot);
        context?.Report("Player Archetype", "Preload archetype textures", 0.18f);
        CreatureSkeletalMaterialImporter.PreloadTextureReferences(
            EnumerateArchetypeTextureReferences(baseAppearance, appearanceOptions, equipmentCatalog),
            log,
            buildContext);
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

        context?.Report("Player Archetype", "Scan skeletal package index", 0.22f);
        var slots = PlayerCharacterSlotCatalogBuilder.Build(new PlayerCharacterSlotCatalogBuilder.BuildArgs
        {
            ClientRoot = clientRoot,
            BaseAppearance = baseAppearance,
            AppearanceOptions = appearanceOptions,
            EquipmentCatalog = equipmentCatalog,
            BaseSharedAsset = baseSharedAsset,
            PackageIndex = packageIndex,
            BuildContext = buildContext,
            PartAssetCache = new Dictionary<string, SceneSkeletalAsset>(StringComparer.OrdinalIgnoreCase),
            MeshAssetCache = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase),
            Context = context,
            Log = log
        });

        context?.Report("Player Archetype", "Write archetype asset", 0.92f);
        var archetype = ScriptableObject.CreateInstance<L2PlayerCharacterArchetypeAsset>();
        archetype.ArchetypeName = characterName;
        archetype.BaseClass = baseClass.ToString();
        archetype.Gender = gender.ToString();
        archetype.VisualFamily = baseAppearance.VisualFamily.ToString();
        archetype.BaseAsset = baseAsset;
        archetype.AnimatorController = controller;
        archetype.Slots = slots;
        archetype = UnityAssetDatabaseUtility.CreateOrReplaceAsset(archetype, archetypeAssetPath);

        context?.Report("Player Archetype", "Create wardrobe prefab", 0.96f);
        CreateWardrobePrefab(baseAsset, archetype, prefabPath, characterName, log);

        AssetDatabase.SaveAssets();

        InstantiatePrefabIfAvailable(prefabPath, log);

        importStopwatch.Stop();
        log?.Invoke($"[PlayerArchetype/Timing] Total archetype import took {importStopwatch.Elapsed.TotalSeconds:F2}s");
        context?.Report("Player Archetype", "Done", 1f);
        return new ImportResult(prefabPath, archetypeAssetPath);
    }

    private static void InstantiatePrefabIfAvailable(string prefabPath, Action<string> log)
    {
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

    internal static IEnumerable<SceneResourceReference> EnumerateCanonicalMeshReferences(
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
                if (!PlayerCharacterSlotCatalogBuilder.CanBuildEquipmentItem(slot.Slot, item))
                {
                    continue;
                }

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

    private static IEnumerable<string> EnumerateArchetypeTextureReferences(
        SceneCharacterAppearanceData baseAppearance,
        SceneCharacterAppearanceOptionsData appearanceOptions,
        SceneCharacterEquipmentCatalogData equipmentCatalog)
    {
        foreach (var part in baseAppearance?.Parts ?? Array.Empty<SceneCharacterResolvedPartData>())
        {
            foreach (var texture in part?.TextureResources ?? Array.Empty<SceneResourceReference>())
            {
                if (!string.IsNullOrWhiteSpace(texture?.Reference))
                {
                    yield return texture.Reference;
                }
            }
        }

        foreach (var face in appearanceOptions?.FaceOptions ?? Array.Empty<SceneCharacterFaceOptionData>())
        {
            foreach (var texture in face?.TextureResources ?? Array.Empty<SceneResourceReference>())
            {
                if (!string.IsNullOrWhiteSpace(texture?.Reference))
                {
                    yield return texture.Reference;
                }
            }
        }

        foreach (var hairStyle in appearanceOptions?.HairStyleOptions ?? Array.Empty<SceneCharacterHairStyleOptionData>())
        {
            foreach (var hairColor in hairStyle?.HairColorOptions ?? Array.Empty<SceneCharacterHairColorOptionData>())
            {
                foreach (var texture in hairColor?.TextureResources ?? Array.Empty<SceneResourceReference>())
                {
                    if (!string.IsNullOrWhiteSpace(texture?.Reference))
                    {
                        yield return texture.Reference;
                    }
                }
            }
        }

        foreach (var slot in equipmentCatalog?.Slots ?? Array.Empty<SceneCharacterEquipmentCatalogSlotData>())
        {
            foreach (var item in slot?.Items ?? Array.Empty<SceneCharacterEquipmentCatalogItemData>())
            {
                if (!PlayerCharacterSlotCatalogBuilder.CanBuildEquipmentItem(slot.Slot, item))
                {
                    continue;
                }

                foreach (var texture in item.TextureResources ?? Array.Empty<SceneResourceReference>())
                {
                    if (!string.IsNullOrWhiteSpace(texture?.Reference))
                    {
                        yield return texture.Reference;
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

    private static void CreateWardrobePrefab(
        L2SkeletalCharacterAsset baseAsset,
        L2PlayerCharacterArchetypeAsset archetype,
        string prefabPath,
        string characterName,
        Action<string> log)
    {
        L2ModularCharacterPrefabFactory.Create(
            archetype,
            prefabPath,
            $"PC_{characterName}",
            build =>
            {
                var metadata = build.Root.AddComponent<L2PlayerCharacterDebugMetadata>();
                metadata.BaseClass = archetype.BaseClass;
                metadata.Gender = archetype.Gender;
                metadata.VisualFamily = archetype.VisualFamily;
                metadata.SkeletonReference = baseAsset.MeshObjectName;
                metadata.SkeletonUri = baseAsset.SourcePackagePath;

                var wardrobe = build.Root.AddComponent<L2PlayerCharacterWardrobe>();
                wardrobe.Archetype = archetype;
                wardrobe.SkeletonRoot = build.SkeletonRoot;
                wardrobe.RootBone = build.RootBone;
                wardrobe.Bones = build.Bones;
                wardrobe.SlotBindings = build.SlotBindings;
                wardrobe.Animator = build.Animator;
                wardrobe.ApplyAppearance();
                wardrobe.ApplyAnimation();

                EditorUtility.SetDirty(metadata);
                EditorUtility.SetDirty(wardrobe);
                EditorUtility.SetDirty(build.Animator);
                EditorUtility.SetDirty(build.Root);
            },
            replaceExisting: true);
        log?.Invoke($"[PlayerArchetype] Wardrobe prefab ready: {prefabPath}");
    }
}
