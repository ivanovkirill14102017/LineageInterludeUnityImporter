using System.IO;
using L2Viewer.PackageCore;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Services.MaterialServices;
using UnityEditor;
using UnityEngine;

internal static class ImportedTextureAssetUtility
{
    public static string BuildTextureAssetPath(string textureDir, string textureReference, string fallbackCategory)
    {
        return L2AssetManager.BuildClientPackageAssetPath(
            textureDir,
            textureReference,
            "TEX",
            "png",
            fallbackCategory);
    }

    public static Texture2D LoadOrCreateTextureAsset(
        string textureReference,
        TextureData textureData,
        string textureDir,
        string fallbackCategory,
        MaterialKnownTraits traits,
        bool reuseExisting)
    {
        var texturePath = BuildTextureAssetPath(textureDir, textureReference, fallbackCategory);
        var texture = reuseExisting
            ? AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath)
            : null;

        if (texture == null && textureData != null)
        {
            texture = L2AssetManager.CreateTextureAsset(
                textureData,
                texturePath,
                false,
                StaticMeshImportUtility.NeedsAlpha(traits));
        }

        return texture;
    }

    public static bool PrepareTextureAssetFile(
        string textureReference,
        TextureData textureData,
        string textureDir,
        string fallbackCategory,
        MaterialKnownTraits traits,
        bool reuseExisting,
        out string texturePath,
        out Texture2D loadedTexture)
    {
        texturePath = BuildTextureAssetPath(textureDir, textureReference, fallbackCategory);
        loadedTexture = reuseExisting
            ? AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath)
            : null;
        if (loadedTexture != null)
        {
            return false;
        }

        if (textureData == null)
        {
            return File.Exists(texturePath);
        }

        if (!reuseExisting || !File.Exists(texturePath))
        {
            L2AssetManager.WriteTextureAssetFile(
                textureData,
                texturePath,
                false,
                StaticMeshImportUtility.NeedsAlpha(traits));
        }

        return true;
    }
}
