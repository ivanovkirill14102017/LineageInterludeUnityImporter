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
        var textures = ImportTextures(asset, referenceText, log, context);
        var shader = StaticMeshImportUtility.FindDefaultShader() ?? Shader.Find("Standard");
        var errorShader = Shader.Find("Hidden/InternalErrorShader");
        var materialIds = CreatureSkeletalImportUtility.GetMaterialIds(asset);
        var materials = new Material[materialIds.Length];
        var importedPlans = CollectTextureImportPlans(asset, referenceText, log, context);
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

            materials[i] = UnityAssetDatabaseUtility.CreateOrReplaceAsset(material, materialPath);
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

            if (seen.Add(resolvedTexture.Reference))
            {
                plans.Add(new TextureImportPlan(resolvedTexture.Reference, resolvedTexture.Texture, resolvedTexture.Traits));
            }
        }

        foreach (var textureRef in asset.UsedTextures ?? Array.Empty<L2SkeletalTextureRefData>())
        {
            TryCollectTextureReferencePlan(textureRef?.Reference, context.TextureManager, seen, plans);
        }

        if (!string.IsNullOrWhiteSpace(asset.PrimaryTextureReference))
        {
            TryCollectTextureReferencePlan(asset.PrimaryTextureReference, context.TextureManager, seen, plans);
        }

        EditorUtility.SetDirty(asset);
        return plans;
    }

    public static void ImportTexturePlansBatch(
        IReadOnlyCollection<TextureImportPlan> plans,
        string textureDir)
    {
        if (plans == null || plans.Count == 0)
        {
            return;
        }

        L2AssetManager.EnsureFolderExists(textureDir);
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

            L2AssetManager.WriteTextureAssetFile(
                plan.TextureData,
                texturePath,
                false,
                StaticMeshImportUtility.NeedsAlpha(plan.Traits));
            AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }
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

        PrimeExistingTextureAssets(asset, textureDir, result);
        var plans = CollectTextureImportPlans(asset, referenceText, log, context);
        ImportTexturePlansBatch(plans, textureDir);
        PrimeExistingTextureAssets(asset, textureDir, result);

        foreach (var textureRef in asset.UsedTextures ?? Array.Empty<L2SkeletalTextureRefData>())
        {
            TryImportTextureReference(textureRef?.Reference, context.TextureManager, textureDir, result);
        }

        if (!string.IsNullOrWhiteSpace(asset.PrimaryTextureReference))
        {
            TryImportTextureReference(asset.PrimaryTextureReference, context.TextureManager, textureDir, result);
        }

        PrimeExistingTextureAssets(asset, textureDir, result);

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

    private static void PrimeExistingTextureAssets(
        L2SkeletalCharacterAsset asset,
        string textureDir,
        Dictionary<string, Texture2D> result)
    {
        if (asset == null || result == null)
        {
            return;
        }

        foreach (var binding in asset.MaterialBindings ?? Array.Empty<L2SkeletalMaterialBindingData>())
        {
            TryUseExistingTextureAsset(binding?.TextureReference, textureDir, result);
        }

        foreach (var textureRef in asset.UsedTextures ?? Array.Empty<L2SkeletalTextureRefData>())
        {
            TryUseExistingTextureAsset(textureRef?.Reference, textureDir, result);
        }

        TryUseExistingTextureAsset(asset.PrimaryTextureReference, textureDir, result);
    }

    private static void TryCollectTextureReferencePlan(
        string textureReference,
        BspTextureManager textureManager,
        HashSet<string> seen,
        List<TextureImportPlan> plans)
    {
        if (string.IsNullOrWhiteSpace(textureReference) || textureManager == null || seen == null || plans == null || !seen.Add(textureReference))
        {
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

        plans.Add(new TextureImportPlan(textureReference, textureEntry.Texture, traits: null));
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

        if (TryUseExistingTextureAsset(textureReference, textureDir, result))
        {
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

    private static bool TryUseExistingTextureAsset(
        string textureReference,
        string textureDir,
        Dictionary<string, Texture2D> result)
    {
        if (string.IsNullOrWhiteSpace(textureReference) || result.ContainsKey(textureReference))
        {
            return false;
        }

        var texturePath = ImportedTextureAssetUtility.BuildTextureAssetPath(textureDir, textureReference, "SkeletalTextures");
        var existingTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);
        if (existingTexture == null)
        {
            return false;
        }

        result[textureReference] = existingTexture;
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
                return new ResolvedSkeletalTextureBinding(
                    directReference,
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
