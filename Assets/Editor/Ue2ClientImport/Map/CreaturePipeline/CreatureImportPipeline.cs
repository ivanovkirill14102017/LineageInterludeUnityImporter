using System;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services.CharacterServices;
using L2Viewer.SceneDomain.Services.Utility;
using UnityEditor;
using UnityEngine;

internal static class CreatureImportPipeline
{
    private const float UnrealUnitsToDegrees = 360f / 65536f;

    public static void Import(
        CreatureImportPlan plan,
        string clientRoot,
        string dbRootPath,
        GameObject parent,
        MapImportExecutionContext context)
    {
        var progress = new CreatureImportProgress(context);
        progress.Report("Plan", $"{plan.Spawns.Length} spawns, {plan.Visuals.Length} visuals", 0.02f);
        var visualSources = CreatureVisualSourceResolver.Resolve(plan, progress);
        var itemIds = plan.Compositions
            .SelectMany(x => new[] { x.Source.RightHandItemId, x.Source.LeftHandItemId })
            .Where(x => x > 0)
            .Distinct()
            .ToArray();
        var catalogs = itemIds.Length == 0
            ? Array.Empty<SceneCharacterEquipmentCatalogData>()
            : new[]
            {
                new SceneCharacterEquipmentCatalogBuilder().Build(
                    clientRoot,
                    dbRootPath,
                    SceneCharacterBaseClass.HumanFighter,
                    SceneCharacterGender.Male)
            };
        var equipmentSources = EquipmentSourceResourceResolver.Resolve(itemIds, catalogs);
        var effectSources = ParticleEffectSourceResourceResolver.Resolve(
            plan.Compositions.SelectMany(x => x.Source.AttachedEffects));
        var surfaceReferences = visualSources.TextureReferences
            .Concat(equipmentSources.TextureReferences)
            .Concat(effectSources.TextureReferences)
            .Distinct()
            .ToArray();
        var requestedMaterials = visualSources.MaterialRequests
            .Concat(equipmentSources.MaterialRequests)
            .GroupBy(x => x.Id)
            .Select(x => x.First())
            .ToArray();

        var surfaceResolver = new L2Viewer.SceneDomain.Services.MaterialServices.SceneSurfaceSourceResolver(clientRoot);
        var surfaceSources = new L2Viewer.SceneDomain.Services.MaterialServices.SceneSurfaceSourceResource[surfaceReferences.Length];
        for (var index = 0; index < surfaceReferences.Length; index++)
        {
            var reference = surfaceReferences[index];
            progress.ReportItem("Surfaces / material graph", reference, index, surfaceReferences.Length, 0.20f, 0.28f);
            surfaceSources[index] = surfaceResolver.Resolve(reference);
        }

        var texturesBySurface = surfaceSources.ToDictionary(x => x.SurfaceReference, x => x.TextureReference);
        var materialRequests = requestedMaterials
            .Select(x => x.WithTextureReference(texturesBySurface[x.Id.Surface]))
            .ToArray();

        SharedTextureResourceResolver.Resolve(surfaceSources, progress);
        SharedMaterialResourceResolver.Resolve(materialRequests, progress);
        CreatureBaseVisualPrefabResolver.Resolve(visualSources, clientRoot, progress);
        progress.Report("Package index", clientRoot, 0.78f);
        var packageIndex = ScenePackageIndexer.BuildResourcePackageIndex(clientRoot);
        CreatureEquipmentPrefabResolver.Resolve(
            plan,
            visualSources,
            equipmentSources,
            clientRoot,
            packageIndex,
            progress);
        CreatureCompositionPrefabResolver.Resolve(plan, equipmentSources, clientRoot, progress);
        PlaceSpawns(plan, parent, progress);
        progress.Report("Done", $"{plan.Spawns.Length} creatures", 1f);
    }

    private static void PlaceSpawns(
        CreatureImportPlan plan,
        GameObject parent,
        CreatureImportProgress progress)
    {
        var compositions = plan.Compositions.ToDictionary(x => x.Key);
        for (var index = 0; index < plan.Spawns.Length; index++)
        {
            var spawn = plan.Spawns[index];
            progress.ReportItem("Placement", spawn.StableName, index, plan.Spawns.Length, 0.92f, 0.99f);
            var composition = compositions[CreatureImportPlanBuilder.BuildCompositionKey(spawn)];
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                CreatureCompositionPrefabResolver.BuildPrefabPath(composition));
            var instance = PrefabUtility.InstantiatePrefab(prefab, parent.transform) as GameObject;
            instance!.name = spawn.StableName;
            instance.transform.localPosition = spawn.Position.TransformFromUnrealToUnityWithScale();
            instance.transform.localRotation = Quaternion.Euler(
                0f,
                -(spawn.Heading * UnrealUnitsToDegrees),
                0f);
            instance.transform.localScale = Vector3.one;
        }
    }
}
