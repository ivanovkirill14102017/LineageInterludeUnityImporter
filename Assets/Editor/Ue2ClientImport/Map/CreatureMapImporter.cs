using System;
using System.Threading.Tasks;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Services.CharacterServices;
using UnityEngine;

internal static class CreatureMapImporter
{
    public static Task ImportAsync(
        MapImportRequest request,
        Ue2MapSource source,
        Action<string> log,
        MapImportExecutionContext context = null,
        bool finalizeScene = true)
    {
        var spawns = new SceneCreatureMapBuilder().Build(
            ConstInfo.L2DbRootPath,
            source.ClientPath,
            request.MapKey,
            source.UnrFile);
        if (spawns.Length == 0)
        {
            return Task.CompletedTask;
        }

        MapImportAssetPreparation.EnsureMapOutputFolderExists(request.OutputDir);
        MapImportSceneManager.RemoveSectionPlacement(request, "Creatures");
        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        var creatureRoot = new GameObject($"{request.ObjectName}_Creatures");
        creatureRoot.transform.SetParent(mapRoot.transform, false);
        try
        {
            CreatureImportPipeline.Import(
                CreatureImportPlanBuilder.Build(spawns),
                source.ClientPath,
                ConstInfo.L2DbRootPath,
                creatureRoot,
                context);
        }
        catch
        {
            UnityEngine.Object.DestroyImmediate(creatureRoot);
            throw;
        }

        if (finalizeScene)
        {
            MapImportFinalizer.Complete(mapRoot, log);
        }

        return Task.CompletedTask;
    }
}
