using System.Collections.Generic;
using System.Linq;
using L2Viewer.PackageCore;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
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

                var flipbookFrames = ImportFlipbookFrames(subMesh, bindingKey, mapKey, textureDir, traits, reuseExistingMaterialTextureAssets, catalog, log);
                if (flipbookFrames != null && flipbookFrames.Length > 1)
                {
                    catalog.FlipbooksByBindingKey[bindingKey] = flipbookFrames;
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
                    ImportTextureAsset(primaryReference, resolved.Texture, mapKey, textureDir, traits, reuseExistingMaterialTextureAssets, catalog, log);
                }

                catalog.PrimaryTextureReferenceByBindingKey[bindingKey] = primaryReference;
            }
        }

        return catalog;
    }

    private static Texture2D[] ImportFlipbookFrames(
        SceneStaticMeshSubMeshDefinition subMesh,
        string bindingKey,
        string mapKey,
        string textureDir,
        MaterialKnownTraits traits,
        bool reuseExistingMaterialTextureAssets,
        StaticMeshTextureCatalog catalog,
        System.Action<string> log)
    {
        var flipbookTraits = subMesh.Material == null ? null : MaterialHeuristics.GetKnownTraits(subMesh.Material);
        if (subMesh.Material?.TextureSlots == null || flipbookTraits == null || !flipbookTraits.HasAnimatedTextureFlipbookHint)
        {
            return null;
        }

        var frames = new List<Texture2D>();
        foreach (var slot in subMesh.Material.TextureSlots)
        {
            if (slot.Texture == null)
            {
                continue;
            }

            var reference = slot.Reference ?? slot.ObjectName ?? $"Flipbook_{bindingKey}";
            var texture = ImportTextureAsset(reference, slot.Texture, mapKey, textureDir, traits, reuseExistingMaterialTextureAssets, catalog, log);
            if (texture != null)
            {
                frames.Add(texture);
            }
        }

        if (frames.Count > 0)
        {
            if (!catalog.PrimaryTextureReferenceByBindingKey.ContainsKey(bindingKey))
            {
                var firstSlot = subMesh.Material.TextureSlots[0];
                catalog.PrimaryTextureReferenceByBindingKey[bindingKey] = firstSlot.Reference ?? firstSlot.ObjectName ?? $"Flipbook_{bindingKey}";
            }
        }

        return frames.Count == 0 ? null : frames.ToArray();
    }

    private static Texture2D ImportTextureAsset(
        string textureReference,
        TextureData textureData,
        string mapKey,
        string textureDir,
        MaterialKnownTraits traits,
        bool reuseExistingMaterialTextureAssets,
        StaticMeshTextureCatalog catalog,
        System.Action<string> log)
    {
        var cacheKey = textureReference ?? AssetNameUtility.SanitizeName(textureData?.Name ?? "Texture");
        if (catalog.TexturesByReference.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var texturePath = L2AssetManager.BuildClientPackageAssetPath(
            textureDir,
            textureReference,
            "TEX",
            "png",
            $"{mapKey}/StaticMeshTextures");

        var texture = reuseExistingMaterialTextureAssets
            ? AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath)
            : null;

        if (texture == null && textureData != null)
        {
            texture = L2AssetManager.CreateTextureAsset(textureData, texturePath, false, StaticMeshImportUtility.NeedsAlpha(traits));
            if (texture == null)
            {
                log($"[StaticMesh/Textures] Failed to create texture asset: {textureReference} -> {texturePath}");
            }
        }

        if (texture != null)
        {
            catalog.TexturesByReference[cacheKey] = texture;
        }

        return texture;
    }
}
