using System;
using System.Threading.Tasks;
using L2Viewer.SceneDomain.Services;
using UnityEngine;

internal static class MapContextImporter
{
    public static Task ImportAsync(MapImportRequest request, Ue2MapSource source, Action<string> log)
    {
        log("[MapContext] START Build context data");
        var mapContextBuilder = new SceneMapContextBuilder();
        var mapContext = mapContextBuilder.Build(source.UnrFile);
        log("[MapContext] DONE Build context data");

        if (mapContext == null)
        {
            log("Map context was not built.");
            return Task.CompletedTask;
        }

        log("[MapContext] START Scene root preparation");
        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        MapImportAssetPreparation.EnsureMapOutputFolderExists(request.OutputDir);

        var contextAsset = L2MapContextAssetBuilder.BuildContextAsset(mapContext, request.OutputDir);
        var contextRootName = $"{request.ObjectName}_Context";
        UnitySceneObjectUtility.RemoveExistingObject(contextRootName);

        var contextRoot = new GameObject(contextRootName);
        contextRoot.transform.SetParent(mapRoot.transform, false);
        log("[MapContext] DONE Scene root preparation");

        log("[MapContext] START Attach context volume");
        var contextVolume = contextRoot.AddComponent<L2MapContextVolume>();
        contextVolume.Context = contextAsset;
        contextVolume.RefreshContext();
        log("[MapContext] DONE Attach context volume");

        log("[MapContext] START Finalize");
        MapImportFinalizer.Complete(mapRoot, log);
        log("[MapContext] DONE Finalize");
        log($"Imported map context for {mapContext.MapKey} with {mapContext.Nodes.Length} nodes and {mapContext.Zones.Length} zones.");
        return Task.CompletedTask;
    }
}
