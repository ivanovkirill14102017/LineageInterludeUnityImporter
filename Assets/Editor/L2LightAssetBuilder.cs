using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using UnityEngine;
using UnityEngine.Rendering;

internal static class L2LightAssetBuilder
{
    private const float UnrealToUnityScale = L2WorldScale.BakeUnrealToUnityScale;
    private const float UnrealLightRadiusScale = 64f / 2f;
    private const float DefaultLightIntensity = 2.35f;
    private const float DefaultFlame01LightCullDistance = 1.1f * L2WorldScale.UnityToUnrealScale;

    public static void BuildLights(SceneLightData[] lights, SceneSunData[] suns, SceneMoonData[] moons, GameObject parent, Action<string> log)
    {
        var importedCount = 0;
        var skippedOverrideDuplicates = 0;
        var overrideMatches = BuildDefaultFlame01OverrideLightMatches(lights);

        if (lights != null)
        {
            for (var i = 0; i < lights.Length; i++)
            {
                var lightData = lights[i];
                if (lightData == null)
                {
                    continue;
                }

                var lightPosition = (lightData.WorldLocation ?? System.Numerics.Vector3.Zero).TransformFromUnrealToUnityWithScale();
                if (overrideMatches.Contains(i))
                {
                    skippedOverrideDuplicates++;
                    continue;
                }

                var lightGo = new GameObject(lightData.StableName);
                lightGo.isStatic = true;
                lightGo.transform.SetParent(parent.transform, false);
                lightGo.transform.localPosition = lightPosition;

                if (lightData.WorldRotationEulerDegrees is System.Numerics.Vector3 lightRotationDegrees)
                {
                    lightGo.transform.localRotation = lightRotationDegrees.ToEulerAngles();
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
        log($"Imported {importedCount} light actors. Skipped {skippedOverrideDuplicates} duplicate lights covered by Default_Flame01 overrides. Shadow casting is disabled for imported map lights. Context only: suns={detectedSuns}, moons={detectedMoons}.");
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

    private static HashSet<int> BuildDefaultFlame01OverrideLightMatches(SceneLightData[] lights)
    {
        var matchedLightIndices = new HashSet<int>();
        if (lights == null || lights.Length == 0)
        {
            return matchedLightIndices;
        }

        var overrides = Resources.FindObjectsOfTypeAll<DefaultFlame01Override>()
            .Where(component => component != null && component.gameObject.scene.IsValid())
            .ToArray();
        if (overrides.Length == 0)
        {
            return matchedLightIndices;
        }

        for (var overrideIndex = 0; overrideIndex < overrides.Length; overrideIndex++)
        {
            var flameOverride = overrides[overrideIndex];
            var overridePosition = flameOverride.transform.position;
            var overrideScale = flameOverride.transform.lossyScale;
            var overrideRadius = DefaultFlame01LightCullDistance * Mathf.Max(0.25f, Mathf.Max(Mathf.Abs(overrideScale.x), Mathf.Abs(overrideScale.z)));

            var bestLightIndex = -1;
            var bestDistance = float.MaxValue;
            for (var lightIndex = 0; lightIndex < lights.Length; lightIndex++)
            {
                if (matchedLightIndices.Contains(lightIndex))
                {
                    continue;
                }

                var lightData = lights[lightIndex];
                if (lightData == null)
                {
                    continue;
                }

                var lightPosition = (lightData.WorldLocation ?? System.Numerics.Vector3.Zero).TransformFromUnrealToUnityWithScale();
                var distance = Vector3.Distance(lightPosition, overridePosition);
                if (distance > overrideRadius || distance >= bestDistance)
                {
                    continue;
                }

                bestDistance = distance;
                bestLightIndex = lightIndex;
            }

            if (bestLightIndex >= 0)
            {
                matchedLightIndices.Add(bestLightIndex);
            }
        }

        return matchedLightIndices;
    }
}


