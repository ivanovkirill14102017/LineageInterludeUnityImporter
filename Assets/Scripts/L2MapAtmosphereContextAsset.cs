using System;
using UnityEngine;

[CreateAssetMenu(fileName = "L2MapAtmosphereContext", menuName = "L2/Map Atmosphere Context")]
public sealed class L2MapAtmosphereContextAsset : ScriptableObject
{
    public string MapKey;
    public string SourcePath;
    public string WorldModelName;
    public int WorldModelExportIndex;
    public Vector3 WorldBoundsMinUnity;
    public Vector3 WorldBoundsMaxUnity;
    public int[] ForcedOutdoorZoneNumbers = new int[0];
    public float MapAverageIndoorFogEnd;
    public float LevelDistanceFogEnd;
    public bool HasSunRotation;
    public Vector3 MapSunEulerDegrees;
    public bool HasMoonRotation;
    public Vector3 MapMoonEulerDegrees;
    public ProbeNodeData[] Nodes = new ProbeNodeData[0];
    public ZoneData[] Zones = new ZoneData[0];
    public SkyZoneData[] SkyZones = new SkyZoneData[0];
    public SkyLightData[] SkySuns = new SkyLightData[0];
    public SkyLightData[] SkyMoons = new SkyLightData[0];
    public SkySourceReferenceData[] SkySourceReferences = new SkySourceReferenceData[0];
    public SkySurfaceMaterialData[] SkySurfaceMaterials = new SkySurfaceMaterialData[0];

    [Serializable]
    public struct ProbeNodeData
    {
        public Vector3 NormalUnreal;
        public float PlaneWUnreal;
        public int FrontNodeIndex;
        public int BackNodeIndex;
        public int PlaneNodeIndex;
        public byte Zone0;
        public byte Zone1;
        public int LeafIndex0;
        public int LeafIndex1;
    }

    [Serializable]
    public struct ZoneData
    {
        public int ZoneNumber;
        public bool SunAffect;
        public string ZoneTag;
        public bool DistanceFogEnabled;
        public bool TerrainZone;
        public bool HasDistanceFogEnd;
        public float DistanceFogEnd;
    }

    [Serializable]
    public struct SkyZoneData
    {
        public int ExportIndex;
        public string StableName;
        public string Name;
        public string ClassName;
        public string Tag;
        public bool HasWorldLocation;
        public Vector3 WorldLocationUnity;
        public bool HasWorldRotation;
        public Vector3 WorldRotationEulerDegrees;
        public string StaticMeshReference;
        public string MeshReference;
        public string TextureReference;
        public bool HasTexUPanSpeed;
        public float TexUPanSpeed;
        public bool HasTexVPanSpeed;
        public float TexVPanSpeed;
        public string[] LensFlareReferences;
        public float[] LensFlareOffset;
        public float[] LensFlareScale;
    }

    [Serializable]
    public struct SkyLightData
    {
        public int ExportIndex;
        public string StableName;
        public string Name;
        public string ClassName;
        public bool HasWorldLocation;
        public Vector3 WorldLocationUnity;
        public bool HasWorldRotation;
        public Vector3 WorldRotationEulerDegrees;
        public bool HasRadius;
        public float Radius;
        public bool SunAffect;
        public string[] SkinReferences;
    }

    [Serializable]
    public struct SkySourceReferenceData
    {
        public string Role;
        public string Reference;
        public string PackageName;
        public string ObjectName;
        public string ClassName;
        public string PackagePath;
        public string ClientRelativePath;
        public string Uri;
    }

    [Serializable]
    public struct SkySurfaceMaterialData
    {
        public int ModelExportIndex;
        public string ModelName;
        public string MaterialReference;
        public uint PolyFlags;
        public string[] PolyFlagNames;
        public int SurfaceCount;
        public bool Environment;
        public bool FakeBackdrop;
        public bool Unlit;
    }
}
