using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services.Utility;
using UnityEngine;

internal static class PlayerCharacterSlotCatalogBuilder
{
    internal sealed class BuildArgs
    {
        public string ClientRoot;
        public SceneCharacterAppearanceData BaseAppearance;
        public SceneCharacterAppearanceOptionsData AppearanceOptions;
        public SceneCharacterEquipmentCatalogData EquipmentCatalog;
        public SceneSkeletalAsset BaseSharedAsset;
        public IReadOnlyDictionary<string, string> PackageIndex;
        public L2SkeletalAnimatorPrefabBuilder.BuildContext BuildContext;
        public Dictionary<string, SceneSkeletalAsset> PartAssetCache;
        public Dictionary<string, Mesh> MeshAssetCache;
        public MapImportExecutionContext Context;
        public string AssetOutputRoot;
        public Action<string> Log;
    }

    public static L2CharacterSlotCatalogData[] Build(BuildArgs args)
    {
        if (args == null)
        {
            throw new ArgumentNullException(nameof(args));
        }

        var totalVariantCount = EstimateVariantCount(args.BaseAppearance, args.AppearanceOptions, args.EquipmentCatalog);
        var progress = new VariantBuildProgress(totalVariantCount);

        return new[]
        {
            BuildFaceSlot(args, progress),
            BuildHairSlot(args, progress),
            BuildEquipmentSlot(SceneCharacterPaperdollSlot.Chest, args, progress),
            BuildEquipmentSlot(SceneCharacterPaperdollSlot.Legs, args, progress),
            BuildEquipmentSlot(SceneCharacterPaperdollSlot.Gloves, args, progress),
            BuildEquipmentSlot(SceneCharacterPaperdollSlot.Feet, args, progress),
            BuildEquipmentSlot(SceneCharacterPaperdollSlot.RightHand, args, progress),
            BuildEquipmentSlot(SceneCharacterPaperdollSlot.LeftHand, args, progress),
            BuildEquipmentSlot(SceneCharacterPaperdollSlot.LeftRightHand, args, progress)
        };
    }

    private static L2CharacterSlotCatalogData BuildFaceSlot(BuildArgs args, VariantBuildProgress progress)
    {
        var variants = new List<L2CharacterVariantData>();
        foreach (var face in args.AppearanceOptions?.FaceOptions ?? Array.Empty<SceneCharacterFaceOptionData>())
        {
            var variant = BuildVariant(
                "Face",
                $"Face {face.Id}",
                $"face_{face.Id:D2}",
                face.Id,
                -1,
                face.MeshResources,
                face.TextureResources,
                args,
                progress);
            if (variant != null)
            {
                variants.Add(variant);
            }
        }

        return new L2CharacterSlotCatalogData
        {
            SlotName = "Face",
            DefaultVariantIndex = ResolveDefaultFaceIndex(args.BaseAppearance?.FaceId ?? 0, variants),
            Variants = variants.ToArray()
        };
    }

    private static L2CharacterSlotCatalogData BuildHairSlot(BuildArgs args, VariantBuildProgress progress)
    {
        var variants = new List<L2CharacterVariantData>();
        foreach (var hairStyle in args.AppearanceOptions?.HairStyleOptions ?? Array.Empty<SceneCharacterHairStyleOptionData>())
        {
            foreach (var hairColor in hairStyle.HairColorOptions ?? Array.Empty<SceneCharacterHairColorOptionData>())
            {
                var variant = BuildVariant(
                    "Hair",
                    $"Hair {hairStyle.Id} / Color {hairColor.Id}",
                    $"hair_{hairStyle.Id:D2}_{hairColor.Id:D2}",
                    hairStyle.Id,
                    hairColor.Id,
                    hairStyle.MeshResources,
                    hairColor.TextureResources,
                    args,
                    progress);
                if (variant != null)
                {
                    variants.Add(variant);
                }
            }
        }

        return new L2CharacterSlotCatalogData
        {
            SlotName = "Hair",
            DefaultVariantIndex = 0,
            Variants = variants.ToArray()
        };
    }

