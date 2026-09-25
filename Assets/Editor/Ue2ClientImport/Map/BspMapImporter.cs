using System;
using System.Threading.Tasks;
using UnityEngine;

internal static class BspMapImporter
{
    public static Task ImportAsync(
        MapImportRequest request,
        Ue2MapSource source,
        Action<string> log,
        MapImportExecutionContext context = null,
        bool finalizeScene = true)
    {
        var scene = BspImportPipeline.Analyze(source);
        MapImportAssetPreparation.PrepareBspVariantFolder(
            $"{request.OutputDir}/Bsp",
            reuseExistingMaterialTextureAssets: true);

        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        var root = new GameObject($"{request.ObjectName}_BSP");
        root.transform.SetParent(mapRoot.transform, false);
        BspImportPipeline.Import(scene, root, request, context);

        if (finalizeScene)
        {
            MapImportFinalizer.Complete(mapRoot, log);
        }

        return Task.CompletedTask;
    }
}
