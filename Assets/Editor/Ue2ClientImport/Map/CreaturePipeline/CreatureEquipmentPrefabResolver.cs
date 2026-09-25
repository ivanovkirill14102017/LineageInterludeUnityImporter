using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services.Utility;
using UnityEditor;
using UnityEngine;

internal static class CreatureEquipmentPrefabResolver
{
    private sealed class EquipmentPartBuild
    {
        public EquipmentPartBuild(L2CharacterVariantPartData part, L2SkeletalCharacterAsset asset)
        {
            Part = part;
            Asset = asset;
        }

        public L2CharacterVariantPartData Part { get; }
        public L2SkeletalCharacterAsset Asset { get; }
    }

    public static void Resolve(
        CreatureImportPlan plan,
        CreatureVisualSourceSet visualSources,
        EquipmentSourceResourceSet equipmentSources,
        string clientRoot,
        IReadOnlyDictionary<string, string> packageIndex,
        CreatureImportProgress progress)
    {
        var items = equipmentSources.Items.ToDictionary(x => x.ItemId);
        for (var index = 0; index < plan.Compositions.Length; index++)
        {
            var composition = plan.Compositions[index];
            progress.ReportItem("Equipment prefabs", composition.Key, index, plan.Compositions.Length, 0.80f, 0.85f);
            var visual = visualSources.Visuals.Single(x => x.Plan.Key == composition.Visual.Key);
            ResolveHand(
                composition,
                visual,
                items,
                SceneCharacterPaperdollSlot.RightHand,
                composition.Source.RightHandItemId,
                clientRoot,
                packageIndex);
            ResolveHand(
                composition,
                visual,
                items,
                SceneCharacterPaperdollSlot.LeftHand,
                composition.Source.LeftHandItemId,
                clientRoot,
                packageIndex);
        }

        AssetDatabase.SaveAssets();
    }

    public static string BuildPrefabPath(
        SceneCharacterPaperdollSlot slot,
        int itemId)
    {
        var root = $"{L2AssetManager.ManagedCreaturePrefabsRoot}/Equipment";
        L2AssetManager.EnsureFolderExists(root);
        return L2AssetManager.BuildAssetPathInFolder(
            root,
            "PF",
            $"Item_{itemId}_{slot}",
            "prefab");
    }

    private static void ResolveHand(
        CreatureCompositionImportPlan composition,
        CreatureVisualSource visual,
        IReadOnlyDictionary<int, SceneCharacterEquipmentCatalogItemData> items,
        SceneCharacterPaperdollSlot requestedSlot,
        int itemId,
        string clientRoot,
        IReadOnlyDictionary<string, string> packageIndex)
    {
        if (itemId <= 0)
        {
            return;
        }

        var item = items[itemId];
        var slot = ResolveSlot(requestedSlot, item);
        var prefabPath = BuildPrefabPath(slot, itemId);
        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null)
        {
            return;
        }

