using System;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using UnityEngine;

internal static class TerrainAlphaMapBuilder
{
    public static TerrainLayerImportData[] GetUsefulLayers(TerrainImportData terrainImport)
    {
        return terrainImport.Layers
            .Where(IsUsableLayer)
            .OrderBy(layer => layer.ArrayIndex)
            .ToArray();
    }

    public static float[,,] BuildAlphamaps(TerrainLayerImportData[] usefulLayers)
    {
        if (usefulLayers.Length == 0)
        {
            throw new InvalidOperationException("Could not build a single usable terrain layer.");
        }

        var width = usefulLayers.Max(layer => Mathf.Max(1, layer.MaskWidth));
        var height = usefulLayers.Max(layer => Mathf.Max(1, layer.MaskHeight));
        var alphamaps = new float[height, width, usefulLayers.Length];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var overlaySum = 0f;

                for (var layerIndex = 1; layerIndex < usefulLayers.Length; layerIndex++)
                {
                    var sample = SampleMask(usefulLayers[layerIndex], x, y);
                    alphamaps[y, x, layerIndex] = sample;
                    overlaySum += sample;
                }

                if (overlaySum > 1f)
                {
                    for (var layerIndex = 1; layerIndex < usefulLayers.Length; layerIndex++)
                    {
                        alphamaps[y, x, layerIndex] /= overlaySum;
                    }

                    overlaySum = 1f;
                }

                alphamaps[y, x, 0] = Mathf.Clamp01(1f - overlaySum);
            }
        }

        return alphamaps;
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

    private static bool IsUsableLayer(TerrainLayerImportData layer)
    {
        if (layer.ArrayIndex == 0)
        {
            return layer.Texture != null;
        }

        return layer.Texture != null && layer.MaskSamples != null && layer.MaskSamples.Any(sample => sample > 0f);
    }
}
