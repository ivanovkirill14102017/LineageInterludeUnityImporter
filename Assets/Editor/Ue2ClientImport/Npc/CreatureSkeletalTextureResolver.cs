using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.PackageCore;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Services.MaterialServices;
using L2Viewer.UtxFile;
using UnityEditor;
using UnityEngine;

internal static class CreatureSkeletalTextureResolver
{
    public static void PrimeExistingTextureAssets(
        L2SkeletalCharacterAsset asset,
        string textureDir,
        Dictionary<string, Texture2D> result,
        L2SkeletalAnimatorPrefabBuilder.BuildContext context)
    {
        if (asset == null || result == null)
        {
            return;
        }

        foreach (var binding in asset.MaterialBindings ?? Array.Empty<L2SkeletalMaterialBindingData>())
        {
            TryUseExistingTextureAsset(binding?.TextureReference, textureDir, result, context);
        }

        foreach (var textureRef in asset.UsedTextures ?? Array.Empty<L2SkeletalTextureRefData>())
        {
            TryUseExistingTextureAsset(textureRef?.Reference, textureDir, result, context);
        }

        TryUseExistingTextureAsset(asset.PrimaryTextureReference, textureDir, result, context);
    }

    public static void TryCollectTextureReferencePlan(
        string textureReference,
        L2SkeletalAnimatorPrefabBuilder.BuildContext context,
        HashSet<string> seen,
        List<CreatureSkeletalMaterialImporter.TextureImportPlan> plans)
    {
        if (string.IsNullOrWhiteSpace(textureReference) || context?.TextureManager == null || seen == null || plans == null || !seen.Add(textureReference))
        {
            return;
        }

        if (context.TexturePlanCache.TryGetValue(textureReference, out var cachedPlan))
        {
            plans.Add(cachedPlan);
            return;
        }

        if (!TryParseTextureReference(textureReference, out var packageName, out var objectName))
        {
            return;
        }

        if (!TryResolveTexturePlan(textureReference, packageName, objectName, context, out var plan))
        {
            return;
        }

        context.TexturePlanCache[textureReference] = plan;
        plans.Add(plan);
    }

    public static void TryImportTextureReference(
        string textureReference,
        L2SkeletalAnimatorPrefabBuilder.BuildContext context,
        string textureDir,
        Dictionary<string, Texture2D> result)
    {
        if (string.IsNullOrWhiteSpace(textureReference) || result.ContainsKey(textureReference) || context?.TextureManager == null)
        {
            return;
        }

        if (TryUseExistingTextureAsset(textureReference, textureDir, result, context))
        {
            return;
        }

        if (!TryParseTextureReference(textureReference, out var packageName, out var objectName))
        {
            return;
        }

        if (!TryResolveTexturePlan(textureReference, packageName, objectName, context, out var plan))
        {
            return;
        }

        var texture = ImportedTextureAssetUtility.LoadOrCreateTextureAsset(
            plan.TextureReference,
            plan.TextureData,
            textureDir,
            "SkeletalTextures",
            traits: plan.Traits,
            reuseExisting: true);
        if (texture != null)
        {
            result[textureReference] = texture;
            context.ImportedTextureCache[textureReference] = texture;
        }
    }

    public static bool TryResolveTextureAlias(
        BspTextureManager textureManager,
        string packageName,
        string objectName)
    {
        return TryResolveTextureResource(textureManager, packageName, objectName, out _);
    }

