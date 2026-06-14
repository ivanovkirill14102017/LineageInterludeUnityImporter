using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using L2Viewer.PackageCore;
using L2Viewer.SceneDomain.Services;
using L2Viewer.UnrFile;
using UnityEngine;

internal static class VolumeMapImporter
{
    public static Task ImportAsync(MapImportRequest request, Ue2MapSource source, Action<string> log)
    {
        log("[Volumes] START Build volume data");
        var volumeBuilder = new SceneVolumeBuilder();
        var volumes = volumeBuilder.Build(source.UnrFile, int.MaxValue);
        var fogBuilder = new SceneFogBuilder();
        var fogZones = fogBuilder.BuildFogZones(source.UnrFile);
        log("[Volumes] DONE Build volume data");

        log($"Found {volumes.Length} supported volume actors and {fogZones.Length} zone infos.");

        log("[Volumes] START Scene root preparation");
        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        var volumesRootName = $"{request.ObjectName}_Volumes";
        UnitySceneObjectUtility.RemoveExistingObject(volumesRootName);

        var volumesRoot = new GameObject(volumesRootName);
        volumesRoot.transform.SetParent(mapRoot.transform, false);
        log("[Volumes] DONE Scene root preparation");

        log("[Volumes] START Resolve water textures");
        var waterTextures = ResolveWaterVolumeTextures(source);
        log("[Volumes] DONE Resolve water textures");

        log("[Volumes] START Build volume objects");
        L2SceneVolumeAssetBuilder.BuildVolumes(volumes, fogZones, request.OutputDir, waterTextures, volumesRoot, log);
        log("[Volumes] DONE Build volume objects");

        log("[Volumes] START Finalize");
        MapImportFinalizer.Complete(mapRoot, log);
        log("[Volumes] DONE Finalize");
        log("Volume import finished.");
        return Task.CompletedTask;
    }

    private static Dictionary<int, (TextureData Texture, string ReferenceText)> ResolveWaterVolumeTextures(Ue2MapSource source)
    {
        var result = new Dictionary<int, (TextureData Texture, string ReferenceText)>();
        var textureManager = new BspTextureManager(source.ClientPath);

        var waterVolumes = source.UnrFile.ExportObjects.Select(x => x.Object).OfType<UnrWaterVolumeObject>().ToList();
        var refs = waterVolumes
            .Where(w => w.TextureReference?.PackageName != null && !string.IsNullOrWhiteSpace(w.TextureReference.ObjectName))
            .Select(w => new { Ref = w.TextureReference, Key = $"{w.TextureReference.PackageName}.{w.TextureReference.ObjectName}" })
            .GroupBy(x => x.Key)
            .Select(g => new L2Viewer.SceneDomain.Services.SceneTextureRequest(g.First().Ref.PackageName, g.First().Ref.ObjectName))
            .ToList();

        var resolvedDict = textureManager.ResolveMany(refs);

        foreach (var waterVolume in waterVolumes)
        {
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
