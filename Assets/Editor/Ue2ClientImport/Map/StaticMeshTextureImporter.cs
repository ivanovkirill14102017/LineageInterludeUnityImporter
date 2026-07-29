using System.Collections.Generic;
using System.Linq;
using L2Viewer.PackageCore;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Services.MaterialServices;
using UnityEditor;
using UnityEngine;

internal static class StaticMeshTextureImporter
{
    public static StaticMeshTextureCatalog ImportTextures(
        IReadOnlyDictionary<string, SceneStaticMeshDefinition> meshDefinitions,
        string clientPath,
        string mapKey,
        string textureDir,
        bool reuseExistingMaterialTextureAssets,
        System.Action<string> log)
    {
        var catalog = new StaticMeshTextureCatalog();
        var textureManager = new BspTextureManager(clientPath);
        var pendingTexturePaths = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase);
        var flipbookReferencesByBindingKey = new Dictionary<string, string[]>(System.StringComparer.OrdinalIgnoreCase);

        var primaryRequests = meshDefinitions.Values
            .SelectMany(def => def.SubMeshes ?? System.Array.Empty<SceneStaticMeshSubMeshDefinition>())
            .Where(subMesh => subMesh.PrimaryTextureResource != null)
            .Select(subMesh => new SceneTextureRequest(subMesh.PrimaryTextureResource.PackageName, subMesh.PrimaryTextureResource.ObjectName))
            .Distinct()
            .ToList();

        var resolvedPrimaryTextures = textureManager.ResolveMany(primaryRequests);
        log($"[StaticMesh/Textures] Resolved {primaryRequests.Count} primary texture requests");

        foreach (var pair in meshDefinitions)
        {
            var meshReference = pair.Key;
            var meshDefinition = pair.Value;

            if (meshDefinition.SubMeshes == null)
            {
                continue;
            }

            foreach (var subMesh in meshDefinition.SubMeshes)
            {
                var bindingKey = StaticMeshImportUtility.BuildBindingKey(meshReference, subMesh.MaterialId);
                var traits = subMesh.Material == null ? null : MaterialHeuristics.GetKnownTraits(subMesh.Material);
                catalog.TraitsByBindingKey[bindingKey] = traits;

                var flipbookReferences = ImportFlipbookFrames(
                    subMesh,
                    bindingKey,
                    mapKey,
                    textureDir,
                    traits,
                    reuseExistingMaterialTextureAssets,
                    catalog,
                    pendingTexturePaths,
                    log);
                if (flipbookReferences != null && flipbookReferences.Length > 1)
                {
                    flipbookReferencesByBindingKey[bindingKey] = flipbookReferences;
                }

                if (catalog.PrimaryTextureReferenceByBindingKey.ContainsKey(bindingKey))
                {
                    continue;
                }

                var primaryReference = subMesh.PrimaryTextureReference ?? subMesh.MaterialReference ?? $"Tex_Mat{subMesh.MaterialId}";
                if (subMesh.PrimaryTextureResource == null)
                {
                    catalog.PrimaryTextureReferenceByBindingKey[bindingKey] = primaryReference;
                    continue;
                }

                var lookup = $"{subMesh.PrimaryTextureResource.PackageName}.{subMesh.PrimaryTextureResource.ObjectName}";
                if (resolvedPrimaryTextures != null &&
                    resolvedPrimaryTextures.TryGetValue(lookup, out var resolved) &&
                    resolved?.Texture != null)
                {
                    PrepareTextureAsset(
                        primaryReference,
                        resolved.Texture,
                        mapKey,
                        textureDir,
                        traits,
                        reuseExistingMaterialTextureAssets,
                        catalog,
                        pendingTexturePaths,
                        log);
                }

                catalog.PrimaryTextureReferenceByBindingKey[bindingKey] = primaryReference;
            }
        }

        if (pendingTexturePaths.Count > 0)
        {
            AssetDatabase.Refresh();
        }

