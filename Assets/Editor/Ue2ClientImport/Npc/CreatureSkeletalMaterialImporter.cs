using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using L2Viewer.PackageCore;
using L2Viewer.SceneDomain.Services;
using L2Viewer.UtxFile;
using UnityEditor;
using UnityEngine;

internal static class CreatureSkeletalMaterialImporter
{
    public static Material[] CreateMaterials(
        L2SkeletalCharacterAsset asset,
        string referenceText,
        string assetRoot,
        Action<string> log,
        L2SkeletalAnimatorPrefabBuilder.BuildContext context)
    {
        var textures = ImportTextures(asset, referenceText, log, context);
        var shader = StaticMeshImportUtility.FindDefaultShader() ?? Shader.Find("Standard");
        var errorShader = Shader.Find("Hidden/InternalErrorShader");
        var materialIds = CreatureSkeletalImportUtility.GetMaterialIds(asset);
        var materials = new Material[materialIds.Length];

        for (var i = 0; i < materialIds.Length; i++)
        {
            var materialId = materialIds[i];
            var materialPath = L2AssetManager.BuildClientPackageAssetPath(
                assetRoot,
                $"{referenceText}.Material{materialId:00}",
                "MAT",
                "mat",
                "SkeletalMaterials");
            var material = new Material(shader)
            {
                name = Path.GetFileNameWithoutExtension(materialPath),
                color = Color.white
            };

            var subMeshTexture = ResolveTextureForMaterial(asset, materialId, textures);
            if (subMeshTexture != null)
            {
                L2MaterialUtility.AssignMainTexture(material, subMeshTexture);
                L2MaterialUtility.SetBaseColor(material, Color.white);
            }
            else
            {
                log?.Invoke(
                    $"[CreatureAnimator] Missing texture for skeletal material slot {materialId} on '{asset.CharacterName}'. " +
                    "Using visible error material to keep map creature placement unblocked.");
                if (errorShader != null)
                {
                    material.shader = errorShader;
                }
            }

            materials[i] = UnityAssetDatabaseUtility.CreateOrReplaceAsset(material, materialPath);
        }

        return materials;
    }

    private static Dictionary<string, Texture2D> ImportTextures(
        L2SkeletalCharacterAsset asset,
        string referenceText,
        Action<string> log,
        L2SkeletalAnimatorPrefabBuilder.BuildContext context)
    {
        var result = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        if (asset == null || context == null || string.IsNullOrWhiteSpace(context.ClientRoot))
        {
            return result;
        }

        var textureDir = L2AssetManager.SharedTexturesRoot;
        L2AssetManager.EnsureFolderExists(textureDir);

        var bindings = asset.MaterialBindings ?? Array.Empty<L2SkeletalMaterialBindingData>();
        if (bindings.Length == 0)
        {
            log?.Invoke($"[SkinnedPOC] No skeletal material bindings were found for {referenceText}.");
        }

        foreach (var binding in bindings.Where(x => x != null))
        {
            if (string.IsNullOrWhiteSpace(binding.PackageName) || string.IsNullOrWhiteSpace(binding.ObjectName))
            {
                continue;
            }

            var resolvedTexture = ResolveExactTextureBinding(
                asset.SourcePackagePath,
                binding,
                context.MaterialResolver,
                context.TextureManager);
            if (resolvedTexture == null || resolvedTexture.Texture == null || string.IsNullOrWhiteSpace(resolvedTexture.Reference))
            {
                continue;
            }

            binding.TextureReference = resolvedTexture.Reference;
            binding.ResolvedPackagePath = resolvedTexture.ResolvedPackagePath ?? string.Empty;
            if (string.IsNullOrWhiteSpace(asset.PrimaryTextureReference))
            {
                asset.PrimaryTextureReference = resolvedTexture.Reference;
            }

            if (result.ContainsKey(resolvedTexture.Reference))
            {
                continue;
            }

            var texture = ImportedTextureAssetUtility.LoadOrCreateTextureAsset(
                resolvedTexture.Reference,
                resolvedTexture.Texture,
                textureDir,
                "SkeletalTextures",
                resolvedTexture.Traits,
                reuseExisting: true);
            if (texture != null)
            {
                result[resolvedTexture.Reference] = texture;
            }
        }

        foreach (var textureRef in asset.UsedTextures ?? Array.Empty<L2SkeletalTextureRefData>())
        {
            TryImportTextureReference(textureRef?.Reference, context.TextureManager, textureDir, result);
        }

        if (!string.IsNullOrWhiteSpace(asset.PrimaryTextureReference))
        {
            TryImportTextureReference(asset.PrimaryTextureReference, context.TextureManager, textureDir, result);
        }

        return result;
    }

