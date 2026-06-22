using L2Viewer.SceneDomain.Models;
using UnityEngine;

internal static class TerrainHeightMapBuilder
{
    private const int TerrainDensityMultiplier = 2;

    public static float[,] BuildUnityHeights(TerrainImportData terrainImport, int heightmapResolution, int smoothLevel = 0)
    {
        if (smoothLevel <= 0 && heightmapResolution == GetBaseUnityHeightmapResolution(terrainImport.HeightWidth, terrainImport.HeightHeight))
        {
            return BuildLegacyUnityHeights(terrainImport, heightmapResolution);
        }

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

        if (smoothLevel > 0)
        {
            heights = SmoothHeights(heights, smoothLevel);
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

    private static float[,] BuildLegacyUnityHeights(TerrainImportData terrainImport, int heightmapResolution)
    {
        var heights = new float[heightmapResolution, heightmapResolution];

        for (var y = 0; y < heightmapResolution; y++)
        {
            var sourceY = Mathf.Clamp(y, 0, terrainImport.HeightHeight - 1);

            for (var x = 0; x < heightmapResolution; x++)
            {
                var sourceX = Mathf.Clamp(x, 0, terrainImport.HeightWidth - 1);
                heights[y, x] = SampleHeightPoint(terrainImport, sourceX, sourceY);
            }
        }

        return heights;
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

    private static float[,] SmoothHeights(float[,] source, int smoothLevel)
    {
        var width = source.GetLength(1);
        var height = source.GetLength(0);
        var iterations = smoothLevel switch
        {
            1 => 1,
            2 => 3,
            _ => 0
        };

        if (iterations <= 0 || width <= 1 || height <= 1)
        {
            return source;
        }

        var current = source;
        for (var iteration = 0; iteration < iterations; iteration++)
        {
            var next = new float[height, width];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var sum = 0f;
                    var weight = 0f;
                    for (var oy = -1; oy <= 1; oy++)
                    {
                        var sy = Mathf.Clamp(y + oy, 0, height - 1);
                        for (var ox = -1; ox <= 1; ox++)
                        {
                            var sx = Mathf.Clamp(x + ox, 0, width - 1);
                            var kernel = ox == 0 && oy == 0 ? 4f : (ox == 0 || oy == 0 ? 2f : 1f);
                            sum += current[sy, sx] * kernel;
                            weight += kernel;
                        }
                    }

                    next[y, x] = sum / weight;
                }
            }

            current = next;
        }

        return current;
    }
}
