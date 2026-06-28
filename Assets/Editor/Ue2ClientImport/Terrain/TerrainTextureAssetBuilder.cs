using System.IO;
using System.Linq;
using L2Viewer.PackageCore;
using L2Viewer.SceneDomain.Models;
using UnityEditor;
using UnityEngine;

internal static class TerrainTextureAssetBuilder
{
    private const float TerrainTextureSizeModifier = 0.15f;
    private const float UnrealToUnityScale = L2WorldScale.UnrealToUnityScale;

    public static TerrainLayer[] BuildTerrainLayers(
        TerrainImportData terrainImport,
        TerrainLayerImportData[] usefulLayers,
        string mapKey,
        string terrainMaskPreviewDir)
    {
        var terrainLayers = new TerrainLayer[usefulLayers.Length];

        for (var index = 0; index < usefulLayers.Length; index++)
        {
            var sourceLayer = usefulLayers[index];
            var referenceText = ResolveTextureReference(sourceLayer, index);
            var diffuseTexturePath = L2AssetManager.BuildClientPackageAssetPath(
                L2AssetManager.SharedTexturesRoot,
                referenceText,
                "TEX",
                "png",
                $"{mapKey}/TerrainTextures",
                "terrain_diffuse");
            var terrainLayerPath = L2AssetManager.BuildClientPackageAssetPath(
                L2AssetManager.SharedTerrainLayersRoot,
                referenceText,
                "TLYR",
                "terrainlayer",
                $"{mapKey}/TerrainLayers",
                "terrain");
            var diffuseTexture = CreateTerrainDiffuseTextureAsset(
                sourceLayer.Texture,
                diffuseTexturePath);
            CreateMaskPreviewTextureAsset(
                sourceLayer,
                $"{terrainMaskPreviewDir}/{BuildTextureAssetName(referenceText, index, "mask")}.png");

            var terrainLayer = new TerrainLayer
            {
                name = BuildLayerName(sourceLayer, index),
                diffuseTexture = diffuseTexture,
                tileSize = new Vector2(
                    terrainImport.SampleSpacingX * sourceLayer.TileSampleSize * UnrealToUnityScale * TerrainTextureSizeModifier,
                    terrainImport.SampleSpacingY * sourceLayer.TileSampleSize * UnrealToUnityScale * TerrainTextureSizeModifier),
                diffuseRemapMax = new Vector4(1f, 1f, 1f, 1f),
                specular = Color.black,
                metallic = 0f,
                smoothness = 0f
            };

            terrainLayer = UnityAssetDatabaseUtility.CreateOrReplaceAsset(terrainLayer, terrainLayerPath);
            EditorUtility.SetDirty(terrainLayer);
            terrainLayers[index] = terrainLayer;
        }

        return terrainLayers;
    }

    private static Texture2D CreateMaskPreviewTextureAsset(TerrainLayerImportData layer, string assetPath)
    {
        var width = Mathf.Max(1, layer.MaskWidth);
        var height = Mathf.Max(1, layer.MaskHeight);
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
        {
            name = Path.GetFileNameWithoutExtension(assetPath)
        };

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sample = SampleMask(layer, x, y);
                texture.SetPixel(x, y, new Color(sample, sample, sample, sample));
            }
        }

        texture.Apply(false, false);

        var pngBytes = texture.EncodeToPNG();
        File.WriteAllBytes(assetPath, pngBytes);
        Object.DestroyImmediate(texture);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        ConfigureTerrainTextureImporter(assetPath, sRgbTexture: false, wrapMode: TextureWrapMode.Clamp);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
    }

    private static Texture2D CreateTerrainDiffuseTextureAsset(TextureData textureData, string assetPath)
    {
        var texture = L2AssetManager.CreateTextureAsset(textureData, assetPath, false);
        ConfigureTerrainTextureImporter(assetPath, sRgbTexture: true, wrapMode: TextureWrapMode.Repeat);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath) ?? texture;
    }

    private static void ConfigureTerrainTextureImporter(string assetPath, bool sRgbTexture, TextureWrapMode wrapMode)
    {
        if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer)
        {
            return;
        }

        importer.textureType = TextureImporterType.Default;
        importer.alphaSource = TextureImporterAlphaSource.FromInput;
        importer.alphaIsTransparency = false;
        importer.sRGBTexture = sRgbTexture;
        importer.wrapMode = wrapMode;
        importer.filterMode = FilterMode.Bilinear;
        importer.mipmapEnabled = true;
        importer.streamingMipmaps = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.SaveAndReimport();
    }

    private static float SampleMask(TerrainLayerImportData layer, int x, int y)
    {
        if (layer.MaskSamples == null || layer.MaskSamples.Length == 0 || layer.MaskWidth <= 0 || layer.MaskHeight <= 0)
        {
            return 0f;
        }

        var clampedX = Mathf.Clamp(x, 0, layer.MaskWidth - 1);
        var clampedY = Mathf.Clamp(y, 0, layer.MaskHeight - 1);
        var index = clampedY * layer.MaskWidth + clampedX;
        return layer.MaskSamples[index];
    }

    private static string BuildLayerName(TerrainLayerImportData layer, int index)
    {
        var rawName = !string.IsNullOrWhiteSpace(layer.Texture?.Name)
            ? layer.Texture.Name
            : !string.IsNullOrWhiteSpace(layer.TextureReference)
                ? layer.TextureReference
                : $"Layer_{index:00}";

        return $"Layer_{index:00}_{rawName}";
    }

    private static string ResolveTextureReference(TerrainLayerImportData layer, int index)
    {
        if (!string.IsNullOrWhiteSpace(layer.TextureReference))
        {
            return layer.TextureReference;
        }

        if (!string.IsNullOrWhiteSpace(layer.Texture?.Name))
        {
            return layer.Texture.Name;
        }

        return $"Layer_{index:00}";
    }

    private static string BuildTextureAssetName(string textureReference, int index, string suffix)
    {
        var name = string.IsNullOrWhiteSpace(textureReference) ? $"Layer_{index:00}" : textureReference;
        return $"TEX_{name}_{suffix}_{index:00}";
    }
}

