using System;
using System.Threading.Tasks;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using UnityEngine;

internal static class BspMapImporter
{
    public static Task ImportAsync(MapImportRequest request, Ue2MapSource source, Action<string> log)
    {
        log("[BSP] START Build room-grouped BSP scene");
        var bspBuilder = new SceneBspRoomBuilder();
        var bspScene = bspBuilder.Build(source.UnrFile);
        log("[BSP] DONE Build room-grouped BSP scene");

        if (!HasBspModels(bspScene))
        {
            log("No BSP models found.");
            return Task.CompletedTask;
        }

        log("[BSP] START Scene root preparation");
        MapImportAssetPreparation.EnsureMapOutputFolderExists(request.OutputDir);
        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        MapImportAssetPreparation.PrepareBspVariantFolder($"{request.OutputDir}/Bsp", request.ReuseExistingMaterialTextureAssets);
        log("[BSP] DONE Scene root preparation");

        ImportBspVariant(
            bspScene,
            request,
            log,
            $"{request.ObjectName}_BSP",
            "Bsp",
            includePortalLike: false,
            includeInvisibleLike: false,
            variantLabel: "room-grouped",
            mapRoot: mapRoot);

        log("[BSP] START Finalize");
        MapImportFinalizer.Complete(mapRoot, log);
        log("[BSP] DONE Finalize");
        log("BSP import finished.");
        return Task.CompletedTask;
    }

    private static bool HasBspModels(SceneBspScene scene)
    {
        return scene != null && scene.Models != null && scene.Models.Length > 0;
    }

    private static void ImportBspVariant(
        SceneBspScene scene,
        MapImportRequest request,
        Action<string> log,
        string rootObjectName,
        string assetSubdirName,
        bool includePortalLike,
        bool includeInvisibleLike,
        string variantLabel,
        GameObject mapRoot)
    {
        log($"Importing {scene.Models.Length} {variantLabel} BSP models...");

        UnitySceneObjectUtility.RemoveExistingObject(rootObjectName);

        var root = new GameObject(rootObjectName);
        root.transform.SetParent(mapRoot.transform, false);

        L2BspAssetBuilder.BuildBsp(
            scene,
            root,
            request.MapKey,
            request.OutputDir,
            log,
            assetSubdirName: assetSubdirName,
            includePortalLike: includePortalLike,
            includeInvisibleLike: includeInvisibleLike,
            reuseExistingMaterialTextureAssets: request.ReuseExistingMaterialTextureAssets);
    }
}
