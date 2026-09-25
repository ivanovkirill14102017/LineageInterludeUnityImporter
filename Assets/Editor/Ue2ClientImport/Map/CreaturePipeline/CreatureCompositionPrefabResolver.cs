using System.Collections.Generic;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using UnityEditor;
using UnityEngine;

internal static class CreatureCompositionPrefabResolver
{
    public static void Resolve(
        CreatureImportPlan plan,
        EquipmentSourceResourceSet equipmentSources,
        string clientRoot,
        CreatureImportProgress progress)
    {
        var items = equipmentSources.Items.ToDictionary(x => x.ItemId);
        for (var index = 0; index < plan.Compositions.Length; index++)
        {
            var composition = plan.Compositions[index];
            progress.ReportItem("Creature prefabs", composition.Key, index, plan.Compositions.Length, 0.85f, 0.92f);
            var basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CreatureBaseVisualPrefabResolver.BuildPrefabPath(composition.Visual));
            var baseArchetype = basePrefab
                .GetComponentInChildren<L2CreatureWardrobe>(true)
                .Archetype;
            var equipment = BuildEquipmentDecorations(composition, items);

            var archetype = ScriptableObject.CreateInstance<L2CreatureCharacterArchetypeAsset>();
            archetype.ArchetypeName = composition.Key;
            archetype.DisplayName = composition.Source.DisplayName;
            archetype.SkeletonReference = baseArchetype.SkeletonReference;
            archetype.SkeletonUri = baseArchetype.SkeletonUri;
            archetype.BaseAsset = baseArchetype.BaseAsset;
            archetype.AnimatorController = baseArchetype.AnimatorController;
            archetype.Slots = baseArchetype.Slots;
            archetype = UnityAssetDatabaseUtility.CreateOrReplaceAsset(
                archetype,
                BuildArchetypePath(composition));

            var effects = composition.Source.AttachedEffects.Length == 0
                ? null
                : new CreatureSkeletalPrefabFactory.EffectDecoration(
                    composition.Source.AttachedEffects,
                    clientRoot,
                    L2AssetManager.SharedSkeletalCharactersRoot,
                    composition.Visual.MeshReference.Id.ObjectPath,
                    null);
            CreatureSkeletalPrefabFactory.Create(
                archetype,
                BuildPrefabPath(composition),
                composition.Source.DisplayName,
                effects,
                equipment);
        }

        AssetDatabase.SaveAssets();
    }

    public static string BuildPrefabPath(CreatureCompositionImportPlan composition)
    {
        return L2AssetManager.BuildClientPackageAssetPath(
            L2AssetManager.ManagedCreaturePrefabsRoot,
            composition.Visual.Source.ActorClassResource.Reference,
            "PF",
            "prefab",
            "CreatureCompositions",
            $"npc_{composition.Source.TemplateId}");
    }

    private static string BuildArchetypePath(CreatureCompositionImportPlan composition)
    {
        return L2AssetManager.BuildClientPackageAssetPath(
            L2AssetManager.ManagedCreaturePrefabsRoot,
            composition.Visual.Source.ActorClassResource.Reference,
            "NCA",
            "asset",
            "CreatureCompositions",
            $"npc_{composition.Source.TemplateId}");
    }

    private static CreatureSkeletalPrefabFactory.EquipmentDecoration[] BuildEquipmentDecorations(
        CreatureCompositionImportPlan composition,
        IReadOnlyDictionary<int, SceneCharacterEquipmentCatalogItemData> items)
    {
        var result = new List<CreatureSkeletalPrefabFactory.EquipmentDecoration>();
        AddEquipment(
            result,
            items,
            SceneCharacterPaperdollSlot.RightHand,
            composition.Source.RightHandItemId);
        AddEquipment(
            result,
            items,
            SceneCharacterPaperdollSlot.LeftHand,
            composition.Source.LeftHandItemId);
        return result.ToArray();
    }

    private static void AddEquipment(
        ICollection<CreatureSkeletalPrefabFactory.EquipmentDecoration> result,
        IReadOnlyDictionary<int, SceneCharacterEquipmentCatalogItemData> items,
        SceneCharacterPaperdollSlot requestedSlot,
        int itemId)
    {
        if (itemId <= 0)
        {
            return;
        }

        var slot = CreatureEquipmentPrefabResolver.ResolveSlot(requestedSlot, items[itemId]);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
            CreatureEquipmentPrefabResolver.BuildPrefabPath(slot, itemId));
        result.Add(new CreatureSkeletalPrefabFactory.EquipmentDecoration(slot.ToString(), prefab));
    }
}