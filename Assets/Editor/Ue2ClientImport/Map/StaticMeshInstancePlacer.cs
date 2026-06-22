using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.PackageCore;
using L2Viewer.SceneDomain.Models;
using UnityEditor;
using UnityEngine;
using L2Viewer.SceneDomain.Services;
using NumericsVector3 = System.Numerics.Vector3;

internal static class StaticMeshInstancePlacer
{
    private const float UnrealToUnityScale = L2WorldScale.UnrealToUnityScale;
    private const string DefaultFlame01Token = "Default_Flame01";

    public static void PlaceInstances(
        IReadOnlyList<SceneStaticMeshInstance> instances,
        GameObject parent,
        IReadOnlyDictionary<string, GameObject> prefabCache,
        Action<string> log)
    {
        log($"Placing {instances.Count} instances on the scene...");
        var spawnedCount = 0;

        foreach (var instance in instances)
        {
            if (TryPlaceOverrideInstance(instance, parent, spawnedCount, log))
            {
                spawnedCount++;
                continue;
            }

            if (string.IsNullOrEmpty(instance.MeshReference) || !prefabCache.TryGetValue(instance.MeshReference, out var prefab))
            {
                continue;
            }

            if (prefab == null)
            {
                continue;
            }

            var gameObjectName = BuildInstanceName(instance, spawnedCount);
            var visual = PrefabUtility.InstantiatePrefab(prefab, parent.transform) as GameObject;
            if (visual == null)
            {
                continue;
            }

            visual.name = gameObjectName;
            visual.isStatic = true;
            visual.transform.localPosition = ConvertPosition(instance.WorldLocation);
            visual.transform.localRotation = ConvertEulerAngles(instance.RotationEulerDegrees);
            visual.transform.localScale = new Vector3(instance.Scale.X, instance.Scale.Z, instance.Scale.Y);
            ApplyPrePivotOffset(visual.transform, instance);

            spawnedCount++;
        }

        log($"Finished placing {spawnedCount} static meshes.");
    }

    private static bool TryPlaceOverrideInstance(
        SceneStaticMeshInstance instance,
        GameObject parent,
        int spawnedCount,
        Action<string> log)
    {
        if (instance == null || string.IsNullOrWhiteSpace(instance.MeshReference))
        {
            return false;
        }

        if (instance.MeshReference.IndexOf(DefaultFlame01Token, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return false;
        }

        var prefab = StaticMeshOverridePrefabUtility.GetDefaultFlame01OverridePrefab();
        if (prefab == null)
        {
            throw new InvalidOperationException("Could not load Default_Flame01 override prefab.");
        }

        var visual = PrefabUtility.InstantiatePrefab(prefab, parent.transform) as GameObject;
        if (visual == null)
        {
            throw new InvalidOperationException("Unity failed to instantiate Default_Flame01 override prefab.");
        }

        visual.name = BuildInstanceName(instance, spawnedCount);
        visual.isStatic = true;
        visual.transform.localPosition = ConvertPosition(instance.WorldLocation);
        visual.transform.localRotation = ConvertEulerAngles(instance.RotationEulerDegrees);
        visual.transform.localScale = new Vector3(instance.Scale.X, instance.Scale.Z, instance.Scale.Y);

        log?.Invoke($"[StaticMesh/Override] Replaced '{instance.MeshReference}' with Default_Flame01 override prefab.");
        return true;
    }

    private static string BuildInstanceName(SceneStaticMeshInstance instance, int spawnedCount)
    {
        var gameObjectName = instance.ActorName;
        if (string.IsNullOrWhiteSpace(gameObjectName) || gameObjectName.StartsWith("StaticMeshActor"))
        {
            var parts = instance.MeshReference.Split('.');
            var niceName = parts.Length > 0 ? parts.Last() : "StaticMesh";
            gameObjectName = $"{niceName}_{spawnedCount}";
        }

        return gameObjectName;
    }

    private static void ApplyPrePivotOffset(Transform visualRoot, SceneStaticMeshInstance instance)
    {
        if (visualRoot == null)
        {
            return;
        }

        var geometry = visualRoot.Find("Geometry");
        if (geometry == null)
        {
            geometry = visualRoot;
        }

        geometry.localPosition = instance.PrePivot == System.Numerics.Vector3.Zero
            ? Vector3.zero
            : ConvertPosition(ComputePrePivotOffset(instance.PrePivot, instance.Scale));
    }

    private static System.Numerics.Vector3 ComputePrePivotOffset(
        System.Numerics.Vector3 prePivot,
        System.Numerics.Vector3 scale)
    {
        return new System.Numerics.Vector3(
            -prePivot.X / SafeScale(scale.X),
            -prePivot.Y / SafeScale(scale.Y),
            -prePivot.Z / SafeScale(scale.Z));
    }

    private static float SafeScale(float value)
    {
        return Math.Abs(value) < 0.0001f ? 1f : value;
    }

    private static Vector3 ConvertPosition(System.Numerics.Vector3 raw)
    {
        return new Vector3(raw.X * UnrealToUnityScale, raw.Z * UnrealToUnityScale, raw.Y * UnrealToUnityScale);
    }

    private static Quaternion ConvertEulerAngles(System.Numerics.Vector3 rotDegrees)
    {
        return Quaternion.Euler(rotDegrees.X, -rotDegrees.Y, -rotDegrees.Z);
    }
}

internal static class TerrainDecorationInstancePlacer
{
    private const float UnrealToUnityScale = L2WorldScale.UnrealToUnityScale;
    private const int MaxSampleResolution = 128;

