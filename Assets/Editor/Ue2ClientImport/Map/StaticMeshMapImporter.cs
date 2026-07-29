using System;
using System.Diagnostics;
using System.Threading.Tasks;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using UnityEngine;

internal static class StaticMeshMapImporter
{
    public static Task ImportAsync(
        MapImportRequest request,
        Ue2MapSource source,
        Action<string> log,
        MapImportExecutionContext context = null,
        bool finalizeScene = true,
        bool convertTerrainDecorationsToTerrainVegetation = true)
    {
        context?.Report("Static Meshes", "Scene analysis", 0.12f);
        log("[StaticMesh] START Scene analysis");
        var analysisStopwatch = Stopwatch.StartNew();
        var instancedResult = StaticMeshSceneAnalyzer.BuildInstancedMeshes(source, log);
        analysisStopwatch.Stop();
        log($"[StaticMesh] DONE Scene analysis ({analysisStopwatch.Elapsed.TotalSeconds:F2}s)");

        log($"Found {instancedResult.UniqueMeshes.Count} unique meshes, {instancedResult.Instances.Count} instances, and {instancedResult.TerrainDecorations.Count} terrain deco layers in {request.MapKey}.");

        if (instancedResult.Instances.Count == 0 && instancedResult.TerrainDecorations.Count == 0)
        {
            log("No static meshes or terrain decorations to import.");
            return Task.CompletedTask;
        }

        log("[StaticMesh] START Scene root preparation");
        context?.Report("Static Meshes", "Scene root preparation", 0.18f);
        MapImportAssetPreparation.EnsureMapOutputFolderExists(request.OutputDir);
        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        UnitySceneObjectUtility.RemoveExistingObject($"{request.ObjectName}_StaticMeshes");

        var staticMeshRoot = new GameObject($"{request.ObjectName}_StaticMeshes");
        staticMeshRoot.transform.SetParent(mapRoot.transform, false);
        log("[StaticMesh] DONE Scene root preparation");

        var terrainImport = TryLoadTerrainImportData(source, log);

        log($"Importing {instancedResult.Instances.Count} static meshes using Unity instancing...");
        log("[StaticMesh] START Asset pipeline");
        context?.Report("Static Meshes", "Asset pipeline", 0.28f);
        var pipelineStopwatch = Stopwatch.StartNew();
        L2StaticMeshAssetBuilder.BuildStaticMeshes(
                instancedResult,
                staticMeshRoot,
                source.ClientPath,
                request.MapKey,
                request.OutputDir,
                log,
                request.ReuseExistingMaterialTextureAssets,
                convertTerrainDecorationsToTerrainVegetation: convertTerrainDecorationsToTerrainVegetation,
                terrainImport: terrainImport,
                populateTerrainVegetation: false,
                context: context);
        pipelineStopwatch.Stop();
        log($"[StaticMesh] DONE Asset pipeline ({pipelineStopwatch.Elapsed.TotalSeconds:F2}s)");

        if (finalizeScene)
        {
            log("[StaticMesh] START Finalize");
            context?.Report("Static Meshes", "Finalize", 0.96f);
            var finalizeStopwatch = Stopwatch.StartNew();
            MapImportFinalizer.Complete(mapRoot, log);
            finalizeStopwatch.Stop();
            log($"[StaticMesh] DONE Finalize ({finalizeStopwatch.Elapsed.TotalSeconds:F2}s)");
        }
        log("Mesh import finished.");
        return Task.CompletedTask;
    }

    private static TerrainImportData TryLoadTerrainImportData(Ue2MapSource source, Action<string> log)
    {
        try
        {
            var terrains = new TerrainImportBuilder(new BspTextureManager(source.ClientPath)).Build(source.UnrFile);
            if (terrains == null || terrains.Length == 0)
            {
                return null;
            }

            log("[StaticMesh] Terrain surface data loaded for vegetation filtering.");
            return terrains[0];
        }
        catch (Exception ex)
        {
            log($"[StaticMesh] Terrain surface data could not be loaded for vegetation filtering: {ex.Message}");
            return null;
        }
    }
}
