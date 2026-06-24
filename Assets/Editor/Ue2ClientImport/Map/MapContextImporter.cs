using System;
using System.Threading.Tasks;
using L2Viewer.SceneDomain.Services;
using UnityEngine;

internal static class MapContextImporter
{
    public static void ImportAsync(MapImportRequest request, Ue2MapSource source, Action<string> log)
    {
        var mapContextBuilder = new SceneMapContextBuilder();
        var mapContext = mapContextBuilder.Build(source.UnrFile);

        if (mapContext == null)
        {
            log("Map context was not built.");
            return;
        }

        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        MapImportAssetPreparation.EnsureMapOutputFolderExists(request.OutputDir);

        var contextAsset = L2MapContextAssetBuilder.BuildContextAsset(mapContext, request.OutputDir);
        var contextRootName = $"{request.ObjectName}_Context";
        UnitySceneObjectUtility.RemoveExistingObject(contextRootName);

        var contextRoot = new GameObject(contextRootName);
        contextRoot.transform.SetParent(mapRoot.transform, false);

        var contextVolume = contextRoot.AddComponent<L2MapContextVolume>();
        contextVolume.Context = contextAsset;
        contextVolume.RefreshContext();

        MapImportFinalizer.Complete(mapRoot, log);
    }
}