        var textureReferences = item.TextureResources
            .Select(x => new SceneSurfaceResourceReference(x.ResourceId))
            .ToArray();
        var partBuilds = item.MeshResources
            .Select((meshReference, index) => BuildPart(
                visual,
                item,
                slot,
                meshReference,
                textureReferences,
                index,
                clientRoot,
                packageIndex))
            .ToArray();
        var variant = new L2CharacterVariantData
        {
            VariantKey = $"{slot.ToString().ToLowerInvariant()}_{itemId}",
            DisplayName = $"{itemId} {item.DisplayName}",
            VariantId = itemId,
            AuxVariantId = -1,
            Parts = partBuilds.Select(x => x.Part).ToArray()
        };
        var root = new GameObject($"Equipment_{itemId}_{slot}");
        var component = root.AddComponent<L2EquipmentVisual>();
        component.ItemId = itemId;
        component.SlotName = slot.ToString();
        component.Variant = variant;
        BuildVisuals(root, partBuilds);
        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        UnityEngine.Object.DestroyImmediate(root);
    }

    private static EquipmentPartBuild BuildPart(
        CreatureVisualSource visual,
        SceneCharacterEquipmentCatalogItemData item,
        SceneCharacterPaperdollSlot slot,
        SceneResourceReference meshReference,
        SceneSurfaceResourceReference[] textureReferences,
        int partIndex,
        string clientRoot,
        IReadOnlyDictionary<string, string> packageIndex)
    {
        var packagePath = packageIndex[meshReference.PackageName];
        var location = SceneReferenceUtilities.BuildResourceLocation(
            clientRoot,
            packagePath,
            meshReference.PackageName,
            meshReference.ObjectName,
            meshReference.ClassName);
        var sharedPart = PlayerCharacterPartAssetBuilder.BuildMeshOnlyAsset(location, visual.SkeletalAsset);
        var characterAsset = L2SkeletalCharacterAssetFactory.Build(
            $"equipment_{item.ItemId}_{partIndex}",
            sharedPart);
        var materialIds = CreatureSkeletalImportUtility.GetMaterialIds(characterAsset);
        var materials = materialIds
            .Select((_, index) => AssetDatabase.LoadAssetAtPath<Material>(
                SharedMaterialResourceResolver.BuildAssetPath(
                    MaterialResourceRequest.Opaque(textureReferences[index]).Id))!)
            .ToArray();
        var meshPath = L2AssetManager.BuildClientPackageAssetPath(
            L2AssetManager.SharedSkeletalMeshesRoot,
            meshReference.Reference,
            "SM",
            "asset",
            "SkeletalMeshes");
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
        if (mesh == null)
        {
            mesh = CreatureSkinnedMeshBuilder.Build(characterAsset, materials, null, out _);
            mesh = UnityAssetDatabaseUtility.CreateAssetIfMissing(mesh, meshPath);
        }

        return new EquipmentPartBuild(
            new L2CharacterVariantPartData
            {
                Name = $"{slot}_{item.ItemId}_{partIndex:D2}",
                Mesh = mesh,
                Materials = materials,
                BoneNames = characterAsset.Bones.Select(x => x.Name).ToArray(),
                BoneParentIndices = characterAsset.Bones.Select(x => x.ParentIndex).ToArray()
            },
            characterAsset);
    }

    private static void BuildVisuals(GameObject root, IReadOnlyList<EquipmentPartBuild> parts)
    {
        foreach (var build in parts)
        {
            var partRoot = new GameObject(build.Part.Name);
            partRoot.transform.SetParent(root.transform, false);

            var skeletonRoot = new GameObject("Skeleton").transform;
            skeletonRoot.SetParent(partRoot.transform, false);
            var session = L2SceneSkeletalAssetBridge.CreateSession(build.Asset);
            var bindFrame = session.CaptureBindPoseDebugFrame();
            var bonePoses = CreatureSkeletalImportUtility.BuildBonePoses(bindFrame.Bones, build.Asset.Bones);
            var bones = CreatureSkeletalImportUtility.CreateBoneHierarchy(
                build.Asset.Bones,
                bonePoses,
                skeletonRoot);

            var geometry = new GameObject("Geometry");
            geometry.transform.SetParent(partRoot.transform, false);
            var renderer = geometry.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = build.Part.Mesh;
            renderer.sharedMaterials = build.Part.Materials;
            renderer.rootBone = CreatureSkeletalImportUtility.ResolveRootBone(build.Asset.Bones, bones);
            renderer.bones = bones;
            renderer.updateWhenOffscreen = true;
            renderer.localBounds = build.Part.Mesh.bounds;
        }
    }

    public static SceneCharacterPaperdollSlot ResolveSlot(
        SceneCharacterPaperdollSlot requestedSlot,
        SceneCharacterEquipmentCatalogItemData item)
    {
        if (item.PaperdollSlots.Contains(SceneCharacterPaperdollSlot.LeftRightHand))
        {
            return SceneCharacterPaperdollSlot.LeftRightHand;
        }

        if (item.PaperdollSlots.Contains(SceneCharacterPaperdollSlot.LeftHand))
        {
            return SceneCharacterPaperdollSlot.LeftHand;
        }

        if (item.PaperdollSlots.Contains(SceneCharacterPaperdollSlot.RightHand))
        {
            return SceneCharacterPaperdollSlot.RightHand;
        }

        return requestedSlot;
    }
}
