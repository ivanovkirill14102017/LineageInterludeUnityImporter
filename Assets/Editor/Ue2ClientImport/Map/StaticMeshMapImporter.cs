using System;
using System.Diagnostics;
using System.Threading.Tasks;
using UnityEngine;

internal static class StaticMeshMapImporter
{
    public static Task ImportAsync(MapImportRequest request, Ue2MapSource source, Action<string> log)
    {
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
        MapImportAssetPreparation.EnsureMapOutputFolderExists(request.OutputDir);
        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        UnitySceneObjectUtility.RemoveExistingObject($"{request.ObjectName}_StaticMeshes");

        var staticMeshRoot = new GameObject($"{request.ObjectName}_StaticMeshes");
        staticMeshRoot.transform.SetParent(mapRoot.transform, false);
        log("[StaticMesh] DONE Scene root preparation");

        log($"Importing {instancedResult.Instances.Count} static meshes using Unity instancing...");
        log("[StaticMesh] START Asset pipeline");
        var pipelineStopwatch = Stopwatch.StartNew();
        L2StaticMeshAssetBuilder.BuildStaticMeshes(
                instancedResult,
                staticMeshRoot,
                source.ClientPath,
                request.MapKey,
                request.OutputDir,
                log,
                request.ReuseExistingMaterialTextureAssets);
        pipelineStopwatch.Stop();
        log($"[StaticMesh] DONE Asset pipeline ({pipelineStopwatch.Elapsed.TotalSeconds:F2}s)");

        log("[StaticMesh] START Finalize");
        var finalizeStopwatch = Stopwatch.StartNew();
        MapImportFinalizer.Complete(mapRoot, log);
        finalizeStopwatch.Stop();
        log($"[StaticMesh] DONE Finalize ({finalizeStopwatch.Elapsed.TotalSeconds:F2}s)");
        log("Mesh import finished.");
        return Task.CompletedTask;
    }
}
