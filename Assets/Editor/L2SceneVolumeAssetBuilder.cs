using System;
using System.Collections.Generic;
using System.Text;
using L2Viewer.SceneDomain.Models;
using L2Viewer.PackageCore;
using UnityEngine;
using UnityEditor;

internal static class L2SceneVolumeAssetBuilder
{
    private const float UnrealToUnityScale = L2WorldScale.BakeUnrealToUnityScale;
    private const float ZoneMarkerScale = 1.5f;
    private const float WaterVolumeVerticalOffsetUnreal = -45f;

    public static void BuildVolumes(
        L2Viewer.SceneDomain.Models.SceneVolumeData[] volumes,
        L2Viewer.SceneDomain.Models.SceneZoneInfoData[] fogZones,
        string outputDir,
        Dictionary<int, (L2Viewer.PackageCore.TextureData Texture, string ReferenceText)> waterTextures,
        GameObject parent,
        Action<string> log)
    {
        var volumeRoot = new GameObject("VolumeActors");
        volumeRoot.isStatic = true;
        volumeRoot.transform.SetParent(parent.transform, false);

        var zoneRoot = new GameObject("ZoneInfos");
        zoneRoot.isStatic = true;
        zoneRoot.transform.SetParent(parent.transform, false);

        var volumeCount = BuildVolumeActors(volumes, outputDir, waterTextures, volumeRoot.transform);
        var fogCount = BuildFogZones(fogZones, zoneRoot.transform);

        log($"Imported {volumeCount} supported volume actors.");
        log($"Imported {fogCount} zone infos.");
    }

    private static int BuildVolumeActors(
        L2Viewer.SceneDomain.Models.SceneVolumeData[] volumes,
        string outputDir,
        Dictionary<int, (L2Viewer.PackageCore.TextureData Texture, string ReferenceText)> waterTextures,
        Transform parent)
    {
        if (volumes == null || volumes.Length == 0)
        {
            return 0;
        }

        var materialCache = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        var waterTextureDir = $"{outputDir}/Volumes/Textures";
        var waterMaterialDir = $"{outputDir}/Volumes/Materials";
        L2AssetManager.EnsureFolderExists($"{outputDir}/Volumes");
        L2AssetManager.EnsureFolderExists(waterTextureDir);
        L2AssetManager.EnsureFolderExists(waterMaterialDir);
        var count = 0;

        foreach (var volume in volumes)
        {
            if (volume is L2Viewer.SceneDomain.Models.SceneMusicVolumeData)
            {
                continue;
            }

            var min = volume.WorldBoundsMin.HasValue ? ConvertPosition(volume.WorldBoundsMin.Value) : Vector3.zero;
            var max = volume.WorldBoundsMax.HasValue ? ConvertPosition(volume.WorldBoundsMax.Value) : Vector3.zero;
            var center = (min + max) * 0.5f;
            var size = max - min;
            if (volume is L2Viewer.SceneDomain.Models.SceneWaterVolumeData)
            {
                center.y += WaterVolumeVerticalOffsetUnreal * UnrealToUnityScale;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = volume.StableName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = new Vector3(
                Mathf.Max(0.01f, Mathf.Abs(size.x)),
                Mathf.Max(0.01f, Mathf.Abs(size.y)),
                Mathf.Max(0.01f, Mathf.Abs(size.z)));

            var isBlockingVolume = volume is L2Viewer.SceneDomain.Models.SceneBlockingVolumeData;
            var boxCollider = go.GetComponent<BoxCollider>();
            if (boxCollider != null)
            {
                boxCollider.isTrigger = false;
                if (!isBlockingVolume)
                {
                    UnityEngine.Object.DestroyImmediate(boxCollider);
                }
            }

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                if (isBlockingVolume)
                {
                    renderer.enabled = false;
                }
                else
                {
                    renderer.sharedMaterial = GetVolumeMaterial(
                        volume,
                        waterTextures,
                        waterTextureDir,
                        waterMaterialDir,
                        materialCache);
                }
            }

            count++;
        }

        return count;
    }

    private static int BuildFogZones(L2Viewer.SceneDomain.Models.SceneZoneInfoData[] fogZones, Transform parent)
    {
        if (fogZones == null || fogZones.Length == 0)
        {
            return 0;
        }

        var materialCache = new Dictionary<string, Material>(StringComparer.OrdinalIgnoreCase);
        var count = 0;

        foreach (var zone in fogZones)
        {
            if (zone.WorldLocation is not System.Numerics.Vector3 location)
            {
                continue;
            }

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = BuildZoneName(zone);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = ConvertPosition(location);
            go.transform.localScale = Vector3.one * ZoneMarkerScale;

            if (go.TryGetComponent<Collider>(out var collider))
            {
                UnityEngine.Object.DestroyImmediate(collider);
            }

            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = GetDebugMaterial(zone.TerrainZone ? "TerrainZone" : "ZoneInfo", materialCache);
            var diagnostic = go.AddComponent<L2JsonDiagnosticData>();
            if (diagnostic != null)
            {
                diagnostic.JsonText = BuildZoneJson(zone);
            }
            count++;
        }

        return count;
    }

