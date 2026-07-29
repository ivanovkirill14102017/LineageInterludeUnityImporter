using System;
using System.Diagnostics;
using System.Threading.Tasks;
using L2Viewer.SceneDomain.Services;
using UnityEngine;

internal static class LightingMapImporter
{
    public static Task ImportAsync(
        MapImportRequest request,
        Ue2MapSource source,
        Action<string> log,
        MapImportExecutionContext context = null,
        bool finalizeScene = true)
    {
        context?.Report("Lights", "Build light data", 0.18f);
        log("[Lighting] START Build light data");
        var buildStopwatch = Stopwatch.StartNew();
        var lightingBuilder = new SceneLightingBuilder();
        var lightImport = (
            Lights: lightingBuilder.BuildLights(source.UnrFile),
            Suns: lightingBuilder.BuildSuns(source.UnrFile),
            Moons: lightingBuilder.BuildMoons(source.UnrFile));
        buildStopwatch.Stop();
        log("[Lighting] DONE Build light data");
        log($"[Lighting] Build light data took {buildStopwatch.Elapsed.TotalSeconds:F2}s");

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
        context?.Report("Lights", "Scene root preparation", 0.42f);
        MapImportAssetPreparation.EnsureMapOutputFolderExists(request.OutputDir);

        var lightsRoot = new GameObject(lightsRootName);
        lightsRoot.isStatic = true;
        lightsRoot.transform.SetParent(mapRoot.transform, false);
        log("[Lighting] DONE Scene root preparation");

        log("[Lighting] START Build light objects");
        context?.ThrowIfCancellationRequested();
        context?.Report("Lights", "Build light objects", 0.70f);
        var objectStopwatch = Stopwatch.StartNew();
        L2LightAssetBuilder.BuildLights(lightImport.Lights, lightImport.Suns, lightImport.Moons, lightsRoot, log);
        objectStopwatch.Stop();
        log("[Lighting] DONE Build light objects");
        log($"[Lighting] Build light objects took {objectStopwatch.Elapsed.TotalSeconds:F2}s");

        if (finalizeScene)
        {
            log("[Lighting] START Finalize");
            context?.ThrowIfCancellationRequested();
            context?.Report("Lights", "Finalize", 0.96f);
            var finalizeStopwatch = Stopwatch.StartNew();
            MapImportFinalizer.Complete(mapRoot, log);
            finalizeStopwatch.Stop();
            log("[Lighting] DONE Finalize");
            log($"[Lighting] Finalize took {finalizeStopwatch.Elapsed.TotalSeconds:F2}s");
        }
        log("Light import finished.");
        return Task.CompletedTask;
    }
}