    public static void PlaceDecorations(
        IReadOnlyList<SceneTerrainDecorationLayer> terrainDecorations,
        GameObject parent,
        IReadOnlyDictionary<string, GameObject> prefabCache,
        string clientPath,
        Action<string> log)
    {
        if (terrainDecorations == null || terrainDecorations.Count == 0)
        {
            return;
        }

        log($"Placing terrain decorations from {terrainDecorations.Count} deco layers...");

        var textureManager = new BspTextureManager(clientPath);
        var heightTextureRequests = terrainDecorations
            .Select(layer => new SceneTextureRequest(layer.HeightMapResource.PackageName, layer.HeightMapResource.ObjectName))
            .Distinct()
            .ToArray();
        var resolvedHeightTextures = textureManager.ResolveMany(heightTextureRequests);

        var terrainDecorationRoot = new GameObject("TerrainDecorations");
        terrainDecorationRoot.transform.SetParent(parent.transform, false);

        var spawnedCount = 0;
        foreach (var layer in terrainDecorations)
        {
            if (string.IsNullOrWhiteSpace(layer.MeshReference) ||
                !prefabCache.TryGetValue(layer.MeshReference, out var prefab) ||
                prefab == null)
            {
                continue;
            }

            var heightLookup = $"{layer.HeightMapResource.PackageName}.{layer.HeightMapResource.ObjectName}";
            if (!resolvedHeightTextures.TryGetValue(heightLookup, out var heightResolved) || heightResolved?.Texture == null)
            {
                continue;
            }

            var heightTexture = heightResolved.Texture;
            var densityTexture = layer.DensityMapTexture;
            if (densityTexture == null || densityTexture.Width <= 0 || densityTexture.Height <= 0)
            {
                continue;
            }

            var heightField = BuildHeightField(heightTexture);
            var stepX = Math.Max(1, (int)Math.Ceiling(densityTexture.Width / (double)MaxSampleResolution));
            var stepY = Math.Max(1, (int)Math.Ceiling(densityTexture.Height / (double)MaxSampleResolution));
            var rng = new System.Random(layer.Seed ^ (layer.TerrainExportIndex * 397) ^ (layer.LayerIndex * 7919));
            var densityScale = layer.DensityMultiplier?.Max ?? 100f;
            var maxPerQuad = Math.Max(1, layer.MaxPerQuad);
            var emitted = 0;

            for (var y = 0; y < densityTexture.Height; y += stepY)
            {
                for (var x = 0; x < densityTexture.Width; x += stepX)
                {
                    var density = SampleGray01(densityTexture, x, y);
                    if (density <= 0.05f)
                    {
                        continue;
                    }

                    var desired = density * maxPerQuad * (densityScale / 100f);
                    var count = Math.Clamp((int)MathF.Round(desired), 0, 3);
                    if (count == 0 && density > 0.35f)
                    {
                        count = 1;
                    }

                    for (var i = 0; i < count; i++)
                    {
                        var jitterU = (x + (float)rng.NextDouble() * stepX) / Math.Max(1, densityTexture.Width - 1);
                        var jitterV = (y + (float)rng.NextDouble() * stepY) / Math.Max(1, densityTexture.Height - 1);
                        jitterU = Math.Clamp(jitterU, 0f, 1f);
                        jitterV = Math.Clamp(jitterV, 0f, 1f);

                        var worldPosition = SampleTerrainWorldPosition(heightTexture, heightField, layer.TerrainScale, layer.TerrainLocation, jitterU, jitterV);
                        var scale = ResolveScale(layer, jitterU, jitterV, rng);
                        var yawDegrees = layer.RandomYaw ? (float)(rng.NextDouble() * 360.0) : 0f;

                        var visual = PrefabUtility.InstantiatePrefab(prefab, terrainDecorationRoot.transform) as GameObject;
                        if (visual == null)
                        {
                            continue;
                        }

                        visual.name = $"{layer.TerrainActorName}:Deco{layer.LayerIndex:D2}:{++emitted:D5}";
                        visual.isStatic = true;
                        visual.transform.localPosition = ConvertTerrainPosition(worldPosition);
                        visual.transform.localRotation = Quaternion.Euler(0f, -yawDegrees, 0f);
                        visual.transform.localScale = new Vector3(scale.X, scale.Z, scale.Y);

                        spawnedCount++;
                    }
                }
            }
        }

        log($"Finished placing {spawnedCount} terrain decoration instances.");
    }

