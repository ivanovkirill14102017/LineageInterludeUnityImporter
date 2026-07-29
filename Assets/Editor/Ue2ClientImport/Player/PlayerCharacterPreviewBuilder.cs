using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services.CharacterServices;
using L2Viewer.SceneDomain.Services.MaterialServices;
using L2Viewer.SceneDomain.Services.Utility;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

internal static class PlayerCharacterPreviewBuilder
{
    private static readonly Dictionary<string, Dictionary<string, string>> SkeletalPackageIndexCache =
        new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);

    internal readonly struct ImportResult
    {
        public ImportResult(string prefabPath, string characterAssetPath)
        {
            PrefabPath = prefabPath;
            CharacterAssetPath = characterAssetPath;
        }

        public string PrefabPath { get; }
        public string CharacterAssetPath { get; }
    }

    private readonly struct EquippedSlotSelection
    {
        public EquippedSlotSelection(SceneCharacterEquipmentCatalogItemData item)
        {
            Item = item;
        }

        public SceneCharacterEquipmentCatalogItemData Item { get; }
        public bool HasValue => Item != null;
        public int? ItemId => Item?.ItemId;
    }

    private sealed class PartRenderData
    {
        public string Name { get; set; }
        public Mesh Mesh { get; set; }
        public Material[] Materials { get; set; }
    }

    public static ImportResult Import(
        string clientRoot,
        string dbRoot,
        SceneCharacterAppearanceRequest request,
        Action<string> log)
    {
        var appearanceBuilder = new SceneCharacterAppearanceBuilder();
        var appearance = appearanceBuilder.Build(clientRoot, request);
        var packageIndex = BuildSkeletalPackageIndex(clientRoot);
        var resolver = new SceneSkeletalMeshResolver();
        var skeletonSharedAsset = BuildCanonicalSharedAsset(
            clientRoot,
            resolver.ResolveAsset(appearance.SkeletonMeshLocation),
            packageIndex,
            appearance.Parts
                .SelectMany(x => x?.MeshResources ?? Array.Empty<SceneResourceReference>())
                .Where(x => x != null),
            log);
        var characterName = BuildCharacterName(appearance);
        var packageName = Path.GetFileNameWithoutExtension(appearance.SkeletonMeshLocation.PackagePath);
        var objectName = appearance.SkeletonMeshLocation.ObjectName ?? characterName;
        var referenceText = L2AssetManager.BuildReferenceText(packageName, objectName, characterName);
        var skeletalAssetRoot = L2AssetManager.BuildClientPackageObjectRoot(
            PlayerCharacterImportBuilder.AssetOutputRoot,
            packageName,
            objectName,
            "PlayerCharacters");

        L2AssetManager.EnsureFolderExists(PlayerCharacterImportBuilder.AssetOutputRoot);
        L2AssetManager.EnsureFolderExists(PlayerCharacterImportBuilder.PrefabOutputRoot);

        var baseAsset = L2SkeletalCharacterAssetFactory.Build(characterName, skeletonSharedAsset);
        var characterAssetPath = L2AssetManager.BuildAssetPathInFolder(
            skeletalAssetRoot,
            "PC",
            objectName,
            "asset",
            "skeleton");
        baseAsset = UnityAssetDatabaseUtility.CreateOrReplaceAsset(baseAsset, characterAssetPath);
        log?.Invoke($"[PlayerPreview] Base skeletal asset updated: {characterAssetPath}");

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

        var renderParts = BuildRenderParts(
            clientRoot,
            appearance,
            skeletonSharedAsset,
            packageIndex,
            buildContext,
            referenceText,
            log);

        var prefabPath = L2AssetManager.BuildClientPackageAssetPath(
            PlayerCharacterImportBuilder.PrefabOutputRoot,
            referenceText,
            "PF",
            "prefab",
            "PlayerCharacterPrefabs",
            "preview");
        CreatePreviewPrefab(baseAsset, renderParts, controller, prefabPath, characterName, appearance, log);

        AssetDatabase.SaveAssets();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab != null)
        {
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance != null)
            {
                instance.name = prefab.name;
                Selection.activeObject = instance;
                log?.Invoke("[PlayerPreview] Preview prefab instantiated into the current scene and selected.");
            }
        }

        return new ImportResult(prefabPath, characterAssetPath);
    }

    public static SceneCharacterAppearanceRequest BuildRequest(
        SceneCharacterBaseClass baseClass,
        SceneCharacterGender gender,
        int faceId,
        int hairStyleId,
        int hairColorId,
        SceneCharacterEquipmentCatalogItemData chestItem,
        SceneCharacterEquipmentCatalogItemData legsItem,
        SceneCharacterEquipmentCatalogItemData glovesItem,
        SceneCharacterEquipmentCatalogItemData feetItem)
    {
        var chest = new EquippedSlotSelection(chestItem);
        var legs = new EquippedSlotSelection(legsItem);
        var gloves = new EquippedSlotSelection(glovesItem);
        var feet = new EquippedSlotSelection(feetItem);

        var upperItemId = ResolveAppearanceItemId(SceneCharacterPaperdollSlot.Chest, chest, legs);
        var lowerItemId = ResolveAppearanceItemId(SceneCharacterPaperdollSlot.Legs, legs, chest);

        return new SceneCharacterAppearanceRequest
        {
            BaseClass = baseClass,
            Gender = gender,
            FaceId = faceId,
            HairStyleId = hairStyleId,
            HairColorId = hairColorId,
            UpperItemId = upperItemId,
            LowerItemId = lowerItemId,
            GlovesItemId = gloves.ItemId,
            BootsItemId = feet.ItemId
        };
    }

    private static int? ResolveAppearanceItemId(
        SceneCharacterPaperdollSlot slot,
        EquippedSlotSelection primary,
        EquippedSlotSelection secondary)
    {
        if (primary.HasValue && ItemAffectsSlot(primary.Item, slot))
        {
            return primary.ItemId;
        }

        if (secondary.HasValue && ItemAffectsSlot(secondary.Item, slot))
        {
            return secondary.ItemId;
        }

        return null;
    }

    private static bool ItemAffectsSlot(SceneCharacterEquipmentCatalogItemData item, SceneCharacterPaperdollSlot slot)
    {
        return item?.AppearanceSlots?.Contains(slot) == true;
    }

    private static string BuildCharacterName(SceneCharacterAppearanceData appearance)
    {
        return $"{appearance.Gender}_{appearance.BaseClass}_{appearance.VisualFamily}_F{appearance.FaceId}_H{appearance.HairStyleId}_{appearance.HairColorId}";
    }

    private static List<PartRenderData> BuildRenderParts(
        string clientRoot,
        SceneCharacterAppearanceData appearance,
        SceneSkeletalAsset baseSharedAsset,
        IReadOnlyDictionary<string, string> packageIndex,
        L2SkeletalAnimatorPrefabBuilder.BuildContext buildContext,
        string referenceText,
        Action<string> log)
    {
        var renderParts = new List<PartRenderData>();
        foreach (var part in appearance.Parts ?? Array.Empty<SceneCharacterResolvedPartData>())
        {
            if (part == null || part.MeshResources == null || part.MeshResources.Length == 0)
            {
                continue;
            }

            for (var i = 0; i < part.MeshResources.Length; i++)
            {
                var meshReference = part.MeshResources[i];
                if (meshReference == null)
                {
                    continue;
                }

                var location = ResolveMeshLocation(clientRoot, packageIndex, meshReference);
                var sharedAsset = BuildPartSharedAsset(location, baseSharedAsset, log);
                var partName = $"{CreatureSkeletalImportUtility.SanitizeName(part.Slot.ToString())}_{i:D2}_{meshReference.ObjectName}";
                var partAsset = L2SkeletalCharacterAssetFactory.Build(partName, sharedAsset);
                ApplyPartTextureOverrides(partAsset, part, i, buildContext);
                var derivedAssetRoot = BuildDerivedAssetRoot(location);
                var variantToken = BuildDerivedVariantToken(part.Slot.ToString(), i);

                var partAssetPath = L2AssetManager.BuildAssetPathInFolder(
                    $"{derivedAssetRoot}/Parts",
                    "PCP",
                    meshReference.ObjectName ?? partName,
                    "asset",
                    $"{variantToken}_part");
                partAsset = UnityAssetDatabaseUtility.CreateOrReplaceAsset(partAsset, partAssetPath);

                var materials = CreatureSkeletalMaterialImporter.CreateMaterials(
                    partAsset,
                    variantToken,
                    $"{derivedAssetRoot}/Materials",
                    log,
                    buildContext);
                var mesh = CreatureSkinnedMeshBuilder.Build(partAsset, materials, log, out _);
                var meshAssetPath = L2AssetManager.BuildAssetPathInFolder(
                    $"{derivedAssetRoot}/Meshes",
                    "SM",
                    meshReference.ObjectName ?? partName,
                    "asset",
                    $"{variantToken}_mesh");
                mesh = UnityAssetDatabaseUtility.CreateOrReplaceAsset(mesh, meshAssetPath);

                renderParts.Add(new PartRenderData
                {
                    Name = partName,
                    Mesh = mesh,
                    Materials = materials
                });
            }
        }

        return renderParts;
    }

    private static SceneSkeletalAsset BuildPartSharedAsset(
        SceneResourceLocation location,
        SceneSkeletalAsset baseSharedAsset,
        Action<string> log)
    {
        log?.Invoke(
            $"[PlayerPreview] Building mesh-only part '{location.Reference}' on top of base player skeleton/animations.");
        return PlayerCharacterPartAssetBuilder.BuildMeshOnlyAsset(location, baseSharedAsset);
    }

    private static string BuildDerivedAssetRoot(SceneResourceLocation location)
    {
        var packageName = Path.GetFileNameWithoutExtension(location?.PackagePath);
        var objectName = location?.ObjectName ?? "DerivedMesh";
        var objectRoot = L2AssetManager.BuildClientPackageObjectRoot(
            PlayerCharacterImportBuilder.AssetOutputRoot,
            packageName,
            objectName,
            "PlayerCharacterParts");
        var derivedRoot = $"{objectRoot}/Derived";
        L2AssetManager.EnsureFolderExists(derivedRoot);
        return derivedRoot;
    }

    private static string BuildDerivedVariantToken(string slotName, int meshPartIndex)
    {
        var slotToken = CreatureSkeletalImportUtility.SanitizeName(slotName ?? "Slot");
        return $"{slotToken}_{meshPartIndex:D2}";
    }

    private static void ApplyPartTextureOverrides(
        L2SkeletalCharacterAsset asset,
        SceneCharacterResolvedPartData part,
        int meshPartIndex,
        L2SkeletalAnimatorPrefabBuilder.BuildContext buildContext)
    {
        if (asset == null || part?.TextureResources == null || part.TextureResources.Length == 0)
        {
            return;
        }

        var textureResources = part.TextureResources
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Reference))
            .ToArray();
        if (textureResources.Length == 0)
        {
            return;
        }

        var overrideTexture = SelectResolvedTextureOverride(textureResources, meshPartIndex, buildContext);
        if (overrideTexture == null)
        {
            return;
        }

        asset.UsedTextures = new[]
        {
            new L2SkeletalTextureRefData
            {
                Reference = overrideTexture.Reference,
                ResolvedPackagePath = string.Empty
            }
        };
        asset.PrimaryTextureReference = overrideTexture.Reference;

        var bindings = asset.MaterialBindings ?? Array.Empty<L2SkeletalMaterialBindingData>();
        for (var i = 0; i < bindings.Length; i++)
        {
            var binding = bindings[i];
            binding.PackageName = overrideTexture.PackageName;
            binding.ObjectName = overrideTexture.ObjectName;
            binding.TextureReference = overrideTexture.Reference;
            binding.ResolvedPackagePath = string.Empty;
        }
    }

    private static SceneResourceReference SelectResolvedTextureOverride(
        IReadOnlyList<SceneResourceReference> textureResources,
        int meshPartIndex,
        L2SkeletalAnimatorPrefabBuilder.BuildContext buildContext)
    {
        if (textureResources == null || textureResources.Count == 0)
        {
            return null;
        }

        var filtered = textureResources
            .Where(x => x != null &&
                        !string.IsNullOrWhiteSpace(x.Reference) &&
                        !string.IsNullOrWhiteSpace(x.PackageName) &&
                        !string.IsNullOrWhiteSpace(x.ObjectName))
            .ToArray();
        if (filtered.Length == 0)
        {
            return null;
        }

        var preferredIndex = Mathf.Clamp(meshPartIndex, 0, filtered.Length - 1);
        if (TryResolveTextureReference(filtered[preferredIndex], buildContext))
        {
            return filtered[preferredIndex];
        }

        foreach (var texture in filtered)
        {
            if (TryResolveTextureReference(texture, buildContext))
            {
                return texture;
            }
        }

        return null;
    }

    private static bool TryResolveTextureReference(
        SceneResourceReference texture,
        L2SkeletalAnimatorPrefabBuilder.BuildContext buildContext)
    {
        if (texture == null ||
            buildContext == null ||
            string.IsNullOrWhiteSpace(texture.PackageName) ||
            string.IsNullOrWhiteSpace(texture.ObjectName))
        {
            return false;
        }

        return CreatureSkeletalMaterialImporter.TryResolveTextureOrMaterialReference(
            buildContext,
            texture.PackageName,
            texture.ObjectName);
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

    private static SceneSkeletalAsset BuildCanonicalSharedAsset(
        string clientRoot,
        SceneSkeletalAsset baseSharedAsset,
        IReadOnlyDictionary<string, string> packageIndex,
        IEnumerable<SceneResourceReference> meshReferences,
        Action<string> log)
    {
        var additionalSkeletons = new List<SceneSkeletalSkeleton>();
        var seenReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var meshReference in meshReferences ?? Array.Empty<SceneResourceReference>())
        {
            if (meshReference == null ||
                string.IsNullOrWhiteSpace(meshReference.Reference) ||
                !seenReferences.Add(meshReference.Reference))
            {
                continue;
            }

            var location = ResolveMeshLocation(clientRoot, packageIndex, meshReference);
            additionalSkeletons.Add(PlayerCharacterPartAssetBuilder.LoadMeshSkeleton(location));
        }

        var merged = PlayerCharacterSkeletonMergeUtility.BuildCanonicalAsset(baseSharedAsset, additionalSkeletons);
        if (merged.Skeleton.Bones.Count != baseSharedAsset.Skeleton.Bones.Count)
        {
            log?.Invoke(
                $"[PlayerPreview] Expanded canonical skeleton from {baseSharedAsset.Skeleton.Bones.Count} to {merged.Skeleton.Bones.Count} bones.");
        }

        return merged;
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

    private static void CreatePreviewPrefab(
        L2SkeletalCharacterAsset baseAsset,
        IReadOnlyList<PartRenderData> renderParts,
        AnimatorController controller,
        string prefabPath,
        string characterName,
        SceneCharacterAppearanceData appearance,
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

            foreach (var part in renderParts)
            {
                if (part?.Mesh == null)
                {
                    continue;
                }

                var geometry = new GameObject(part.Name);
                geometry.transform.SetParent(root.transform, false);
                geometry.transform.localPosition = Vector3.zero;
                geometry.transform.localRotation = Quaternion.identity;
                geometry.transform.localScale = Vector3.one;

                var renderer = geometry.AddComponent<SkinnedMeshRenderer>();
                renderer.sharedMesh = part.Mesh;
                renderer.sharedMaterials = part.Materials ?? Array.Empty<Material>();
                renderer.rootBone = rootBone;
                renderer.bones = boneTransforms;
                renderer.updateWhenOffscreen = true;
                renderer.localBounds = part.Mesh.bounds;
            }

            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            root.AddComponent<L2AnimationNotifyReceiver>();

            var metadata = root.AddComponent<L2PlayerCharacterDebugMetadata>();
            metadata.BaseClass = appearance.BaseClass.ToString();
            metadata.Gender = appearance.Gender.ToString();
            metadata.VisualFamily = appearance.VisualFamily.ToString();
            metadata.SkeletonReference = appearance.SkeletonMeshLocation.Reference;
            metadata.SkeletonUri = appearance.SkeletonMeshLocation.Uri;

            var labelMesh = renderParts.FirstOrDefault(x => x?.Mesh != null)?.Mesh;
            CreatureSkeletalImportUtility.CreateLabel(root.transform, labelMesh, characterName);

            L2AssetManager.EnsureParentFolderExists(prefabPath);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            log?.Invoke($"[PlayerPreview] Preview prefab updated: {prefabPath}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }
}
