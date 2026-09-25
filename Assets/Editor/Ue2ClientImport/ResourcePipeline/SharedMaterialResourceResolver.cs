using System;
using System.Collections.Generic;
using L2Viewer.SceneDomain.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

internal static class SharedMaterialResourceResolver
{
    public static Material ResolveMissingTextureMaterial()
    {
        var materialPath = $"{L2AssetManager.ManagedFallbackMaterialsRoot}/MAT_MissingTexture.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (existing != null)
        {
            return existing;
        }

        var shader = (StaticMeshImportUtility.FindDefaultShader() ?? Shader.Find("Standard"))!;
        var material = new Material(shader)
        {
            color = Color.magenta,
            enableInstancing = true
        };
        L2MaterialUtility.SetBaseColor(material, Color.magenta);
        if (material.HasProperty("_Smoothness"))
        {
            material.SetFloat("_Smoothness", 0.25f);
        }

        if (material.HasProperty("_Glossiness"))
        {
            material.SetFloat("_Glossiness", 0.25f);
        }

        return UnityAssetDatabaseUtility.CreateAssetIfMissing(material, materialPath);
    }

    public static string BuildAssetPath(MaterialResourceId id)
    {
        return L2AssetManager.BuildClientPackageAssetPath(
            L2AssetManager.SharedMaterialsRoot,
            id.Surface.ToString(),
            "MAT",
            "mat",
            "ResourceMaterials");
    }

    public static void Resolve(
        IReadOnlyCollection<MaterialResourceRequest> materialRequests,
        IResourceImportProgress progress)
    {
        var shader = (StaticMeshImportUtility.FindDefaultShader() ?? Shader.Find("Standard"))!;
        var requests = new List<MaterialResourceRequest>(materialRequests);

        UnityAssetDatabaseUtility.RunAssetEditingBatch(() =>
        {
            for (var index = 0; index < requests.Count; index++)
            {
                var request = requests[index];
                progress.ReportItem("Materials", request.Id, index, requests.Count, 0.38f, 0.48f);
                var texture = request.TextureReference == null
                    ? null
                    : AssetDatabase.LoadAssetAtPath<Texture2D>(
                        SharedTextureResourceResolver.BuildAssetPath(request.TextureReference));

                var materialPath = BuildAssetPath(request.Id);
                var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                if (material == null)
                {
                    material = new Material(shader)
                    {
                        color = texture != null ? Color.white : Color.magenta,
                        enableInstancing = true
                    };
                    if (texture != null)
                    {
                        L2MaterialUtility.AssignMainTexture(material, texture);
                    }

                    L2MaterialUtility.SetBaseColor(material, texture != null ? Color.white : Color.magenta);
                    if (material.HasProperty("_Smoothness"))
                    {
                        material.SetFloat("_Smoothness", 0.25f);
                    }

                    if (material.HasProperty("_Glossiness"))
                    {
                        material.SetFloat("_Glossiness", 0.25f);
                    }

                    ConfigureMaterial(material, request);
                    material = UnityAssetDatabaseUtility.CreateAssetIfMissing(material, materialPath);
                }

            }
        });
    }

    private static void ConfigureMaterial(
        Material material,
        MaterialResourceRequest request)
    {
        switch (request.BlendMode)
        {
            case SceneMaterialBlendMode.Opaque:
                break;
            case SceneMaterialBlendMode.AlphaBlend:
                L2MaterialUtility.ConfigureTransparent(material, BlendMode.SrcAlpha, BlendMode.OneMinusSrcAlpha, false);
                break;
            case SceneMaterialBlendMode.Modulated:
                L2MaterialUtility.ConfigureTransparent(material, BlendMode.DstColor, BlendMode.Zero, false, true);
                break;
            case SceneMaterialBlendMode.Additive:
                L2MaterialUtility.ConfigureTransparent(material, BlendMode.One, BlendMode.One, false);
                break;
            case SceneMaterialBlendMode.AlphaModulate:
                L2MaterialUtility.ConfigureTransparent(material, BlendMode.DstColor, BlendMode.OneMinusSrcAlpha, false, true);
                break;
            case SceneMaterialBlendMode.Darken:
                L2MaterialUtility.ConfigureTransparent(material, BlendMode.Zero, BlendMode.OneMinusSrcColor, false);
                break;
            default:
                throw new InvalidOperationException(
                    $"Unsupported material blend mode '{request.BlendMode}' for material '{request.Id}'.");
        }
    }
}