    private static Texture2D ResolveTextureForMaterial(
        L2SkeletalCharacterAsset asset,
        int materialId,
        Dictionary<string, Texture2D> textures)
    {
        if (asset == null || textures == null || textures.Count == 0)
        {
            return null;
        }

        if (asset.MaterialBindings != null)
        {
            var binding = asset.MaterialBindings.FirstOrDefault(
                x => x != null && x.MaterialId == materialId && !string.IsNullOrWhiteSpace(x.TextureReference));
            if (binding != null && textures.TryGetValue(binding.TextureReference, out var boundTexture))
            {
                return boundTexture;
            }
        }

        foreach (var textureRef in asset.UsedTextures ?? Array.Empty<L2SkeletalTextureRefData>())
        {
            if (textureRef != null &&
                !string.IsNullOrWhiteSpace(textureRef.Reference) &&
                textures.TryGetValue(textureRef.Reference, out var usedTexture))
            {
                return usedTexture;
            }
        }

        if (!string.IsNullOrWhiteSpace(asset.PrimaryTextureReference) &&
            textures.TryGetValue(asset.PrimaryTextureReference, out var primaryTexture))
        {
            return primaryTexture;
        }

        return null;
    }

    private static void TryImportTextureReference(
        string textureReference,
        BspTextureManager textureManager,
        string textureDir,
        Dictionary<string, Texture2D> result)
    {
        if (string.IsNullOrWhiteSpace(textureReference) || result.ContainsKey(textureReference) || textureManager == null)
        {
            return;
        }

        var texturePath = ImportedTextureAssetUtility.BuildTextureAssetPath(textureDir, textureReference, "SkeletalTextures");
        var existingTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (existingTexture != null)
        {
            result[textureReference] = existingTexture;
            return;
        }

        if (!TryParseTextureReference(textureReference, out var packageName, out var objectName))
        {
            return;
        }

        var resolvedTexture = textureManager.ResolveMany(new[]
        {
            new SceneTextureRequest(packageName, objectName)
        });
        if (!resolvedTexture.TryGetValue(textureReference, out var textureEntry) || textureEntry?.Texture == null)
        {
            var directKey = $"{packageName}.{objectName}";
            if (!resolvedTexture.TryGetValue(directKey, out textureEntry) || textureEntry?.Texture == null)
            {
                return;
            }
        }

        var texture = ImportedTextureAssetUtility.LoadOrCreateTextureAsset(
            textureReference,
            textureEntry.Texture,
            textureDir,
            "SkeletalTextures",
            traits: null,
            reuseExisting: true);
        if (texture != null)
        {
            result[textureReference] = texture;
        }
    }

    private static bool TryParseTextureReference(string textureReference, out string packageName, out string objectName)
    {
        packageName = null;
        objectName = null;
        if (string.IsNullOrWhiteSpace(textureReference))
        {
            return false;
        }

        var separatorIndex = textureReference.LastIndexOf('.');
        if (separatorIndex <= 0 || separatorIndex >= textureReference.Length - 1)
        {
            return false;
        }

        packageName = textureReference.Substring(0, separatorIndex);
        objectName = textureReference.Substring(separatorIndex + 1);
        return !string.IsNullOrWhiteSpace(packageName) && !string.IsNullOrWhiteSpace(objectName);
    }

    private static ResolvedSkeletalTextureBinding ResolveExactTextureBinding(
        string sourcePackagePath,
        L2SkeletalMaterialBindingData binding,
        SceneMaterialResolver materialResolver,
        BspTextureManager textureManager)
    {
        if (binding == null || string.IsNullOrWhiteSpace(binding.PackageName) || string.IsNullOrWhiteSpace(binding.ObjectName))
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(binding.TextureReference))
        {
            var resolvedDirectTexture = textureManager.ResolveMany(new[]
            {
                new SceneTextureRequest(binding.PackageName, binding.ObjectName)
            });
            var directKey = $"{binding.PackageName}.{binding.ObjectName}";
            if (resolvedDirectTexture.TryGetValue(directKey, out var directTexture) && directTexture?.Texture != null)
            {
                return new ResolvedSkeletalTextureBinding(
                    binding.TextureReference,
                    binding.ResolvedPackagePath,
                    directTexture.Texture,
                    traits: null);
            }
        }

        var graph = materialResolver.ResolveMany(
                sourcePackagePath,
                new[]
                {
                    new SceneMaterialRequest(binding.PackageName, binding.ObjectName)
                })
            .Values
            .FirstOrDefault();
        if (graph == null)
        {
            return null;
        }

        var preferredSlot = MaterialTextureSlotOrdering.GetPreferredTextureSlot(graph.TextureSlots);
        if (preferredSlot == null)
        {
            return null;
        }

        var texture = preferredSlot.Texture;
        if (texture == null)
        {
            var resolvedTexture = textureManager.ResolveMany(new[]
            {
                new SceneTextureRequest(preferredSlot.PackageName, preferredSlot.ObjectName)
            });
            resolvedTexture.TryGetValue(preferredSlot.Reference, out var resolved);
            texture = resolved?.Texture;
        }

        return new ResolvedSkeletalTextureBinding(
            preferredSlot.Reference,
            preferredSlot.PackagePath,
            texture,
            MaterialHeuristics.GetKnownTraits(graph));
    }
    private sealed class ResolvedSkeletalTextureBinding
    {
        public ResolvedSkeletalTextureBinding(string reference, string resolvedPackagePath, TextureData texture, MaterialKnownTraits traits)
        {
            Reference = reference;
            ResolvedPackagePath = resolvedPackagePath;
            Texture = texture;
            Traits = traits;
        }

        public string Reference { get; }
        public string ResolvedPackagePath { get; }
        public TextureData Texture { get; }
        public MaterialKnownTraits Traits { get; }
    }
}
