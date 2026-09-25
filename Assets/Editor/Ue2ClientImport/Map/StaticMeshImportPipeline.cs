using System;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using UnityEngine;

internal static class StaticMeshImportPipeline
{
    private static readonly Action<string> NoLog = _ => { };

    public static SceneInstancedMeshResult Analyze(Ue2MapSource source)
    {
        return StaticMeshSceneAnalyzer.BuildInstancedMeshes(source, NoLog);
    }

    public static TerrainImportData ReadTerrainSurface(Ue2MapSource source)
    {
        return new TerrainImportBuilder(
                new L2Viewer.SceneDomain.Services.MaterialServices.BspTextureManager(source.ClientPath))
            .Build(source.UnrFile)[0];
    }

    public static void ImportStaticMeshInstances(
        SceneInstancedMeshResult scene,
        TerrainImportData terrain,
        GameObject parent,
        Ue2MapSource source,
        MapImportRequest request)
    {
        L2StaticMeshAssetBuilder.BuildStaticMeshes(
            scene,
            parent,
            source.ClientPath,
            request.MapKey,
            request.OutputDir,
            NoLog,
            reuseExistingMaterialTextureAssets: true,
            convertTerrainDecorationsToTerrainVegetation: true,
            terrainImport: terrain,
            populateTerrainVegetation: false);
    }

    public static void PrepareTerrainVegetationResources(
        SceneInstancedMeshResult scene,
        TerrainImportData terrain,
        GameObject parent,
        Ue2MapSource source,
        MapImportRequest request)
    {
        L2StaticMeshAssetBuilder.BuildStaticMeshes(
            scene,
            parent,
            source.ClientPath,
            request.MapKey,
            request.OutputDir,
            NoLog,
            reuseExistingMaterialTextureAssets: true,
            placeRegularInstances: false,
            placeTerrainDecorations: false,
            convertTerrainDecorationsToTerrainVegetation: true,
            convertTreeInstancesToTerrainVegetation: true,
            placeTreeInstancesAsRegularInstances: false,
            terrainImport: terrain,
            populateTerrainVegetation: false);
    }

    public static void PopulateTerrainVegetation(
        SceneInstancedMeshResult scene,
        TerrainImportData terrain,
        GameObject parent,
        Ue2MapSource source,
        MapImportRequest request)
    {
        L2StaticMeshAssetBuilder.BuildStaticMeshes(
            scene,
            parent,
            source.ClientPath,
            request.MapKey,
            request.OutputDir,
            NoLog,
            reuseExistingMaterialTextureAssets: true,
            placeRegularInstances: false,
            placeTerrainDecorations: false,
            convertTerrainDecorationsToTerrainVegetation: true,
            convertTreeInstancesToTerrainVegetation: true,
            placeTreeInstancesAsRegularInstances: false,
            terrainImport: terrain,
            populateTerrainVegetation: true,
            removeExistingConvertedTerrainVegetationFallback: false);
    }
}