    public static bool TryResolveTextureOrMaterialReference(
        L2SkeletalAnimatorPrefabBuilder.BuildContext context,
        string packageName,
        string objectName)
    {
        if (context == null ||
            string.IsNullOrWhiteSpace(packageName) ||
            string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        return TryResolveTexturePlan(
            $"{packageName}.{objectName}",
            packageName,
            objectName,
            context,
            out _);
    }

    public static ResolvedSkeletalTextureBinding ResolveExactTextureBinding(
        string sourcePackagePath,
        L2SkeletalMaterialBindingData binding,
        SceneMaterialResolver materialResolver,
        BspTextureManager textureManager,
        L2SkeletalAnimatorPrefabBuilder.BuildContext context)
    {
        if (binding == null || string.IsNullOrWhiteSpace(binding.PackageName) || string.IsNullOrWhiteSpace(binding.ObjectName))
        {
            return null;
        }

        var cacheKey = $"{sourcePackagePath}|{binding.PackageName}|{binding.ObjectName}|{binding.TextureReference}";
        if (context != null &&
            context.ExactBindingCache.TryGetValue(cacheKey, out var cachedBinding) &&
            cachedBinding is ResolvedSkeletalTextureBinding typedBinding)
        {
            return typedBinding;
        }

        var directReference = !string.IsNullOrWhiteSpace(binding.TextureReference)
            ? binding.TextureReference
            : $"{binding.PackageName}.{binding.ObjectName}";
        if (!string.IsNullOrWhiteSpace(directReference))
        {
            var resolvedDirectTexture = textureManager.ResolveMany(new[]
            {
                new SceneTextureRequest(binding.PackageName, binding.ObjectName)
            });
            var directKey = $"{binding.PackageName}.{binding.ObjectName}";
            if (resolvedDirectTexture.TryGetValue(directKey, out var directTexture) && directTexture?.Texture != null)
            {
                var directBinding = new ResolvedSkeletalTextureBinding(
                    directReference,
                    binding.ResolvedPackagePath,
                    directTexture.Texture,
                    traits: null);
                if (context != null)
                {
                    context.ExactBindingCache[cacheKey] = directBinding;
                }

                return directBinding;
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

        var resolvedBinding = new ResolvedSkeletalTextureBinding(
            preferredSlot.Reference,
            preferredSlot.PackagePath,
            texture,
            MaterialHeuristics.GetKnownTraits(graph));
        if (context != null)
        {
            context.ExactBindingCache[cacheKey] = resolvedBinding;
        }

        return resolvedBinding;
    }

    public sealed class ResolvedSkeletalTextureBinding
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

    private static bool TryUseExistingTextureAsset(
        string textureReference,
        string textureDir,
        Dictionary<string, Texture2D> result,
        L2SkeletalAnimatorPrefabBuilder.BuildContext context)
    {
        if (string.IsNullOrWhiteSpace(textureReference) || result.ContainsKey(textureReference))
        {
            return false;
        }

        if (context != null &&
            context.ImportedTextureCache.TryGetValue(textureReference, out var cachedTexture) &&
            cachedTexture != null)
        {
            result[textureReference] = cachedTexture;
            return true;
        }

        var texturePath = ImportedTextureAssetUtility.BuildTextureAssetPath(textureDir, textureReference, "SkeletalTextures");
        var existingTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (existingTexture == null)
        {
            return false;
        }

        result[textureReference] = existingTexture;
        context?.ImportedTextureCache.TryAdd(textureReference, existingTexture);
        return true;
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

    private static bool TryResolveTextureResource(
        BspTextureManager textureManager,
        string packageName,
        string objectName,
        out BspTextureManager.ResolvedTexture textureEntry)
    {
        textureEntry = null;
        if (textureManager == null ||
            string.IsNullOrWhiteSpace(packageName) ||
            string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        var candidateNames = EnumerateTextureObjectCandidates(objectName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var resolved = textureManager.ResolveMany(candidateNames.Select(x => new SceneTextureRequest(packageName, x)));
        foreach (var candidateName in candidateNames)
        {
            if (resolved.TryGetValue($"{packageName}.{candidateName}", out var candidate) &&
                candidate?.Texture != null)
            {
                textureEntry = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryResolveTexturePlan(
        string textureReference,
        string packageName,
        string objectName,
        L2SkeletalAnimatorPrefabBuilder.BuildContext context,
        out CreatureSkeletalMaterialImporter.TextureImportPlan plan)
    {
        plan = null;
        if (context?.TextureManager == null ||
            string.IsNullOrWhiteSpace(packageName) ||
            string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        if (TryResolveTextureResource(context.TextureManager, packageName, objectName, out var textureEntry))
        {
            plan = new CreatureSkeletalMaterialImporter.TextureImportPlan(textureReference, textureEntry.Texture, traits: null);
            return true;
        }

        if (!TryResolveMaterialGraphTexture(context, packageName, objectName, out var graphTexture, out var graphTraits))
        {
            return false;
        }

        plan = new CreatureSkeletalMaterialImporter.TextureImportPlan(textureReference, graphTexture, graphTraits);
        return true;
    }

    private static bool TryResolveMaterialGraphTexture(
        L2SkeletalAnimatorPrefabBuilder.BuildContext context,
        string packageName,
        string objectName,
        out TextureData texture,
        out MaterialKnownTraits traits)
    {
        texture = null;
        traits = null;
        if (context?.MaterialResolver == null ||
            context.TextureManager == null ||
            string.IsNullOrWhiteSpace(packageName) ||
            string.IsNullOrWhiteSpace(objectName))
        {
            return false;
        }

        var graph = context.MaterialResolver.ResolveMany(
                context.ClientRoot,
                new[]
                {
                    new SceneMaterialRequest(packageName, objectName)
                })
            .Values
            .FirstOrDefault();
        if (graph == null)
        {
            return false;
        }

        var preferredSlot = MaterialTextureSlotOrdering.GetPreferredTextureSlot(graph.TextureSlots);
        if (preferredSlot == null)
        {
            return false;
        }

        texture = preferredSlot.Texture;
        if (texture == null &&
            !string.IsNullOrWhiteSpace(preferredSlot.PackageName) &&
            !string.IsNullOrWhiteSpace(preferredSlot.ObjectName) &&
            TryResolveTextureResource(context.TextureManager, preferredSlot.PackageName, preferredSlot.ObjectName, out var resolvedTexture))
        {
            texture = resolvedTexture.Texture;
        }

        if (texture == null)
        {
            return false;
        }

        traits = MaterialHeuristics.GetKnownTraits(graph);
        return true;
    }

    private static IEnumerable<string> EnumerateTextureObjectCandidates(string objectName)
    {
        if (string.IsNullOrWhiteSpace(objectName))
        {
            yield break;
        }

        yield return objectName;

        if (objectName.EndsWith("_ah", StringComparison.OrdinalIgnoreCase))
        {
            yield return objectName + "_ori";
        }

        if (objectName.EndsWith("_bh", StringComparison.OrdinalIgnoreCase))
        {
            var stemWithoutBh = objectName.Substring(0, objectName.Length - 3);
            yield return stemWithoutBh + "_ah";
            yield return stemWithoutBh + "_ah_ori";
        }

        var suffixSeparator = objectName.LastIndexOf('_');
        if (suffixSeparator <= 0 || suffixSeparator >= objectName.Length - 1)
        {
            yield break;
        }

        var stem = objectName.Substring(0, suffixSeparator);
        var suffix = objectName.Substring(suffixSeparator + 1);
        foreach (var alternateSuffix in EnumerateAlternateSuffixes(suffix))
        {
            yield return $"{stem}_{alternateSuffix}";
        }
    }

    private static IEnumerable<string> EnumerateAlternateSuffixes(string suffix)
    {
        switch ((suffix ?? string.Empty).ToLowerInvariant())
        {
            case "u":
                yield return "g";
                yield return "b";
                yield return "l";
                break;
            case "g":
                yield return "u";
                yield return "b";
                yield return "l";
                break;
            case "b":
                yield return "g";
                yield return "u";
                yield return "l";
                break;
            case "l":
                yield return "u";
                yield return "g";
                yield return "b";
                break;
        }
    }
}
