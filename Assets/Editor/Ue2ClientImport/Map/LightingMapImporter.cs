using System;
using System.Threading.Tasks;
using L2Viewer.SceneDomain.Services;
using UnityEngine;

internal static class LightingMapImporter
{
    public static Task ImportAsync(MapImportRequest request, Ue2MapSource source, Action<string> log)
    {
        log("[Lighting] START Build light data");
        var lightingBuilder = new SceneLightingBuilder();
        var lightImport = (
            Lights: lightingBuilder.BuildLights(source.UnrFile),
            Suns: lightingBuilder.BuildSuns(source.UnrFile),
            Moons: lightingBuilder.BuildMoons(source.UnrFile));
        log("[Lighting] DONE Build light data");

        if (lightImport.Lights == null || lightImport.Lights.Length == 0)
        {
            log($"No point/spot scene lights found. Context only: suns={lightImport.Suns.Length}, moons={lightImport.Moons.Length}.");
            return Task.CompletedTask;
        }

        log($"Found light actors: point/spot={lightImport.Lights.Length}. Context only: suns={lightImport.Suns.Length}, moons={lightImport.Moons.Length}.");

        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        var lightsRootName = $"{request.ObjectName}_Lights";
        UnitySceneObjectUtility.RemoveExistingObject(lightsRootName);
        UnitySceneObjectUtility.RemoveExistingObject($"{request.ObjectName}_LightingVolume");

        log("[Lighting] START Scene root preparation");
        MapImportAssetPreparation.EnsureMapOutputFolderExists(request.OutputDir);

        var lightsRoot = new GameObject(lightsRootName);
        lightsRoot.isStatic = true;
        lightsRoot.transform.SetParent(mapRoot.transform, false);
        log("[Lighting] DONE Scene root preparation");

        log("[Lighting] START Build light objects");
        L2LightAssetBuilder.BuildLights(lightImport.Lights, lightImport.Suns, lightImport.Moons, lightsRoot, log);
        log("[Lighting] DONE Build light objects");

        log("[Lighting] START Finalize");
        MapImportFinalizer.Complete(mapRoot, log);
        log("[Lighting] DONE Finalize");
        log("Light import finished.");
        return Task.CompletedTask;
    }
}

