using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using L2Viewer.PackageCore;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Services.MaterialServices;
using L2Viewer.UtxFile;
using UnityEditor;
using UnityEngine;

internal static class CreatureSkeletalMaterialImporter
{
    public sealed class TextureImportPlan
    {
        public TextureImportPlan(string textureReference, TextureData textureData, MaterialKnownTraits traits)
        {
            TextureReference = textureReference;
            TextureData = textureData;
            Traits = traits;
        }

        public string TextureReference { get; }
        public TextureData TextureData { get; }
        public MaterialKnownTraits Traits { get; }
    }

    public static Material[] CreateMaterials(
        L2SkeletalCharacterAsset asset,
        string referenceText,
        string assetRoot,
        Action<string> log,
        L2SkeletalAnimatorPrefabBuilder.BuildContext context)
    {
        var importedPlans = CollectTextureImportPlans(asset, referenceText, log, context);
        var textures = ImportTextures(asset, importedPlans, context);
        var shader = StaticMeshImportUtility.FindDefaultShader() ?? Shader.Find("Standard");
        var errorShader = Shader.Find("Hidden/InternalErrorShader");
        var materialIds = CreatureSkeletalImportUtility.GetMaterialIds(asset);
        var materials = new Material[materialIds.Length];
        var traitsByReference = importedPlans
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.TextureReference) && x.Traits != null)
            .GroupBy(x => x.TextureReference, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Traits, StringComparer.OrdinalIgnoreCase);

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
            if (subMeshTexture == null &&
                TryInferTextureForMaterial(asset, materialId, context.TextureManager, out var inferredReference, out subMeshTexture))
            {
                textures[inferredReference] = subMeshTexture;
            }

            if (subMeshTexture != null)
            {
                L2MaterialUtility.AssignMainTexture(material, subMeshTexture);
                L2MaterialUtility.SetBaseColor(material, Color.white);
                var traits = ResolveTraitsForMaterial(asset, materialId, traitsByReference);
                if (traits != null)
                {
                    L2AssetManager.ApplyMaterialTraits(material, traits, L2MaterialUtility.IsHdrp(shader));
                }
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

            materials[i] = UnityAssetDatabaseUtility.CreateAssetIfMissing(material, materialPath);
        }

        return materials;
    }

    public static List<TextureImportPlan> CollectTextureImportPlans(
        L2SkeletalCharacterAsset asset,
        string referenceText,
        Action<string> log,
        L2SkeletalAnimatorPrefabBuilder.BuildContext context)
    {
        var plans = new List<TextureImportPlan>();
        if (asset == null || context == null || string.IsNullOrWhiteSpace(context.ClientRoot))
        {
            return plans;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var bindings = asset.MaterialBindings ?? Array.Empty<L2SkeletalMaterialBindingData>();
        if (bindings.Length == 0)
        {
            log?.Invoke($"[SkinnedPOC] No skeletal material bindings were found for {referenceText}.");
        }

        foreach (var binding in bindings.Where(x => x != null))
        {
            if (!string.IsNullOrWhiteSpace(binding.TextureReference))
            {
                if (string.IsNullOrWhiteSpace(asset.PrimaryTextureReference))
                {
                    asset.PrimaryTextureReference = binding.TextureReference;
                }

                CreatureSkeletalTextureResolver.TryCollectTextureReferencePlan(binding.TextureReference, context, seen, plans);
                continue;
            }

            if (string.IsNullOrWhiteSpace(binding.PackageName) || string.IsNullOrWhiteSpace(binding.ObjectName))
            {
                continue;
            }

            var resolvedTexture = CreatureSkeletalTextureResolver.ResolveExactTextureBinding(
                asset.SourcePackagePath,
                binding,
                context.MaterialResolver,
                context.TextureManager,
                context);
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

            if (seen.Add(resolvedTexture.Reference))
            {
                plans.Add(new TextureImportPlan(resolvedTexture.Reference, resolvedTexture.Texture, resolvedTexture.Traits));
            }
        }

        foreach (var textureRef in asset.UsedTextures ?? Array.Empty<L2SkeletalTextureRefData>())
        {
            CreatureSkeletalTextureResolver.TryCollectTextureReferencePlan(textureRef?.Reference, context, seen, plans);
        }

        if (!string.IsNullOrWhiteSpace(asset.PrimaryTextureReference))
        {
            CreatureSkeletalTextureResolver.TryCollectTextureReferencePlan(asset.PrimaryTextureReference, context, seen, plans);
        }

        EditorUtility.SetDirty(asset);
        return plans;
    }

    public static int ImportTexturePlansBatch(
        IReadOnlyCollection<TextureImportPlan> plans,
        string textureDir)
    {
        if (plans == null || plans.Count == 0)
        {
            return 0;
        }

        L2AssetManager.EnsureFolderExists(textureDir);
        var createdTextureCount = 0;
        foreach (var plan in plans)
        {
            if (plan == null || string.IsNullOrWhiteSpace(plan.TextureReference) || plan.TextureData == null)
            {
                continue;
            }

            var texturePath = ImportedTextureAssetUtility.BuildTextureAssetPath(textureDir, plan.TextureReference, "SkeletalTextures");
            var existingTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
            if (existingTexture != null)
            {
                continue;
            }

            if (!File.Exists(texturePath))
            {
                L2AssetManager.WriteTextureAssetFile(
                    plan.TextureData,
                    texturePath,
                    false,
                    StaticMeshImportUtility.NeedsAlpha(plan.Traits));
            }

            createdTextureCount++;
        }

        return createdTextureCount;
    }

    public static void PreloadTextureReferences(
        IEnumerable<string> textureReferences,
        Action<string> log,
        L2SkeletalAnimatorPrefabBuilder.BuildContext context)
    {
        if (textureReferences == null || context?.TextureManager == null || string.IsNullOrWhiteSpace(context.ClientRoot))
        {
            return;
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var plans = new List<TextureImportPlan>();
        foreach (var textureReference in textureReferences)
        {
            CreatureSkeletalTextureResolver.TryCollectTextureReferencePlan(textureReference, context, seen, plans);
        }

        if (plans.Count == 0)
        {
            return;
        }

        var textureDir = L2AssetManager.SharedTexturesRoot;
        var createdTextureCount = ImportTexturePlansBatch(plans, textureDir);
        if (createdTextureCount > 0)
        {
            AssetDatabase.Refresh();
        }

        var primed = 0;
        var loaded = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        foreach (var textureReference in seen)
        {
            CreatureSkeletalTextureResolver.TryImportTextureReference(textureReference, context, textureDir, loaded);
            if (loaded.ContainsKey(textureReference))
            {
                primed++;
            }
        }

        log?.Invoke($"[TextureBatch] Preloaded {primed} skeletal textures for archetype build.");
    }

    internal static bool TryResolveTextureAlias(
        BspTextureManager textureManager,
        string packageName,
        string objectName)
    {
        return CreatureSkeletalTextureResolver.TryResolveTextureAlias(textureManager, packageName, objectName);
    }

    internal static bool TryResolveTextureOrMaterialReference(
        L2SkeletalAnimatorPrefabBuilder.BuildContext context,
        string packageName,
        string objectName)
    {
        return CreatureSkeletalTextureResolver.TryResolveTextureOrMaterialReference(context, packageName, objectName);
    }

    private static Dictionary<string, Texture2D> ImportTextures(
        L2SkeletalCharacterAsset asset,
        IReadOnlyCollection<TextureImportPlan> plans,
        L2SkeletalAnimatorPrefabBuilder.BuildContext context)
    {
        var result = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        if (asset == null || context == null || string.IsNullOrWhiteSpace(context.ClientRoot))
        {
            return result;
        }

        var textureDir = L2AssetManager.SharedTexturesRoot;
        L2AssetManager.EnsureFolderExists(textureDir);

        CreatureSkeletalTextureResolver.PrimeExistingTextureAssets(asset, textureDir, result, context);
        var createdTextureCount = ImportTexturePlansBatch(plans, textureDir);
        if (createdTextureCount > 0)
        {
            AssetDatabase.Refresh();
        }
        CreatureSkeletalTextureResolver.PrimeExistingTextureAssets(asset, textureDir, result, context);

        foreach (var textureRef in asset.UsedTextures ?? Array.Empty<L2SkeletalTextureRefData>())
        {
            CreatureSkeletalTextureResolver.TryImportTextureReference(textureRef?.Reference, context, textureDir, result);
        }

        if (!string.IsNullOrWhiteSpace(asset.PrimaryTextureReference))
        {
            CreatureSkeletalTextureResolver.TryImportTextureReference(asset.PrimaryTextureReference, context, textureDir, result);
        }

        CreatureSkeletalTextureResolver.PrimeExistingTextureAssets(asset, textureDir, result, context);
        return result;
    }

    private static MaterialKnownTraits ResolveTraitsForMaterial(
        L2SkeletalCharacterAsset asset,
        int materialId,
        IReadOnlyDictionary<string, MaterialKnownTraits> traitsByReference)
    {
        if (asset?.MaterialBindings != null)
        {
            var binding = asset.MaterialBindings.FirstOrDefault(
                x => x != null && x.MaterialId == materialId && !string.IsNullOrWhiteSpace(x.TextureReference));
            if (binding != null &&
                traitsByReference != null &&
                traitsByReference.TryGetValue(binding.TextureReference, out var bindingTraits))
            {
                return bindingTraits;
            }
        }

        foreach (var textureRef in asset?.UsedTextures ?? Array.Empty<L2SkeletalTextureRefData>())
        {
            if (textureRef != null &&
                !string.IsNullOrWhiteSpace(textureRef.Reference) &&
                traitsByReference != null &&
                traitsByReference.TryGetValue(textureRef.Reference, out var usedTraits))
            {
                return usedTraits;
            }
        }

        return null;
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

    private static bool TryInferTextureForMaterial(
        L2SkeletalCharacterAsset asset,
        int materialId,
        BspTextureManager textureManager,
        out string textureReference,
        out Texture2D texture)
    {
        textureReference = null;
        texture = null;
        if (asset == null || textureManager == null)
        {
            return false;
        }

        var meshName = asset.MeshObjectName ?? asset.CharacterName ?? string.Empty;
        var normalizedBase = NormalizeMeshBaseName(meshName);
        if (string.IsNullOrWhiteSpace(normalizedBase))
        {
            return false;
        }

        var pascalBase = ToPascalCase(normalizedBase);
        var oneBasedIndex = materialId + 1;
        var packageCandidates = new[]
        {
            normalizedBase,
            pascalBase,
            $"{normalizedBase}Tex",
            Path.GetFileNameWithoutExtension(asset.SourcePackagePath ?? string.Empty) + "Tex",
            "LineageMonstersTex"
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase);
        var objectCandidates = new[]
        {
            $"{normalizedBase}_t{materialId:00}",
            $"{normalizedBase}_t0{materialId}",
            $"{normalizedBase}_t00",
            $"{normalizedBase}_t01",
            $"{normalizedBase}_t02",
            $"{normalizedBase}{oneBasedIndex:00}",
            $"{pascalBase}{oneBasedIndex:00}",
            $"{meshName}_t{materialId:00}",
            $"{meshName}_t00",
            $"{meshName}{oneBasedIndex:00}"
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

        foreach (var packageName in packageCandidates)
        {
            var resolved = textureManager.ResolveMany(objectCandidates.Select(x => new SceneTextureRequest(packageName, x)));
            foreach (var objectName in objectCandidates)
            {
                var key = $"{packageName}.{objectName}";
                if (!resolved.TryGetValue(key, out var entry) || entry?.Texture == null)
                {
                    continue;
                }

                textureReference = key;
                texture = ImportedTextureAssetUtility.LoadOrCreateTextureAsset(
                    textureReference,
                    entry.Texture,
                    L2AssetManager.SharedTexturesRoot,
                    "SkeletalTextures",
                    traits: null,
                    reuseExisting: true);
                if (texture != null)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string NormalizeMeshBaseName(string meshName)
    {
        if (string.IsNullOrWhiteSpace(meshName))
        {
            return string.Empty;
        }

        return Regex.Replace(meshName.Trim(), "_[mf]\\d\\d$", string.Empty, RegexOptions.IgnoreCase);
    }

    private static string ToPascalCase(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var parts = value
            .Split(new[] { '_', '-', ' ' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Length == 0
                ? string.Empty
                : char.ToUpperInvariant(x[0]) + x.Substring(1).ToLowerInvariant());
        return string.Concat(parts);
    }
}