    private static NumericsVector3 SampleTerrainWorldPosition(
        TextureData heightTexture,
        IReadOnlyList<float> heightField,
        NumericsVector3? terrainScale,
        NumericsVector3 terrainLocation,
        float u,
        float v)
    {
        var sampleX = u * Math.Max(1, heightTexture.Width - 1);
        var sampleY = v * Math.Max(1, heightTexture.Height - 1);
        var scaleX = terrainScale?.X ?? 4f;
        var scaleHeight = terrainScale is null ? 240f : terrainScale.Value.Y * 256f;
        var scaleY = terrainScale?.Z ?? 4f;
        var cx = 0.5f * (heightTexture.Width - 1);
        var cy = 0.5f * (heightTexture.Height - 1);
        var heightValue = SampleHeight(heightField, heightTexture.Width, heightTexture.Height, sampleX, sampleY);

        return new NumericsVector3(
            ((sampleX - cx) * scaleX) + terrainLocation.X,
            ((sampleY - cy) * scaleY) + terrainLocation.Y,
            ((heightValue - 0.5f) * scaleHeight) + terrainLocation.Z);
    }

    private static float SampleHeight(IReadOnlyList<float> heightField, int width, int height, float sampleX, float sampleY)
    {
        var x0 = Math.Clamp((int)MathF.Floor(sampleX), 0, width - 1);
        var y0 = Math.Clamp((int)MathF.Floor(sampleY), 0, height - 1);
        var x1 = Math.Clamp(x0 + 1, 0, width - 1);
        var y1 = Math.Clamp(y0 + 1, 0, height - 1);
        var tx = sampleX - x0;
        var ty = sampleY - y0;

        var h00 = heightField[(y0 * width) + x0];
        var h10 = heightField[(y0 * width) + x1];
        var h01 = heightField[(y1 * width) + x0];
        var h11 = heightField[(y1 * width) + x1];
        var hx0 = Lerp(h00, h10, tx);
        var hx1 = Lerp(h01, h11, tx);
        return Lerp(hx0, hx1, ty);
    }

    private static NumericsVector3 ResolveScale(SceneTerrainDecorationLayer layer, float u, float v, System.Random rng)
    {
        var amount = layer.ScaleMapTexture == null ? (float)rng.NextDouble() : SampleGray01(layer.ScaleMapTexture, u, v);
        if (layer.ScaleMultiplier == null)
        {
            return NumericsVector3.One;
        }

        return new NumericsVector3(
            Lerp(layer.ScaleMultiplier.X.Min, layer.ScaleMultiplier.X.Max, amount),
            Lerp(layer.ScaleMultiplier.Y.Min, layer.ScaleMultiplier.Y.Max, amount),
            Lerp(layer.ScaleMultiplier.Z.Min, layer.ScaleMultiplier.Z.Max, amount));
    }

