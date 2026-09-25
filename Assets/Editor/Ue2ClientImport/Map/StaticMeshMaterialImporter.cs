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
                    var materialReference = ResolveSurfaceReference(subMesh, meshReference, materialId);
                    var materialKey = materialReference ?? "<UnrealNullMaterial>";

                    if (!materialCache.TryGetValue(materialKey, out var material))
                    {
                        var materialPath = materialReference == null
                            ? BuildNullMaterialPath()
                            : L2AssetManager.BuildClientPackageAssetPath(
                                materialDir,
                                materialReference,
                                "MAT",
                                "mat",
                                $"{mapKey}/StaticMeshMaterials");

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

    private static string ResolveSurfaceReference(
        SceneStaticMeshSubMeshDefinition subMesh,
        string meshReference,
        int materialId)
    {
        if (subMesh == null)
        {
            throw new System.InvalidOperationException(
                $"Static mesh '{meshReference}' has geometry for material slot {materialId}, but SceneDomain did not return its submesh description.");
        }

        var materialReference = subMesh.MaterialResource?.Reference ?? subMesh.MaterialReference;
        if (!string.IsNullOrWhiteSpace(materialReference))
        {
            return materialReference;
        }

        if (subMesh.Material?.RootClass == L2Viewer.UtxFile.MaterialGraphRootClass.Texture)
        {
            return subMesh.PrimaryTextureResource?.Reference
                ?? subMesh.PrimaryTextureReference
                ?? throw new System.InvalidOperationException(
                    $"Static mesh '{meshReference}' material slot {materialId} is a Texture root without its own resource reference.");
        }

        if (subMesh.Material == null &&
            subMesh.MaterialResource == null &&
            string.IsNullOrWhiteSpace(subMesh.MaterialReference) &&
            subMesh.PrimaryTextureResource == null &&
            string.IsNullOrWhiteSpace(subMesh.PrimaryTextureReference))
        {
            return null;
        }

        throw new System.InvalidOperationException(
            $"Static mesh '{meshReference}' material slot {materialId} has a material graph without its root resource reference. " +
            $"RootClass={subMesh.Material?.RootClass.ToString() ?? "<null>"}.");
    }

    private static string BuildNullMaterialPath()
    {
        L2AssetManager.EnsureFolderExists(L2AssetManager.ManagedStaticMeshMaterialsRoot);
        return L2AssetManager.BuildAssetPathInFolder(
            L2AssetManager.ManagedStaticMeshMaterialsRoot,
            "MAT",
            "NullMaterial",
            "mat");
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
