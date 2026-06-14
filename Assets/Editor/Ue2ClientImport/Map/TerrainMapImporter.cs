using System;
using System.Diagnostics;
using System.Threading.Tasks;
using L2Viewer.SceneDomain.Services;

internal static class TerrainMapImporter
{
    public static Task ImportAsync(MapImportRequest request, Ue2MapSource source, Action<string> log)
    {
        log("[Terrain] START Build terrain import data");
        var terrainBuilder = new TerrainImportBuilder(new BspTextureManager(source.ClientPath));
        var terrains = terrainBuilder.Build(source.UnrFile);
        log("[Terrain] DONE Build terrain import data");

        if (terrains == null || terrains.Length == 0)
        {
            throw new InvalidOperationException("TerrainInfo was not found in the map.");
        }

        var terrainImport = terrains[0];
        log($"TerrainInfo found: {terrainImport.ObjectName}, height size {terrainImport.HeightWidth}x{terrainImport.HeightHeight}, layers {terrainImport.Layers.Length}");

        log("[Terrain] START Scene root preparation");
        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        UnitySceneObjectUtility.RemoveExistingObject($"{request.ObjectName}_Terrain");
        MapImportAssetPreparation.PrepareTerrainOutputFolder(request.OutputDir);
        log("[Terrain] DONE Scene root preparation");

        log("[Terrain] START Build terrain assets and object");
        TerrainAssetBuilder.BuildTerrain(terrainImport, request, mapRoot);
        log("[Terrain] DONE Build terrain assets and object");

        log("[Terrain] START Terrain vegetation analysis");
        var analysisStopwatch = Stopwatch.StartNew();
        var instancedResult = StaticMeshSceneAnalyzer.BuildInstancedMeshes(source, log);
        analysisStopwatch.Stop();
        log($"[Terrain] DONE Terrain vegetation analysis ({analysisStopwatch.Elapsed.TotalSeconds:F2}s)");

        log("[Terrain] START Terrain vegetation build");
        var vegetationStopwatch = Stopwatch.StartNew();
        L2StaticMeshAssetBuilder.BuildStaticMeshes(
            instancedResult,
            mapRoot,
            source.ClientPath,
            request.MapKey,
            request.OutputDir,
            log,
            request.ImportTrees,
            request.ImportNonTrees,
            request.ReuseExistingMaterialTextureAssets,
            placeRegularInstances: false,
            placeTerrainDecorations: false,
            convertTerrainDecorationsToTerrainVegetation: true,
            convertTreeInstancesToTerrainVegetation: true,
            placeTreeInstancesAsRegularInstances: false);
        vegetationStopwatch.Stop();
        log($"[Terrain] DONE Terrain vegetation build ({vegetationStopwatch.Elapsed.TotalSeconds:F2}s)");

        log("[Terrain] START Finalize");
        MapImportFinalizer.Complete(mapRoot, log);
        log("[Terrain] DONE Finalize");
        log("Import finished.");
        return Task.CompletedTask;
    }
}