    private static L2CharacterSlotCatalogData BuildEquipmentSlot(
        SceneCharacterPaperdollSlot slot,
        BuildArgs args,
        VariantBuildProgress progress)
    {
        var variants = new List<L2CharacterVariantData>();
        var basePart = args.BaseAppearance?.Parts?.FirstOrDefault(x => x != null && x.Slot == slot);
        if (basePart != null && !IsWeaponSlot(slot))
        {
            var baseVariant = BuildVariant(
                slot.ToString(),
                "<Base>",
                $"{slot.ToString().ToLowerInvariant()}_base",
                -1,
                -1,
                basePart.MeshResources,
                basePart.TextureResources,
                args,
                progress);
            if (baseVariant != null)
            {
                variants.Add(baseVariant);
            }
        }

        var items = args.EquipmentCatalog?.Slots?
            .FirstOrDefault(x => x.Slot == slot)?
            .Items?
            .Where(x => CanBuildEquipmentItem(slot, x))
            .ToArray() ?? Array.Empty<SceneCharacterEquipmentCatalogItemData>();
        foreach (var item in items)
        {
            var variant = BuildVariant(
                slot.ToString(),
                $"{item.ItemId} {item.DisplayName}",
                $"{slot.ToString().ToLowerInvariant()}_{item.ItemId}",
                item.ItemId,
                -1,
                item.MeshResources,
                item.TextureResources,
                args,
                progress);
            if (variant != null)
            {
                variants.Add(variant);
            }
        }

        return new L2CharacterSlotCatalogData
        {
            SlotName = slot.ToString(),
            DefaultVariantIndex = 0,
            Variants = variants.ToArray()
        };
    }

    internal static L2CharacterVariantData BuildVariant(
        string slotName,
        string displayName,
        string variantKey,
        int variantId,
        int auxVariantId,
        SceneResourceReference[] meshResources,
        SceneResourceReference[] textureResources,
        BuildArgs args,
        VariantBuildProgress progress)
    {
        progress?.Report(args.Context, slotName, displayName);
        var parts = new List<L2CharacterVariantPartData>();
        var textures = textureResources?.Where(x => x != null && !string.IsNullOrWhiteSpace(x.Reference)).ToArray()
                       ?? Array.Empty<SceneResourceReference>();
        var meshes = meshResources?.Where(x => x != null).ToArray() ?? Array.Empty<SceneResourceReference>();

        for (var i = 0; i < meshes.Length; i++)
        {
            args.Context?.ThrowIfCancellationRequested();
            var meshReference = meshes[i];
            SceneResourceLocation location;
            SceneSkeletalAsset sharedAsset;
            try
            {
                location = ResolveMeshLocation(args.ClientRoot, args.PackageIndex, meshReference);
                sharedAsset = ResolveOrBuildPartAsset(location, args.BaseSharedAsset, args.PartAssetCache);
            }
            catch (Exception ex) when (IsWeaponSlotName(slotName))
            {
                args.Log?.Invoke(
                    $"[PlayerArchetype] Skipped weapon mesh '{meshReference.Reference}' for {displayName}: {ex.Message}");
                continue;
            }

            var tempAsset = L2SkeletalCharacterAssetFactory.Build(
                $"{slotName}_{variantKey}_{i:D2}",
                sharedAsset);
            ApplyPartTextureOverrides(tempAsset, textures, i, args.BuildContext);

            var derivedAssetRoot = BuildDerivedAssetRoot(location, args.AssetOutputRoot);
            var variantToken = BuildDerivedVariantToken(slotName, variantKey, i);
            var materials = CreatureSkeletalMaterialImporter.CreateMaterials(
                tempAsset,
                variantToken,
                $"{derivedAssetRoot}/Materials",
                args.Log,
                args.BuildContext);
            var mesh = ResolveOrBuildMeshAsset(tempAsset, materials, derivedAssetRoot, variantToken, args.MeshAssetCache, args.Log);

            parts.Add(new L2CharacterVariantPartData
            {
                Name = $"{slotName}_{variantKey}_{i:D2}",
                Mesh = mesh,
                Materials = materials
            });
        }

        if (parts.Count == 0 && IsWeaponSlotName(slotName))
        {
            args.Log?.Invoke($"[PlayerArchetype] Skipped weapon variant '{displayName}' because none of its meshes were resolved.");
            return null;
        }

        return new L2CharacterVariantData
        {
            VariantKey = variantKey,
            DisplayName = displayName,
            VariantId = variantId,
            AuxVariantId = auxVariantId,
            Parts = parts.ToArray()
        };
    }

