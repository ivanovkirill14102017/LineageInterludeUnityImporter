using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using UnityEditor;
using UnityEngine;

internal static class PlayerCharacterPrefabResolver
{
    private static readonly string[] SlotOrder =
    {
        "Face", "Hair", "Chest", "Legs", "Gloves", "Feet", "RightHand", "LeftHand", "LeftRightHand"
    };

    public static void Resolve(
        PlayerCharacterImportPlan plan,
        PlayerCharacterMeshAssetSet assets,
        RuntimeAnimatorController controller,
        string characterName,
        string archetypeAssetPath,
        string prefabPath,
        PlayerCharacterImportProgress progress)
    {
        var builtByVariant = assets.Parts
            .GroupBy(x => x.Source.Variant)
            .ToDictionary(x => x.Key, x => x.OrderBy(part => part.Source.PartIndex).ToArray());
        var slots = new List<L2CharacterSlotCatalogData>(SlotOrder.Length);
        foreach (var slotName in SlotOrder)
        {
            var variants = plan.Variants
                .Where(x => string.Equals(x.SlotName, slotName, StringComparison.Ordinal))
                .Select(variant => new L2CharacterVariantData
                {
                    VariantKey = variant.Key,
                    DisplayName = variant.DisplayName,
                    VariantId = variant.Id,
                    AuxVariantId = variant.AuxiliaryId,
                    Parts = builtByVariant[variant]
                        .Select(part => new L2CharacterVariantPartData
                        {
                            Name = $"{variant.SlotName}_{variant.Key}_{part.Source.PartIndex:D2}",
                            Mesh = part.Mesh,
                            Materials = part.Materials,
                            BoneNames = part.BoneNames,
                            BoneParentIndices = part.BoneParentIndices,
                            UsesOwnSkeleton = PlayerCharacterImportPlanBuilder.IsWeaponSlotName(variant.SlotName)
                        })
                        .ToArray()
                })
                .ToArray();
            slots.Add(new L2CharacterSlotCatalogData
            {
                SlotName = slotName,
                DefaultVariantIndex = slotName == "Face"
                    ? Array.FindIndex(variants, x => x.VariantId == plan.Appearance.FaceId) is var index && index >= 0 ? index : 0
                    : 0,
                Variants = variants
            });
        }

        progress.Report("Prefab", characterName, 0.90f);
        var archetype = ScriptableObject.CreateInstance<L2PlayerCharacterArchetypeAsset>();
        archetype.ArchetypeName = characterName;
        archetype.BaseClass = plan.Appearance.BaseClass.ToString();
        archetype.Gender = plan.Appearance.Gender.ToString();
        archetype.VisualFamily = plan.Appearance.VisualFamily.ToString();
        archetype.BaseAsset = assets.BaseAsset;
        archetype.AnimatorController = controller;
        archetype.Slots = slots.ToArray();
        archetype = UnityAssetDatabaseUtility.CreateOrReplaceAsset(archetype, archetypeAssetPath);

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
                metadata.SkeletonReference = assets.BaseAsset.MeshObjectName;
                metadata.SkeletonUri = assets.BaseAsset.SourcePackagePath;

                var wardrobe = build.Root.AddComponent<L2PlayerCharacterWardrobe>();
                wardrobe.Archetype = archetype;
                wardrobe.SkeletonRoot = build.SkeletonRoot;
                wardrobe.RootBone = build.RootBone;
                wardrobe.Bones = build.Bones;
                wardrobe.SlotBindings = build.SlotBindings;
                wardrobe.Animator = build.Animator;
                wardrobe.ApplyAppearance();
                wardrobe.ApplyAnimation();
            },
            replaceExisting: true);
    }
}
