using L2Viewer.SceneDomain.Models;
using UnityEngine;

internal static class TerrainHeightMapBuilder
{
    private const int TerrainDensityMultiplier = 2;

    public static float[,] BuildUnityHeights(TerrainImportData terrainImport, int heightmapResolution)
    {
        var heights = new float[heightmapResolution, heightmapResolution];
        var sourceWidth = Mathf.Max(1, terrainImport.HeightWidth);
        var sourceHeight = Mathf.Max(1, terrainImport.HeightHeight);
        var targetWidth = Mathf.Max(1, heightmapResolution);
        var targetHeight = Mathf.Max(1, heightmapResolution);

        for (var y = 0; y < targetHeight; y++)
        {
            var sourceY = RemapSampleCoordinate(y, targetHeight, sourceHeight);

            for (var x = 0; x < targetWidth; x++)
            {
                var sourceX = RemapSampleCoordinate(x, targetWidth, sourceWidth);
                heights[y, x] = SampleHeightBilinear(terrainImport, sourceX, sourceY);
            }
        }

        return heights;
    }

    public static int ToUnityHeightmapResolution(int width, int height)
    {
        var baseResolution = GetBaseUnityHeightmapResolution(width, height);
        return ((baseResolution - 1) * TerrainDensityMultiplier) + 1;
    }

    private static int GetBaseUnityHeightmapResolution(int width, int height)
    {
        var sampleCount = Mathf.Max(width, height);
        return Mathf.IsPowerOfTwo(sampleCount) ? sampleCount + 1 : sampleCount;
    }

    private static float RemapSampleCoordinate(int targetIndex, int targetResolution, int sourceResolution)
    {
        if (targetResolution <= 1 || sourceResolution <= 1)
        {
            return 0f;
        }

        return (targetIndex / (float)(targetResolution - 1)) * (sourceResolution - 1);
    }

    private static float SampleHeightBilinear(TerrainImportData terrainImport, float x, float y)
    {
        var x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, terrainImport.HeightWidth - 1);
        var y0 = Mathf.Clamp(Mathf.FloorToInt(y), 0, terrainImport.HeightHeight - 1);
        var x1 = Mathf.Clamp(x0 + 1, 0, terrainImport.HeightWidth - 1);
        var y1 = Mathf.Clamp(y0 + 1, 0, terrainImport.HeightHeight - 1);
        var tx = Mathf.Clamp01(x - x0);
        var ty = Mathf.Clamp01(y - y0);

        var h00 = SampleHeightPoint(terrainImport, x0, y0);
        var h10 = SampleHeightPoint(terrainImport, x1, y0);
        var h01 = SampleHeightPoint(terrainImport, x0, y1);
        var h11 = SampleHeightPoint(terrainImport, x1, y1);
        var hx0 = Mathf.Lerp(h00, h10, tx);
        var hx1 = Mathf.Lerp(h01, h11, tx);
        return Mathf.Lerp(hx0, hx1, ty);
    }

    private static float SampleHeightPoint(TerrainImportData terrainImport, int x, int y)
    {
        var index = y * terrainImport.HeightWidth + x;
        return terrainImport.HeightSamples[index];
    }

}