    private static int ResolveDefaultFaceIndex(int defaultFaceId, IReadOnlyList<L2CharacterVariantData> variants)
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
        string derivedAssetRoot,
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
        var meshAssetPath = L2AssetManager.BuildAssetPathInFolder(
            $"{derivedAssetRoot}/Meshes",
            "SM",
            tempAsset.MeshObjectName ?? tempAsset.CharacterName ?? "Mesh",
            "asset",
            $"{materialPrefix}_mesh");
        mesh = UnityAssetDatabaseUtility.CreateAssetIfMissing(mesh, meshAssetPath);
        cache[meshKey] = mesh;
        return mesh;
    }

    private static string BuildDerivedAssetRoot(SceneResourceLocation location, string assetOutputRoot)
    {
        var packageName = Path.GetFileNameWithoutExtension(location?.PackagePath);
        var objectName = location?.ObjectName ?? "DerivedMesh";
        var objectRoot = L2AssetManager.BuildClientPackageObjectRoot(
            string.IsNullOrWhiteSpace(assetOutputRoot) ? PlayerCharacterImportBuilder.AssetOutputRoot : assetOutputRoot,
            packageName,
            objectName,
            "PlayerCharacterParts");
        var derivedRoot = $"{objectRoot}/Derived";
        L2AssetManager.EnsureFolderExists(derivedRoot);
        return derivedRoot;
    }

    private static string BuildDerivedVariantToken(string slotName, string variantKey, int meshPartIndex)
    {
        var slotToken = CreatureSkeletalImportUtility.SanitizeName(slotName ?? "Slot");
        var variantToken = CreatureSkeletalImportUtility.SanitizeName(variantKey ?? "Variant");
        return $"{slotToken}_{variantToken}_{meshPartIndex:D2}";
    }

    private static void ApplyPartTextureOverrides(
        L2SkeletalCharacterAsset asset,
        SceneResourceReference[] textureResources,
        int meshPartIndex,
        L2SkeletalAnimatorPrefabBuilder.BuildContext buildContext)
    {
        if (asset == null || textureResources == null || textureResources.Length == 0)
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
            bindings[i].PackageName = overrideTexture.PackageName;
            bindings[i].ObjectName = overrideTexture.ObjectName;
            bindings[i].TextureReference = overrideTexture.Reference;
            bindings[i].ResolvedPackagePath = string.Empty;
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
        count += CountEquipmentVariants(SceneCharacterPaperdollSlot.RightHand, baseAppearance, equipmentCatalog);
        count += CountEquipmentVariants(SceneCharacterPaperdollSlot.LeftHand, baseAppearance, equipmentCatalog);
        count += CountEquipmentVariants(SceneCharacterPaperdollSlot.LeftRightHand, baseAppearance, equipmentCatalog);
        return Math.Max(1, count);
    }

    private static int CountEquipmentVariants(
        SceneCharacterPaperdollSlot slot,
        SceneCharacterAppearanceData baseAppearance,
        SceneCharacterEquipmentCatalogData equipmentCatalog)
    {
        var count = !IsWeaponSlot(slot) && baseAppearance?.Parts?.Any(x => x != null && x.Slot == slot) == true ? 1 : 0;
        count += equipmentCatalog?.Slots?
            .FirstOrDefault(x => x.Slot == slot)?
            .Items?
            .Count(x => CanBuildEquipmentItem(slot, x)) ?? 0;
        return count;
    }

    internal static bool CanBuildEquipmentItem(SceneCharacterPaperdollSlot slot, SceneCharacterEquipmentCatalogItemData item)
    {
        if (item == null)
        {
            return false;
        }

        if (item.IsRenderableWithCurrentAppearanceBuilder)
        {
            return true;
        }

        return IsWeaponSlot(slot) &&
               (item.MeshResources ?? Array.Empty<SceneResourceReference>())
               .Any(x => x != null && !string.IsNullOrWhiteSpace(x.Reference));
    }

    internal static bool IsWeaponSlot(SceneCharacterPaperdollSlot slot)
    {
        return slot == SceneCharacterPaperdollSlot.RightHand ||
               slot == SceneCharacterPaperdollSlot.LeftHand ||
               slot == SceneCharacterPaperdollSlot.LeftRightHand;
    }

    internal static bool IsWeaponSlotName(string slotName)
    {
        return string.Equals(slotName, SceneCharacterPaperdollSlot.RightHand.ToString(), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(slotName, SceneCharacterPaperdollSlot.LeftHand.ToString(), StringComparison.OrdinalIgnoreCase) ||
               string.Equals(slotName, SceneCharacterPaperdollSlot.LeftRightHand.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    internal sealed class VariantBuildProgress
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
}
