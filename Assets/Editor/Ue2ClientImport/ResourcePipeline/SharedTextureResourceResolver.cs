using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services.MaterialServices;
using UnityEditor;
using UnityEngine;

internal static class SharedTextureResourceResolver
{
    public static string BuildAssetPath(SceneSurfaceResourceReference reference)
    {
        return ImportedTextureAssetUtility.BuildTextureAssetPath(
            L2AssetManager.SharedTexturesRoot,
            reference.ToString(),
            "ResourceTextures");
    }

    public static void Resolve(
        IReadOnlyCollection<SceneSurfaceSourceResource> surfaceSources,
        IResourceImportProgress progress)
    {
        var refreshRequired = false;
        var sources = surfaceSources
            .GroupBy(x => x.TextureReference)
            .Select(x => x.First())
            .ToArray();
        for (var index = 0; index < sources.Length; index++)
        {
            var source = sources[index];
            progress.ReportItem("Textures / assets", source.TextureReference, index, sources.Length, 0.28f, 0.38f);
            var needsRefresh = ImportedTextureAssetUtility.PrepareTextureAssetFile(
                source.TextureReference.ToString(),
                source.Texture,
                L2AssetManager.SharedTexturesRoot,
                "ResourceTextures",
                traits: null,
                reuseExisting: true,
                out var path,
                out var loadedTexture);
            if (loadedTexture != null)
            {
                continue;
            }

            if (needsRefresh)
            {
                refreshRequired = true;
            }
        }

        if (refreshRequired)
        {
            AssetDatabase.Refresh();
        }
    }
}
