using System.Linq;
using L2Viewer.SceneDomain.Models;
using UnityEditor;
using UnityEngine;

internal static class L2MapContextAssetBuilder
{
    public static L2MapAtmosphereContextAsset BuildContextAsset(SceneMapContextData source, string outputDir)
    {
        var contextDir = $"{outputDir}/Context";
        L2AssetManager.EnsureFolderExists(contextDir);
        var assetPath = $"{contextDir}/{source.MapKey}_MapContext.asset";

        var asset = AssetDatabase.LoadAssetAtPath<L2MapAtmosphereContextAsset>(assetPath);
        if (asset == null)
        {
            asset = ScriptableObject.CreateInstance<L2MapAtmosphereContextAsset>();
            AssetDatabase.CreateAsset(asset, assetPath);
        }

        var boundsMin = source.WorldBoundsMin.TransformFromUnrealToUnityWithScale();
        var boundsMax = source.WorldBoundsMax.TransformFromUnrealToUnityWithScale();
        var normalizedBounds = NormalizeBoundsToTerrainQuadrant(boundsMin, boundsMax);

        asset.MapKey = source.MapKey;
        asset.SourcePath = source.SourcePath;
        asset.WorldModelName = source.WorldModelName;
        asset.WorldModelExportIndex = source.WorldModelExportIndex;
        asset.WorldBoundsMinUnity = normalizedBounds.min;
        asset.WorldBoundsMaxUnity = normalizedBounds.max;
        asset.ForcedOutdoorZoneNumbers = source.ForcedOutdoorZoneNumbers ?? new int[0];
        asset.MapAverageIndoorFogEnd = source.MapAverageIndoorFogEnd;
        asset.LevelDistanceFogEnd = source.LevelDistanceFogEnd;
        asset.HasSunRotation = source.HasSunRotation;
        asset.MapSunEulerDegrees = source.PrimarySunEulerDegrees.ToDirectUnityVectorWithoutModification();
        asset.HasMoonRotation = source.HasMoonRotation;
        asset.MapMoonEulerDegrees = source.PrimaryMoonEulerDegrees.ToDirectUnityVectorWithoutModification();
        asset.Nodes = source.Nodes == null
            ? new L2MapAtmosphereContextAsset.ProbeNodeData[0]
            : source.Nodes.Select(x => new L2MapAtmosphereContextAsset.ProbeNodeData
            {
                NormalUnreal = new Vector3(x.Normal.X, x.Normal.Y, x.Normal.Z),
                PlaneWUnreal = x.PlaneW,
                FrontNodeIndex = x.FrontNodeIndex,
                BackNodeIndex = x.BackNodeIndex,
                PlaneNodeIndex = x.PlaneNodeIndex,
                Zone0 = x.Zone0,
                Zone1 = x.Zone1,
                LeafIndex0 = x.LeafIndex0,
                LeafIndex1 = x.LeafIndex1
            }).ToArray();
        asset.Zones = source.Zones == null
            ? new L2MapAtmosphereContextAsset.ZoneData[0]
            : source.Zones.Select(x => new L2MapAtmosphereContextAsset.ZoneData
            {
                ZoneNumber = x.ZoneNumber,
                SunAffect = x.SunAffect,
                ZoneTag = x.ZoneTag ?? string.Empty,
                DistanceFogEnabled = x.DistanceFogEnabled,
                TerrainZone = x.TerrainZone,
                HasDistanceFogEnd = x.DistanceFogEnd.HasValue,
                DistanceFogEnd = x.DistanceFogEnd ?? 0f
            }).ToArray();

        EditorUtility.SetDirty(asset);
        return asset;
    }

    private static (Vector3 min, Vector3 max) NormalizeBoundsToTerrainQuadrant(Vector3 boundsMin, Vector3 boundsMax)
    {
        var originalMin = Vector3.Min(boundsMin, boundsMax);
        var originalMax = Vector3.Max(boundsMin, boundsMax);
        var center = (originalMin + originalMax) * 0.5f;
        var verticalSize = Mathf.Max(0.01f, originalMax.y - originalMin.y);
        var halfQuadrantSize = L2WorldScale.TerrainQuadrantSizeUnity * 0.5f;
        var halfVerticalSize = verticalSize * 0.5f;

        var min = new Vector3(
            center.x - halfQuadrantSize,
            center.y - halfVerticalSize,
            center.z - halfQuadrantSize);
        var max = new Vector3(
            center.x + halfQuadrantSize,
            center.y + halfVerticalSize,
            center.z + halfQuadrantSize);
        return (min, max);
    }

}


