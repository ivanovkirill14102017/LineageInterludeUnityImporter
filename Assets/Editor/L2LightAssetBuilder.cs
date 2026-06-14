using System;
using L2Viewer.SceneDomain.Models;
using UnityEngine;
using UnityEngine.Rendering;

internal static class L2LightAssetBuilder
{
    private const float UnrealToUnityScale = 0.01f;
    private const float UnrealLightRadiusScale = 64f;
    private const float DefaultLightIntensity = 2.35f;

    public static void BuildLights(SceneLightData[] lights, SceneSunData[] suns, SceneMoonData[] moons, GameObject parent, Action<string> log)
    {
        var importedCount = 0;

        if (lights != null)
        {
            for (var i = 0; i < lights.Length; i++)
            {
                var lightData = lights[i];
                if (lightData == null)
                {
                    continue;
                }

                var lightGo = new GameObject(BuildLightObjectName(lightData.Name, lightData.ClassName, lightData.ExportIndex));
                lightGo.isStatic = true;
                lightGo.transform.SetParent(parent.transform, false);
                lightGo.transform.localPosition = ConvertPosition(lightData.WorldLocation ?? System.Numerics.Vector3.Zero);

                if (lightData.WorldRotationEulerDegrees is System.Numerics.Vector3 lightRotationDegrees)
                {
                    lightGo.transform.localRotation = ConvertEulerAngles(lightRotationDegrees);
                }

                var unityLight = lightGo.AddComponent<Light>();
                unityLight.type = ResolveLightType(lightData);
                unityLight.color = BuildLightColor(lightData);
                unityLight.range = BuildLightRange(lightData);
                var enableShadows = false;
                unityLight.shadows = LightShadows.None;
                unityLight.lightmapBakeType = LightmapBakeType.Baked;
                if (unityLight.type == LightType.Spot)
                {
                    unityLight.spotAngle = BuildSpotAngle(lightData);
                }

                ConfigureLight(unityLight, BuildLightIntensity(lightData), enableShadows);

                importedCount++;
            }
        }

        var detectedSuns = suns != null ? suns.Length : 0;
        var detectedMoons = moons != null ? moons.Length : 0;
        log($"Imported {importedCount} light actors. Shadow casting is disabled for imported map lights. Context only: suns={detectedSuns}, moons={detectedMoons}.");
    }

    private static string BuildLightObjectName(string name, string className, int exportIndex)
    {
        var safeName = AssetNameUtility.SanitizeName(name);
        var safeClass = AssetNameUtility.SanitizeName(className);
        return $"Light_{safeClass}_{safeName}_{exportIndex}";
    }

    private static LightType ResolveLightType(SceneLightData lightData)
    {
        if (lightData.Directional)
        {
            return LightType.Directional;
        }

        if (lightData.Cone is > 0f)
        {
            return LightType.Spot;
        }

        return LightType.Point;
    }

    private static Color BuildLightColor(SceneLightData lightData)
    {
        var hue = lightData.Hue.GetValueOrDefault();
        var saturation = lightData.Saturation.GetValueOrDefault(255);
        var h = Mathf.Repeat(hue / 255f, 1f);
        var s = Mathf.Clamp01(1f - (saturation / 255f));
        return Color.HSVToRGB(h, s, 1f);
    }

    private static float BuildLightIntensity(SceneLightData lightData)
    {
        var brightness = lightData.Brightness.GetValueOrDefault(255f);
        return Mathf.Max(0f, (brightness / 255f) * DefaultLightIntensity);
    }

    private static float BuildLightRange(SceneLightData lightData)
    {
        var radius = lightData.Radius.GetValueOrDefault(64f);
        return Mathf.Max(0.1f, radius * UnrealLightRadiusScale * UnrealToUnityScale);
    }

    private static float BuildSpotAngle(SceneLightData lightData)
    {
        var cone = lightData.Cone.GetValueOrDefault(64f);
        return Mathf.Clamp((cone / 255f) * 180f, 1f, 179f);
    }

    private static void ConfigureLight(Light unityLight, float intensity, bool enableShadows)
    {
        unityLight.intensity = intensity;
        unityLight.shadows = enableShadows ? unityLight.shadows : LightShadows.None;

        if (enableShadows && GraphicsSettings.currentRenderPipeline == null)
        {
            unityLight.shadowResolution = LightShadowResolution.Low;
        }
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


