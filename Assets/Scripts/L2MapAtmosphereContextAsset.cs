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
}