    private static Material GetVolumeMaterial(
        L2Viewer.SceneDomain.Models.SceneVolumeData volume,
        Dictionary<int, (L2Viewer.PackageCore.TextureData Texture, string ReferenceText)> waterTextures,
        string waterTextureDir,
        string waterMaterialDir,
        Dictionary<string, Material> cache)
    {
        if (volume is not L2Viewer.SceneDomain.Models.SceneWaterVolumeData waterVolume ||
            !waterTextures.TryGetValue(volume.ExportIndex, out var waterTexture))
        {
            return GetDebugMaterial(volume.ClassName, cache);
        }

        var cacheKey = $"WaterVolume::{waterTexture.ReferenceText}";
        if (cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        var shader = L2MaterialUtility.FindBestLitShader();
        var safeTextureName = waterTexture.ReferenceText;
        var texturePath = AssetDatabase.GenerateUniqueAssetPath($"{waterTextureDir}/TEX_{safeTextureName}.png");
        var texture = L2AssetManager.CreateTextureAsset(waterTexture.Texture, texturePath, false, true);

        var material = new Material(shader)
        {
            name = $"MAT_WaterVolume_{safeTextureName}"
        };

        L2MaterialUtility.AssignMainTexture(material, texture);
        L2MaterialUtility.SetBaseColor(material, new Color(1f, 1f, 1f, 0.65f));
        L2MaterialUtility.ConfigureTransparent(
            material,
            UnityEngine.Rendering.BlendMode.SrcAlpha,
            UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha,
            premultiplyKeyword: false);

        var materialPath = AssetDatabase.GenerateUniqueAssetPath($"{waterMaterialDir}/{material.name}.mat");
        AssetDatabase.CreateAsset(material, materialPath);
        cache[cacheKey] = material;
        return material;
    }

    private static string BuildZoneName(L2Viewer.SceneDomain.Models.SceneZoneInfoData zone)
    {
        var fogState = zone.DistanceFogEnabled ? "FogOn" : "FogOff";
        var terrainState = zone.TerrainZone ? "TerrainZone" : "ZoneInfo";
        var ambient = zone.AmbientBrightness?.ToString() ?? "NoAmbient";
        var tag = string.IsNullOrWhiteSpace(zone.ZoneTag) ? "NoTag" : zone.ZoneTag;
        return $"ZONE_{terrainState}_{fogState}_A{ambient}_{tag}_{zone.Name}_{zone.ExportIndex}";
    }

    private static Material GetDebugMaterial(string key, Dictionary<string, Material> cache)
    {
        if (cache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var shader = L2MaterialUtility.FindBestUnlitShader();

        var material = new Material(shader)
        {
            name = $"DBG_{key}"
        };

        var color = GetColorForKey(key);
        L2MaterialUtility.SetBaseColor(material, color);
        L2MaterialUtility.ConfigureTransparent(
            material,
            UnityEngine.Rendering.BlendMode.SrcAlpha,
            UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha,
            premultiplyKeyword: false);

        cache[key] = material;
        return material;
    }

    private static Color GetColorForKey(string key)
    {
        switch (key)
        {
            case "WaterVolume":
                return new Color(0.1f, 0.6f, 1f, 0.22f);
            case "PhysicsVolume":
                return new Color(1f, 0.6f, 0.1f, 0.2f);
            case "BlockingVolume":
                return new Color(1f, 0.2f, 0.2f, 0.2f);
            case "TerrainZone":
                return new Color(0.2f, 1f, 0.2f, 0.75f);
            default:
                return new Color(1f, 1f, 0.2f, 0.75f);
        }
    }

    private static string BuildZoneJson(L2Viewer.SceneDomain.Models.SceneZoneInfoData zone)
    {
        if (zone == null) return "null";
        var builder = new StringBuilder();
        builder.AppendLine("{");
        var props = typeof(L2Viewer.SceneDomain.Models.SceneZoneInfoData).GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        for (int i = 0; i < props.Length; i++)
        {
            var prop = props[i];
            var val = prop.GetValue(zone);
            string valStr = SerializeValue(val);

            builder.Append($"  \"{prop.Name}\": {valStr}");
            if (i < props.Length - 1) builder.Append(",");
            builder.AppendLine();
        }
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static string SerializeValue(object val)
    {
        if (val == null)
        {
            return "null";
        }
        else if (val is string s)
        {
            return $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
        }
        else if (val is bool b)
        {
            return b ? "true" : "false";
        }
        else if (val is System.Numerics.Vector3 v)
        {
            return $"{{\"x\": {v.X.ToString(System.Globalization.CultureInfo.InvariantCulture)}, \"y\": {v.Y.ToString(System.Globalization.CultureInfo.InvariantCulture)}, \"z\": {v.Z.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";
        }
        else if (val is string[] arr)
        {
            var elements = new string[arr.Length];
            for (int j = 0; j < arr.Length; j++)
            {
                elements[j] = arr[j] == null ? "null" : $"\"{arr[j].Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
            }
            return $"[{string.Join(", ", elements)}]";
        }
        else if (val.GetType().IsEnum)
        {
            return $"\"{val}\"";
        }
        else if (val.GetType() == typeof(L2Viewer.UnrFile.UnrPointRegion))
        {
            var region = (L2Viewer.UnrFile.UnrPointRegion)val;
            string zoneRefStr = SerializeValue(region.ZoneReference);
            string leafStr = region.LeafIndex.HasValue ? region.LeafIndex.Value.ToString() : "null";
            string zoneNumStr = region.ZoneNumber.HasValue ? region.ZoneNumber.Value.ToString() : "null";
            return $"{{\"ZoneReference\": {zoneRefStr}, \"LeafIndex\": {leafStr}, \"ZoneNumber\": {zoneNumStr}}}";
        }
        else if (val.GetType() == typeof(L2Viewer.UnrFile.UnrTextureModifyInfo))
        {
            var tmi = (L2Viewer.UnrFile.UnrTextureModifyInfo)val;
            return "{" +
                   $"\"UseModify\": {(tmi.UseModify.HasValue ? (tmi.UseModify.Value ? "true" : "false") : "null")}, " +
                   $"\"TwoSide\": {(tmi.TwoSide.HasValue ? (tmi.TwoSide.Value ? "true" : "false") : "null")}, " +
                   $"\"AlphaBlend\": {(tmi.AlphaBlend.HasValue ? (tmi.AlphaBlend.Value ? "true" : "false") : "null")}, " +
                   $"\"AlphaOp\": {(tmi.AlphaOp.HasValue ? tmi.AlphaOp.Value.ToString() : "null")}, " +
                   $"\"ColorOp\": {(tmi.ColorOp.HasValue ? tmi.ColorOp.Value.ToString() : "null")}, " +
                   $"\"Color\": {SerializeValue(tmi.Color)}" +
                   "}";
        }
        else if (val.GetType() == typeof(L2Viewer.UnrFile.UnrFileColor))
        {
            var col = (L2Viewer.UnrFile.UnrFileColor)val;
            return $"{{\"R\": {col.R}, \"G\": {col.G}, \"B\": {col.B}, \"A\": {col.A}}}";
        }
        else if (val.GetType() == typeof(L2Viewer.UnrFile.UnrFileScale))
        {
            var fscale = (L2Viewer.UnrFile.UnrFileScale)val;
            return $"{{\"Scale\": {SerializeValue(fscale.Scale)}, \"SheerRate\": {fscale.SheerRate.ToString(System.Globalization.CultureInfo.InvariantCulture)}, \"SheerAxis\": {fscale.SheerAxis}}}";
        }
        else if (val.GetType() == typeof(L2Viewer.UnrFile.UnrFileObjectReference))
        {
            var oref = (L2Viewer.UnrFile.UnrFileObjectReference)val;
            return "{" +
                   $"\"RawReference\": {oref.RawReference}, " +
                   $"\"ClassName\": {SerializeValue(oref.ClassName)}, " +
                   $"\"ObjectName\": {SerializeValue(oref.ObjectName)}, " +
                   $"\"PackageName\": {SerializeValue(oref.PackageName)}" +
                   "}";
        }
        else if (val.GetType().IsValueType)
        {
            return val is IFormattable formattable ? formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture) : val.ToString();
        }
        else
        {
            return $"\"{val.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"")}\"";
        }
    }

    private static Vector3 ConvertPosition(System.Numerics.Vector3 raw)
    {
        return new Vector3(raw.X * UnrealToUnityScale, raw.Z * UnrealToUnityScale, raw.Y * UnrealToUnityScale);
    }
}


