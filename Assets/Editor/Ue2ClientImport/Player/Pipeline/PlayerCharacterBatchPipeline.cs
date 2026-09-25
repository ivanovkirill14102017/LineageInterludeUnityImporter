using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services.CharacterServices;
using L2Viewer.SceneDomain.Services.MaterialServices;
using UnityEditor;
using UnityEngine;

internal static class PlayerCharacterBatchPipeline
{
    public static PlayerCharacterArchetypeBuilder.ImportResult Import(
        string clientRoot,
        string dbRoot,
        SceneCharacterBaseClass baseClass,
        SceneCharacterGender gender,
        SceneCharacterAppearanceOptionsData options,
        SceneCharacterEquipmentCatalogData equipment,
        MapImportExecutionContext context)
    {
        var progress = new PlayerCharacterImportProgress(context);
        var appearance = new SceneCharacterAppearanceBuilder().Build(
            clientRoot,
            new SceneCharacterAppearanceRequest { BaseClass = baseClass, Gender = gender });
        options ??= new SceneCharacterAppearanceOptionsBuilder().Build(clientRoot, baseClass, gender);
        equipment ??= new SceneCharacterEquipmentCatalogBuilder().Build(clientRoot, dbRoot, baseClass, gender);
        var plan = PlayerCharacterImportPlanBuilder.Build(appearance, options, equipment);
        var sources = PlayerCharacterSourceResolver.Resolve(plan, clientRoot, progress);

        var surfaceReferences = sources.Parts
            .SelectMany(x => x.Surfaces)
            .Concat(sources.BaseAsset.SurfaceResourceReferences)
            .Distinct()
            .ToArray();
        var surfaceResolver = new SceneSurfaceSourceResolver(clientRoot);
        var surfaceSources = new List<SceneSurfaceSourceResource>(surfaceReferences.Length);
        var materialRequests = new MaterialResourceRequest[surfaceReferences.Length];
        for (var index = 0; index < surfaceReferences.Length; index++)
        {
            progress.ReportItem("Surfaces", surfaceReferences[index], index, surfaceReferences.Length, 0.15f, 0.26f);
            var surface = surfaceReferences[index];
            if (surfaceResolver.TryResolve(surface, out var source))
            {
                surfaceSources.Add(source);
                materialRequests[index] = MaterialResourceRequest.Opaque(surface)
                    .WithTextureReference(source.TextureReference);
                continue;
            }

            materialRequests[index] = MaterialResourceRequest.MissingTexture(surface);
        }

        SharedTextureResourceResolver.Resolve(surfaceSources, progress);
        SharedMaterialResourceResolver.Resolve(materialRequests, progress);

        var characterName = $"{appearance.Gender}_{appearance.BaseClass}_{appearance.VisualFamily}";
        var packageName = Path.GetFileNameWithoutExtension(appearance.SkeletonMeshLocation.PackagePath);
        var objectName = appearance.SkeletonMeshLocation.ObjectName;
        var referenceText = L2AssetManager.BuildReferenceText(packageName, objectName, characterName);
        var skeletalAssetRoot = L2AssetManager.BuildClientPackageObjectRoot(
            PlayerCharacterImportBuilder.AssetOutputRoot,
            packageName,
            objectName,
            "PlayerCharacters");
        var characterAssetPath = L2AssetManager.BuildAssetPathInFolder(
            skeletalAssetRoot,
            "PC",
            objectName,
            "asset",
            "skeleton");
        var archetypeFolder = $"{PlayerCharacterImportBuilder.PrefabOutputRoot}/Archetypes/{appearance.BaseClass}/{appearance.Gender}";
        var archetypeAssetPath = L2AssetManager.BuildAssetPathInFolder(
            archetypeFolder,
            "PCA",
            objectName,
            "asset",
            "archetype");
        var prefabPath = L2AssetManager.BuildClientPackageAssetPath(
            PlayerCharacterImportBuilder.PrefabOutputRoot,
            referenceText,
            "PF",
            "prefab",
            "PlayerCharacterPrefabs",
            "wardrobe");

        var meshAssets = PlayerCharacterMeshAssetResolver.Resolve(
            sources,
            characterName,
            characterAssetPath,
            progress);
        var controller = PlayerCharacterAnimationAssetResolver.Resolve(
            meshAssets.BaseAsset,
            referenceText,
            progress);
        PlayerCharacterPrefabResolver.Resolve(
            plan,
            meshAssets,
            controller,
            characterName,
            archetypeAssetPath,
            prefabPath,
            progress);
        AssetDatabase.SaveAssets();

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        instance.name = prefab.name;
        Selection.activeObject = instance;
        progress.Report("Done", prefabPath, 1f);
        return new PlayerCharacterArchetypeBuilder.ImportResult(prefabPath, archetypeAssetPath);
    }
}
