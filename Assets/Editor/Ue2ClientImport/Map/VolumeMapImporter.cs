using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using L2Viewer.PackageCore;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Services.MaterialServices;
using L2Viewer.UnrFile;
using UnityEngine;

internal static class VolumeMapImporter
{
    public static Task ImportAsync(
        MapImportRequest request,
        Ue2MapSource source,
        Action<string> log,
        MapImportExecutionContext context = null,
        bool finalizeScene = true)
    {
        context?.Report("Volumes", "Build volume data", 0.18f);
        log("[Volumes] START Build volume data");
        var buildStopwatch = Stopwatch.StartNew();
        var volumeBuilder = new SceneVolumeBuilder();
        var volumes = volumeBuilder.Build(source.UnrFile, int.MaxValue);
        var fogBuilder = new SceneFogBuilder();
        var fogZones = fogBuilder.BuildFogZones(source.UnrFile);
        buildStopwatch.Stop();
        log("[Volumes] DONE Build volume data");
        log($"[Volumes] Build volume data took {buildStopwatch.Elapsed.TotalSeconds:F2}s");

        log($"Found {volumes.Length} supported volume actors and {fogZones.Length} zone infos.");

        log("[Volumes] START Scene root preparation");
        context?.Report("Volumes", "Scene root preparation", 0.38f);
        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        var volumesRootName = $"{request.ObjectName}_Volumes";
        if (UnitySceneObjectUtility.ObjectExists(volumesRootName))
        {
            log($"[Volumes] Skipping import because '{volumesRootName}' already exists.");
            return Task.CompletedTask;
        }

        var volumesRoot = new GameObject(volumesRootName);
        volumesRoot.transform.SetParent(mapRoot.transform, false);
        log("[Volumes] DONE Scene root preparation");

        log("[Volumes] START Resolve water textures");
        context?.ThrowIfCancellationRequested();
        context?.Report("Volumes", "Resolve water textures", 0.56f);
        var textureStopwatch = Stopwatch.StartNew();
        var waterTextures = ResolveWaterVolumeTextures(source, context);
        textureStopwatch.Stop();
        log("[Volumes] DONE Resolve water textures");
        log($"[Volumes] Resolve water textures took {textureStopwatch.Elapsed.TotalSeconds:F2}s");

        log("[Volumes] START Build volume objects");
        context?.ThrowIfCancellationRequested();
        context?.Report("Volumes", "Build volume objects", 0.78f);
        var objectStopwatch = Stopwatch.StartNew();
        L2SceneVolumeAssetBuilder.BuildVolumes(volumes, fogZones, request.OutputDir, waterTextures, volumesRoot, log);
        objectStopwatch.Stop();
        log("[Volumes] DONE Build volume objects");
        log($"[Volumes] Build volume objects took {objectStopwatch.Elapsed.TotalSeconds:F2}s");

        if (finalizeScene)
        {
            log("[Volumes] START Finalize");
            context?.ThrowIfCancellationRequested();
            context?.Report("Volumes", "Finalize", 0.96f);
            var finalizeStopwatch = Stopwatch.StartNew();
            MapImportFinalizer.Complete(mapRoot, log);
            finalizeStopwatch.Stop();
            log("[Volumes] DONE Finalize");
            log($"[Volumes] Finalize took {finalizeStopwatch.Elapsed.TotalSeconds:F2}s");
        }
        log("Volume import finished.");
        return Task.CompletedTask;
    }

    private static Dictionary<int, (TextureData Texture, string ReferenceText)> ResolveWaterVolumeTextures(Ue2MapSource source, MapImportExecutionContext context = null)
    {
        var result = new Dictionary<int, (TextureData Texture, string ReferenceText)>();
        var textureManager = new BspTextureManager(source.ClientPath);

        var waterVolumes = source.UnrFile.ExportObjects.Select(x => x.Object).OfType<UnrWaterVolumeObject>().ToList();
        var refs = waterVolumes
            .Where(w => w.TextureReference?.PackageName != null && !string.IsNullOrWhiteSpace(w.TextureReference.ObjectName))
            .Select(w => new { Ref = w.TextureReference, Key = $"{w.TextureReference.PackageName}.{w.TextureReference.ObjectName}" })
            .GroupBy(x => x.Key)
            .Select(g => new L2Viewer.SceneDomain.Services.MaterialServices.SceneTextureRequest(g.First().Ref.PackageName, g.First().Ref.ObjectName))
            .ToList();

        var resolvedDict = textureManager.ResolveMany(refs);

        foreach (var waterVolume in waterVolumes)
        {
            context?.ThrowIfCancellationRequested();
            var textureReference = waterVolume.TextureReference;
            if (textureReference?.PackageName == null || string.IsNullOrWhiteSpace(textureReference.ObjectName))
            {
                continue;
            }

            if (resolvedDict != null &&
                resolvedDict.TryGetValue($"{textureReference.PackageName}.{textureReference.ObjectName}", out var resolved) &&
                resolved != null &&
                resolved.Texture != null)
            {
                var referenceText = $"{textureReference.PackageName}.{textureReference.ObjectName}";
                result[waterVolume.ExportIndex] = (resolved.Texture, referenceText);
            }
        }

        return result;
    }
}
