using System;
using L2Viewer.SceneDomain.Models;
using UnityEngine;

internal static class BspImportPipeline
{
    private static readonly Action<string> NoLog = _ => { };

    public static SceneBspScene Analyze(Ue2MapSource source)
    {
        return new L2Viewer.SceneDomain.Services.BSPServices.SceneBspRoomBuilder()
            .Build(source.UnrFile);
    }

    public static void Import(
        SceneBspScene scene,
        GameObject parent,
        MapImportRequest request,
        MapImportExecutionContext context)
    {
        L2BspAssetBuilder.BuildBsp(
            scene,
            parent,
            request.MapKey,
            request.OutputDir,
            NoLog,
            context,
            reuseExistingMaterialTextureAssets: true);
    }
}
