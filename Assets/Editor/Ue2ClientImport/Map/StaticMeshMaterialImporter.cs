using System.Collections.Generic;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using UnityEditor;
using UnityEngine;

internal static class StaticMeshMaterialImporter
{
    public static StaticMeshMaterialCatalog ImportMaterials(
        IReadOnlyDictionary<string, SceneStaticMeshDefinition> meshDefinitions,
        string mapKey,
        string materialDir,
        Shader shader,
        StaticMeshTextureCatalog textureCatalog,
        bool reuseExistingMaterialTextureAssets)
    {
        var catalog = new StaticMeshMaterialCatalog();
        var materialCache = new Dictionary<string, Material>(System.StringComparer.OrdinalIgnoreCase);

        UnityAssetDatabaseUtility.RunAssetEditingBatch(() =>
        {
            foreach (var pair in meshDefinitions)
            {
                var meshReference = pair.Key;
                var meshDefinition = pair.Value;
                var materialIds = StaticMeshImportUtility.CollectMaterialIds(meshDefinition.RenderGeometry);
                var meshMaterials = new Material[materialIds.Count];
                var meshFlipbooks = new Texture2D[materialIds.Count][];

                for (var i = 0; i < materialIds.Count; i++)
                {
                    var materialId = materialIds[i];
                    var subMesh = meshDefinition.SubMeshes == null
                        ? null
                        : System.Linq.Enumerable.FirstOrDefault(meshDefinition.SubMeshes, item => item.MaterialId == materialId);
                    var bindingKey = StaticMeshImportUtility.BuildBindingKey(meshReference, materialId);
                    textureCatalog.TraitsByBindingKey.TryGetValue(bindingKey, out var traits);
                    textureCatalog.PrimaryTextureReferenceByBindingKey.TryGetValue(bindingKey, out var textureReference);
                    var texture = ResolvePrimaryTexture(textureReference, textureCatalog);
                    var blendHint = traits?.BlendModeHint.ToString() ?? "Opaque";
                    var materialKey = $"{textureReference ?? $"Tex_Mat{materialId}"}_{blendHint}";

                    if (!materialCache.TryGetValue(materialKey, out var material))
                    {
                        var materialPath = L2AssetManager.BuildClientPackageAssetPath(
                            materialDir,
                            textureReference,
                            "MAT",
                            "mat",
                            $"{mapKey}/StaticMeshMaterials",
                            blendHint);

                        material = reuseExistingMaterialTextureAssets
                            ? AssetDatabase.LoadAssetAtPath<Material>(materialPath)
                            : null;

                        if (material == null)
                        {
                            material = new Material(shader);
                            if (texture != null)
                            {
                                L2MaterialUtility.AssignMainTexture(material, texture);
                            }

                            if (traits != null)
                            {
                                L2AssetManager.ApplyMaterialTraits(material, traits, L2MaterialUtility.IsHdrp(shader));
                            }

                            material = UnityAssetDatabaseUtility.CreateOrReplaceAsset(material, materialPath);
                        }

                        materialCache[materialKey] = material;
                    }

                    meshMaterials[i] = material;
                    textureCatalog.FlipbooksByBindingKey.TryGetValue(bindingKey, out var flipbookFrames);
                    meshFlipbooks[i] = flipbookFrames;
                }

                catalog.MaterialsByMeshReference[meshReference] = meshMaterials;
                catalog.FlipbooksByMeshReference[meshReference] = meshFlipbooks;
            }
        });

        return catalog;
    }

    private static Texture2D ResolvePrimaryTexture(string textureReference, StaticMeshTextureCatalog textureCatalog)
    {
        if (string.IsNullOrWhiteSpace(textureReference))
        {
            return null;
        }

        if (textureCatalog.TexturesByReference.TryGetValue(textureReference, out var texture))
        {
            return texture;
        }

        return null;
    }
}