        foreach (var pendingTexture in pendingTexturePaths)
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(pendingTexture.Value);
            if (texture == null)
            {
                log($"[StaticMesh/Textures] Failed to load texture asset after refresh: {pendingTexture.Key} -> {pendingTexture.Value}");
                continue;
            }

            catalog.TexturesByReference[pendingTexture.Key] = texture;
        }

        foreach (var flipbookPair in flipbookReferencesByBindingKey)
        {
            var frames = flipbookPair.Value
                .Where(reference => !string.IsNullOrWhiteSpace(reference) && catalog.TexturesByReference.TryGetValue(reference, out _))
                .Select(reference => catalog.TexturesByReference[reference])
                .Where(texture => texture != null)
                .ToArray();
            if (frames.Length > 1)
            {
                catalog.FlipbooksByBindingKey[flipbookPair.Key] = frames;
            }
        }

        return catalog;
    }

    private static string[] ImportFlipbookFrames(
        SceneStaticMeshSubMeshDefinition subMesh,
        string bindingKey,
        string mapKey,
        string textureDir,
        MaterialKnownTraits traits,
        bool reuseExistingMaterialTextureAssets,
        StaticMeshTextureCatalog catalog,
        IDictionary<string, string> pendingTexturePaths,
        System.Action<string> log)
    {
        var flipbookTraits = subMesh.Material == null ? null : MaterialHeuristics.GetKnownTraits(subMesh.Material);
        if (subMesh.Material?.TextureSlots == null || flipbookTraits == null || !flipbookTraits.HasAnimatedTextureFlipbookHint)
        {
            return null;
        }

        var frameReferences = new List<string>();
        foreach (var slot in subMesh.Material.TextureSlots)
        {
            if (slot.Texture == null)
            {
                continue;
            }

            var reference = slot.Reference ?? slot.ObjectName ?? $"Flipbook_{bindingKey}";
            if (PrepareTextureAsset(
                    reference,
                    slot.Texture,
                    mapKey,
                    textureDir,
                    traits,
                    reuseExistingMaterialTextureAssets,
                    catalog,
                    pendingTexturePaths,
                    log) != null)
            {
                frameReferences.Add(reference);
            }
        }

        if (frameReferences.Count > 0)
        {
            if (!catalog.PrimaryTextureReferenceByBindingKey.ContainsKey(bindingKey))
            {
                var firstSlot = subMesh.Material.TextureSlots[0];
                catalog.PrimaryTextureReferenceByBindingKey[bindingKey] = firstSlot.Reference ?? firstSlot.ObjectName ?? $"Flipbook_{bindingKey}";
            }
        }

        return frameReferences.Count == 0 ? null : frameReferences.ToArray();
    }

    private static string PrepareTextureAsset(
        string textureReference,
        TextureData textureData,
        string mapKey,
        string textureDir,
        MaterialKnownTraits traits,
        bool reuseExistingMaterialTextureAssets,
        StaticMeshTextureCatalog catalog,
        IDictionary<string, string> pendingTexturePaths,
        System.Action<string> log)
    {
        var cacheKey = textureReference ?? (textureData?.Name ?? "Texture");
        if (catalog.TexturesByReference.TryGetValue(cacheKey, out var cached))
        {
            return cacheKey;
        }

        if (pendingTexturePaths.ContainsKey(cacheKey))
        {
            return cacheKey;
        }

        var needsRefresh = ImportedTextureAssetUtility.PrepareTextureAssetFile(
            textureReference,
            textureData,
            textureDir,
            $"{mapKey}/StaticMeshTextures",
            traits,
            reuseExistingMaterialTextureAssets,
            out var texturePath,
            out var texture);
        if (texture != null)
        {
            catalog.TexturesByReference[cacheKey] = texture;
            return cacheKey;
        }

        if (needsRefresh)
        {
            pendingTexturePaths[cacheKey] = texturePath;
            return cacheKey;
        }

        if (textureData != null)
        {
            log($"[StaticMesh/Textures] Failed to create texture asset: {textureReference} -> {texturePath}");
        }

        return null;
    }
}
