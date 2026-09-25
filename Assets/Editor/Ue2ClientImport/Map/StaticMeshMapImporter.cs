using System;
using System.Threading.Tasks;
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
        var scene = StaticMeshImportPipeline.Analyze(source);
        var terrain = StaticMeshImportPipeline.ReadTerrainSurface(source);
        MapImportAssetPreparation.EnsureMapOutputFolderExists(request.OutputDir);
        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        var root = new GameObject($"{request.ObjectName}_StaticMeshes");
        root.transform.SetParent(mapRoot.transform, false);
        StaticMeshImportPipeline.ImportStaticMeshInstances(scene, terrain, root, source, request);
        if (finalizeScene)
        {
            MapImportFinalizer.Complete(mapRoot, log);
        }

        return Task.CompletedTask;
    }
}
