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
        asset.SkyZones = source.Sky?.SkyZones == null
            ? new L2MapAtmosphereContextAsset.SkyZoneData[0]
            : source.Sky.SkyZones.Select(x => new L2MapAtmosphereContextAsset.SkyZoneData
            {
                ExportIndex = x.ExportIndex,
                StableName = x.StableName ?? string.Empty,
                Name = x.Name ?? string.Empty,
                ClassName = x.ClassName ?? string.Empty,
                Tag = x.Tag ?? string.Empty,
                HasWorldLocation = x.WorldLocation.HasValue,
                WorldLocationUnity = x.WorldLocation.HasValue ? x.WorldLocation.Value.TransformFromUnrealToUnityWithScale() : Vector3.zero,
                HasWorldRotation = x.WorldRotationEulerDegrees.HasValue,
                WorldRotationEulerDegrees = x.WorldRotationEulerDegrees.HasValue ? x.WorldRotationEulerDegrees.Value.ToDirectUnityVectorWithoutModification() : Vector3.zero,
                StaticMeshReference = x.StaticMeshReference ?? string.Empty,
                MeshReference = x.MeshReference ?? string.Empty,
                TextureReference = x.TextureReference ?? string.Empty,
                HasTexUPanSpeed = x.TexUPanSpeed.HasValue,
                TexUPanSpeed = x.TexUPanSpeed ?? 0f,
                HasTexVPanSpeed = x.TexVPanSpeed.HasValue,
                TexVPanSpeed = x.TexVPanSpeed ?? 0f,
                LensFlareReferences = x.LensFlareReferences ?? new string[0],
                LensFlareOffset = x.LensFlareOffset ?? new float[0],
                LensFlareScale = x.LensFlareScale ?? new float[0]
            }).ToArray();
        asset.SkySuns = source.Sky?.Suns == null
            ? new L2MapAtmosphereContextAsset.SkyLightData[0]
            : source.Sky.Suns.Select(x => new L2MapAtmosphereContextAsset.SkyLightData
            {
                ExportIndex = x.ExportIndex,
                StableName = x.StableName ?? string.Empty,
                Name = x.Name ?? string.Empty,
                ClassName = x.ClassName ?? string.Empty,
                HasWorldLocation = x.WorldLocation.HasValue,
                WorldLocationUnity = x.WorldLocation.HasValue ? x.WorldLocation.Value.TransformFromUnrealToUnityWithScale() : Vector3.zero,
                HasWorldRotation = x.WorldRotationEulerDegrees.HasValue,
                WorldRotationEulerDegrees = x.WorldRotationEulerDegrees.HasValue ? x.WorldRotationEulerDegrees.Value.ToDirectUnityVectorWithoutModification() : Vector3.zero,
                HasRadius = x.Radius.HasValue,
                Radius = x.Radius ?? 0f,
                SunAffect = x.SunAffect,
                SkinReferences = x.SkinReferences ?? new string[0]
            }).ToArray();
        asset.SkyMoons = source.Sky?.Moons == null
            ? new L2MapAtmosphereContextAsset.SkyLightData[0]
            : source.Sky.Moons.Select(x => new L2MapAtmosphereContextAsset.SkyLightData
            {
                ExportIndex = x.ExportIndex,
                StableName = x.StableName ?? string.Empty,
                Name = x.Name ?? string.Empty,
                ClassName = x.ClassName ?? string.Empty,
                HasWorldLocation = x.WorldLocation.HasValue,
                WorldLocationUnity = x.WorldLocation.HasValue ? x.WorldLocation.Value.TransformFromUnrealToUnityWithScale() : Vector3.zero,
                HasWorldRotation = x.WorldRotationEulerDegrees.HasValue,
                WorldRotationEulerDegrees = x.WorldRotationEulerDegrees.HasValue ? x.WorldRotationEulerDegrees.Value.ToDirectUnityVectorWithoutModification() : Vector3.zero,
                HasRadius = x.Radius.HasValue,
                Radius = x.Radius ?? 0f,
                SunAffect = x.SunAffect,
                SkinReferences = x.SkinReferences ?? new string[0]
            }).ToArray();
        asset.SkySourceReferences = source.Sky?.SourceReferences == null
            ? new L2MapAtmosphereContextAsset.SkySourceReferenceData[0]
            : source.Sky.SourceReferences.Select(x => new L2MapAtmosphereContextAsset.SkySourceReferenceData
            {
                Role = x.Role ?? string.Empty,
                Reference = x.Reference ?? string.Empty,
                PackageName = x.PackageName ?? string.Empty,
                ObjectName = x.ObjectName ?? string.Empty,
                ClassName = x.ClassName ?? string.Empty,
                PackagePath = x.PackagePath ?? string.Empty,
                ClientRelativePath = x.ClientRelativePath ?? string.Empty,
                Uri = x.Uri ?? string.Empty
            }).ToArray();
        asset.SkySurfaceMaterials = source.Sky?.SurfaceMaterials == null
            ? new L2MapAtmosphereContextAsset.SkySurfaceMaterialData[0]
            : source.Sky.SurfaceMaterials.Select(x => new L2MapAtmosphereContextAsset.SkySurfaceMaterialData
            {
                ModelExportIndex = x.ModelExportIndex,
                ModelName = x.ModelName ?? string.Empty,
                MaterialReference = x.MaterialReference ?? string.Empty,
                PolyFlags = x.PolyFlags,
                PolyFlagNames = x.PolyFlagNames ?? new string[0],
                SurfaceCount = x.SurfaceCount,
                Environment = x.Environment,
                FakeBackdrop = x.FakeBackdrop,
                Unlit = x.Unlit
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


