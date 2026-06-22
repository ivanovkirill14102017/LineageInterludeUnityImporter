using System;
using System.IO;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using UnityEditor;
using UnityEngine;

internal static class TerrainAssetBuilder
{
    private const float UnrealToUnityScale = L2WorldScale.UnrealToUnityScale;

    public static GameObject BuildTerrain(TerrainImportData terrainImport, MapImportRequest request, GameObject mapRoot)
    {
        var terrainData = BuildTerrainData(
            terrainImport,
            request.MapKey,
            request.OutputDir,
            request.TerrainHeightSmooth);
        return TerrainSceneBuilder.CreateTerrainObject(request.ObjectName, terrainImport, terrainData, mapRoot);
    }

    public static TerrainData BuildTerrainData(
        TerrainImportData terrainImport,
        string mapKey,
        string outputDir,
        int terrainHeightSmooth = 0)
    {
        var terrainRootDir = $"{outputDir}/Terrain";
        var terrainMaskPreviewDir = $"{terrainRootDir}/MaskPreviews";
        var terrainHeightPreviewDir = $"{terrainRootDir}/HeightPreviews";

        L2AssetManager.EnsureFolderExists(terrainRootDir);
        L2AssetManager.EnsureFolderExists(terrainMaskPreviewDir);
        L2AssetManager.EnsureFolderExists(terrainHeightPreviewDir);

        var heightmapResolution = TerrainHeightMapBuilder.ToUnityHeightmapResolution(
            terrainImport.HeightWidth,
            terrainImport.HeightHeight);
        var usefulLayers = TerrainAlphaMapBuilder.GetUsefulLayers(terrainImport);
        var heights = TerrainHeightMapBuilder.BuildUnityHeights(terrainImport, heightmapResolution, terrainHeightSmooth);
        SaveHeightPreviewTexture(
            terrainImport.HeightWidth,
            terrainImport.HeightHeight,
            terrainImport.HeightSamples,
            $"{terrainHeightPreviewDir}/{mapKey}_Height_Source.png");
        SaveHeightPreviewTexture(
            heightmapResolution,
            heightmapResolution,
            heights,
            $"{terrainHeightPreviewDir}/{mapKey}_Height_Unity_{heightmapResolution}_S{terrainHeightSmooth}.png");
        var layers = TerrainTextureAssetBuilder.BuildTerrainLayers(terrainImport, usefulLayers, mapKey, terrainMaskPreviewDir);

        var terrainData = new TerrainData();
        var terrainDataPath = $"{terrainRootDir}/{mapKey}_TerrainData.asset";
        AssetDatabase.CreateAsset(terrainData, terrainDataPath);

        terrainData.name = $"{mapKey}_TerrainData";
        terrainData.heightmapResolution = heightmapResolution;
        terrainData.alphamapResolution = terrainImport.Layers.Length > 0
            ? terrainImport.Layers.Max(layer => Mathf.Max(1, layer.MaskWidth))
            : 256;
        terrainData.baseMapResolution = 1024;
        terrainData.size = new Vector3(
            terrainImport.SampleSpacingX * (terrainImport.HeightWidth - 1) * UnrealToUnityScale,
            terrainImport.HeightValueScale * UnrealToUnityScale,
            terrainImport.SampleSpacingY * (terrainImport.HeightHeight - 1) * UnrealToUnityScale);

        terrainData.terrainLayers = layers;
        terrainData.SetHeights(0, 0, heights);
        var alphamaps = TerrainAlphaMapBuilder.BuildAlphamaps(usefulLayers);
        terrainData.SetAlphamaps(0, 0, alphamaps);

        if (terrainImport.QuadVisibilityMask != null && terrainImport.QuadVisibilityMask.Bits != null)
        {
            var holes = BuildUnityHoles(terrainImport.QuadVisibilityMask);
            terrainData.SetHoles(0, 0, holes);
        }

        EditorUtility.SetDirty(terrainData);
        return terrainData;
    }

    public static Vector3 ConvertTerrainPosition(TerrainImportData terrainImport, TerrainData terrainData)
    {
        var raw = terrainImport.WorldMinCorner ?? System.Numerics.Vector3.Zero;
        var baseZ = raw.Z - (terrainImport.HeightValueScale * 0.5f);
        return new Vector3(
            raw.X * UnrealToUnityScale,
            baseZ * UnrealToUnityScale,
            raw.Y * UnrealToUnityScale);
    }

    private static bool[,] BuildUnityHoles(TerrainBitMaskData quadMask)
    {
        var width = Mathf.Max(1, quadMask.Width);
        var height = Mathf.Max(1, quadMask.Height);
        var holes = new bool[height, width];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var index = y * width + x;
                holes[y, x] = index >= 0 &&
                              index < quadMask.Bits.Length &&
                              quadMask.Bits[index];
            }
        }

        return holes;
    }

    private static void SaveHeightPreviewTexture(int width, int height, float[] samples, string assetPath)
    {
        if (samples == null || samples.Length == 0)
        {
            return;
        }

        var texture = new Texture2D(Mathf.Max(1, width), Mathf.Max(1, height), TextureFormat.RGBA32, false, true)
        {
            name = Path.GetFileNameWithoutExtension(assetPath)
        };

        try
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var sample = Mathf.Clamp01(samples[(y * width) + x]);
                    texture.SetPixel(x, y, new Color(sample, sample, sample, 1f));
                }
            }

            texture.Apply(false, false);
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

    private static void SaveHeightPreviewTexture(int width, int height, float[,] samples, string assetPath)
    {
        if (samples == null)
        {
            return;
        }

        var texture = new Texture2D(Mathf.Max(1, width), Mathf.Max(1, height), TextureFormat.RGBA32, false, true)
        {
            name = Path.GetFileNameWithoutExtension(assetPath)
        };

        try
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var sample = Mathf.Clamp01(samples[y, x]);
                    texture.SetPixel(x, y, new Color(sample, sample, sample, 1f));
                }
            }

            texture.Apply(false, false);
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }

}
