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
                    WeaponAnimationClass = ResolveWeaponAnimationClass(variant.Item),
                    RawWeaponType = variant.Item?.RawWeaponType ?? 0,
                    RawHandness = variant.Item?.RawHandness ?? 0,
                    Parts = builtByVariant[variant]
                        .Select(part => new L2CharacterVariantPartData
                        {
                            Name = $"{variant.SlotName}_{variant.Key}_{part.Source.PartIndex:D2}",
                            Mesh = part.Mesh,
                            Materials = part.Materials,
                            BoneNames = part.BoneNames,
                            BoneParentIndices = part.BoneParentIndices,
                            RootAttachmentBoneName = PlayerCharacterHairBinding.ResolveRootAttachment(
                                slotName, part.BoneNames, part.BoneParentIndices),
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
                var hairRoots = slots.First(x => x.SlotName == "Hair").Variants
                    .SelectMany(x => x.Parts ?? Array.Empty<L2CharacterVariantPartData>())
                    .Where(x => x.RootAttachmentBoneName != null)
                    .Select(x => x.BoneNames[0]);
                PlayerCharacterHairBinding.AttachRootsToHead(build.SkeletonRoot, build.Bones, hairRoots);

                var metadata = build.Root.AddComponent<L2PlayerCharacterDebugMetadata>();
                metadata.BaseClass = archetype.BaseClass;
                metadata.Gender = archetype.Gender;
                metadata.VisualFamily = archetype.VisualFamily;
                metadata.SkeletonReference = assets.BaseAsset.MeshObjectName;
                metadata.SkeletonUri = assets.BaseAsset.SourcePackagePath;

                var appearance = build.Root.AddComponent<L2PlayerAppearanceVisual>();
                appearance.Archetype = archetype;
                appearance.SkeletonRoot = build.SkeletonRoot;
                appearance.RootBone = build.RootBone;
                appearance.Bones = build.Bones;
                appearance.SlotBindings = build.SlotBindings;
                appearance.SelectedFaceIndex = slots.First(x => x.SlotName == "Face").DefaultVariantIndex;
                appearance.ApplyAppearance();

                var equipment = build.Root.AddComponent<L2PlayerEquipmentVisual>();
                equipment.Archetype = archetype;
                equipment.SkeletonRoot = build.SkeletonRoot;
                equipment.RootBone = build.RootBone;
                equipment.Bones = build.Bones;
                equipment.SlotBindings = build.SlotBindings;
                equipment.ApplyEquipment();

                var animation = build.Root.AddComponent<L2PlayerAnimationStateController>();
                animation.Archetype = archetype;
                animation.Equipment = equipment;
                animation.Animator = build.Animator;
                animation.Refresh();
            },
            replaceExisting: true);
    }

    private static L2WeaponAnimationClass ResolveWeaponAnimationClass(SceneCharacterEquipmentCatalogItemData item)
    {
        return item?.AnimationClass switch
        {
            null or SceneWeaponAnimationClass.None => L2WeaponAnimationClass.None,
            SceneWeaponAnimationClass.Hand => L2WeaponAnimationClass.Hand,
            SceneWeaponAnimationClass.OneHanded => L2WeaponAnimationClass.OneHanded,
            SceneWeaponAnimationClass.TwoHanded => L2WeaponAnimationClass.TwoHanded,
            SceneWeaponAnimationClass.Bow => L2WeaponAnimationClass.Bow,
            SceneWeaponAnimationClass.Dual => L2WeaponAnimationClass.Dual,
            SceneWeaponAnimationClass.Pole => L2WeaponAnimationClass.Pole,
            SceneWeaponAnimationClass.Fishing => L2WeaponAnimationClass.Fishing,
            _ => throw new ArgumentOutOfRangeException(nameof(item.AnimationClass), item.AnimationClass, null)
        };
    }
}