    private static List<float> BuildHeightField(TextureData texture)
    {
        var pixels = new List<(byte R, byte G, byte B, byte A)>(texture.Width * texture.Height);
        for (var i = 0; i < texture.RgbaBytes.Length; i += 4)
        {
            pixels.Add((texture.RgbaBytes[i], texture.RgbaBytes[i + 1], texture.RgbaBytes[i + 2], texture.RgbaBytes[i + 3]));
        }

        // Heightmaps vary by channel across client assets, so we pick the smoothest channel with usable range.
        var channels = new[] { "a", "r", "g", "b", "luma" };
        var channelName = "luma";
        var bestScore = float.MaxValue;
        foreach (var channel in channels)
        {
            var (rough, lo, hi) = AnalyzeHeightChannel(pixels, texture.Width, texture.Height, channel);
            var range = hi - lo;
            if (range < 0.03f)
            {
                continue;
            }

            var score = rough - (0.04f * range);
            if (score < bestScore)
            {
                bestScore = score;
                channelName = channel;
            }
        }

        return pixels.Select(pixel => ChannelValue(pixel, channelName)).ToList();
    }

    private static (float Rough, float Lo, float Hi) AnalyzeHeightChannel(
        List<(byte R, byte G, byte B, byte A)> pixels,
        int width,
        int height,
        string mode)
    {
        var lo = 1f;
        var hi = 0f;
        var rough = 0f;
        var count = 0;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var value = ChannelValue(pixels[row + x], mode);
                lo = MathF.Min(lo, value);
                hi = MathF.Max(hi, value);
                if (x + 1 < width)
                {
                    rough += MathF.Abs(ChannelValue(pixels[row + x + 1], mode) - value);
                    count++;
                }

                if (y + 1 < height)
                {
                    rough += MathF.Abs(ChannelValue(pixels[row + x + width], mode) - value);
                    count++;
                }
            }
        }

        return (rough / Math.Max(1, count), lo, hi);
    }

    private static float ChannelValue((byte R, byte G, byte B, byte A) pixel, string mode)
    {
        return mode switch
        {
            "r" => pixel.R / 255f,
            "g" => pixel.G / 255f,
            "b" => pixel.B / 255f,
            "a" => pixel.A / 255f,
            _ => ((0.299f * pixel.R) + (0.587f * pixel.G) + (0.114f * pixel.B)) / 255f
        };
    }

    private static float SampleGray01(TextureData texture, int x, int y)
    {
        var clampedX = Math.Clamp(x, 0, texture.Width - 1);
        var clampedY = Math.Clamp(y, 0, texture.Height - 1);
        var src = (clampedY * texture.Width + clampedX) * 4;
        return ((0.299f * texture.RgbaBytes[src + 0]) +
                (0.587f * texture.RgbaBytes[src + 1]) +
                (0.114f * texture.RgbaBytes[src + 2])) / 255f;
    }

    private static float SampleGray01(TextureData texture, float u, float v)
    {
        var x = Math.Clamp((int)MathF.Round(u * Math.Max(1, texture.Width - 1)), 0, texture.Width - 1);
        var y = Math.Clamp((int)MathF.Round(v * Math.Max(1, texture.Height - 1)), 0, texture.Height - 1);
        return SampleGray01(texture, x, y);
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
    }

    private static Vector3 ConvertTerrainPosition(NumericsVector3 raw)
    {
        return new Vector3(raw.X * UnrealToUnityScale, raw.Z * UnrealToUnityScale, raw.Y * UnrealToUnityScale);
    }
}

internal static class StaticMeshRendererMaterialUtility
{
    private static Material _fallbackMaterial;

    public static Material[] BuildRendererMaterials(
        string meshReference,
        Mesh mesh,
        StaticMeshMaterialCatalog materialCatalog)
    {
        if (mesh == null || mesh.subMeshCount <= 0)
        {
            return null;
        }

        var resolved = new Material[mesh.subMeshCount];
        materialCatalog.MaterialsByMeshReference.TryGetValue(meshReference, out var importedMaterials);
        var fallback = GetFallbackMaterial();

        for (var i = 0; i < resolved.Length; i++)
        {
            if (importedMaterials != null && i < importedMaterials.Length && importedMaterials[i] != null)
            {
                resolved[i] = importedMaterials[i];
            }
            else
            {
                resolved[i] = fallback;
            }
        }

        return resolved;
    }

    private static Material GetFallbackMaterial()
    {
        if (_fallbackMaterial != null)
        {
            return _fallbackMaterial;
        }

        var shader = StaticMeshImportUtility.FindDefaultShader();
        _fallbackMaterial = new Material(shader)
        {
            name = "StaticMeshFallbackMaterial",
            enableInstancing = true
        };
        return _fallbackMaterial;
    }
}
