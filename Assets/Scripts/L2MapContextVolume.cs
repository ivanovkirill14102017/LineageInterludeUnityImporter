using System;
using System.Collections.Generic;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2MapContextVolume : MonoBehaviour
{
    private const float UnityToUnrealScale = 1f / L2WorldScale.BakeUnrealToUnityScale;

    [Header("Context")]
    public L2MapAtmosphereContextAsset Context;
    public bool AutoConfigureContextCollider = true;

    [Header("Debug")]
    public bool DrawGizmo = true;
    [SerializeField] private string mapKey;
    [SerializeField] private Vector3 boundsCenter;
    [SerializeField] private Vector3 boundsSize;

    private readonly Dictionary<int, ConsumerState> _consumerStates = new Dictionary<int, ConsumerState>();
    private readonly Dictionary<int, L2MapAtmosphereContextAsset.ZoneData> _zonesByNumber = new Dictionary<int, L2MapAtmosphereContextAsset.ZoneData>();
    private BoxCollider _boxCollider;

    public string MapKey { get { return Context != null ? Context.MapKey : mapKey; } }

    private void OnEnable()
    {
        RebuildCaches();
        EnsureContextColliderSetup();
    }

    private void OnValidate()
    {
        RebuildCaches();
        EnsureContextColliderSetup();
    }

    public void RefreshContext()
    {
        RebuildCaches();
        EnsureContextColliderSetup();
    }

    public void ClearConsumer(L2CameraAtmosphereProbe consumer)
    {
        if (consumer == null)
        {
            return;
        }

        _consumerStates.Remove(consumer.GetInstanceID());
    }

    public bool ContainsWorldPosition(Vector3 worldPosition)
    {
        EnsureContextColliderSetup();
        return _boxCollider != null && _boxCollider.enabled && _boxCollider.bounds.Contains(worldPosition);
    }

    public bool TryEvaluate(L2CameraAtmosphereProbe consumer, Vector3 worldPosition, out EvaluationResult result)
    {
        result = default(EvaluationResult);
        if (consumer == null || Context == null || Context.Nodes == null || Context.Nodes.Length == 0)
        {
            return false;
        }

        var localPosition = transform.InverseTransformPoint(worldPosition);
        var unrealPoint = ToUnrealPosition(localPosition);
        var probeResult = Probe(unrealPoint, Context.Nodes);

        L2MapAtmosphereContextAsset.ZoneData zoneData;
        var hasZoneInfo = _zonesByNumber.TryGetValue(probeResult.ZoneNumber, out zoneData);
        var observedIndoor = hasZoneInfo ? !zoneData.SunAffect : false;
        var sunShouldAffect = hasZoneInfo ? zoneData.SunAffect : true;
        var zoneTag = hasZoneInfo ? zoneData.ZoneTag ?? string.Empty : string.Empty;
        if (IsForcedOutdoorZone(probeResult.ZoneNumber))
        {
            observedIndoor = false;
            sunShouldAffect = true;
        }

        var key = consumer.GetInstanceID();
        ConsumerState state;
        if (!_consumerStates.TryGetValue(key, out state))
        {
            state = new ConsumerState();
        }

        if (!state.HasPreviousProbePosition)
        {
            state.HasPreviousProbePosition = true;
            state.PreviousProbeLocalPosition = localPosition;
            state.PreviousZone = probeResult.ZoneNumber;
            state.PreviousObservedIndoor = observedIndoor;
            state.HasTransitionPoint = false;
        }
        else
        {
            var jumpDistance = Vector3.Distance(state.PreviousProbeLocalPosition, localPosition);
            if (jumpDistance >= consumer.GetTransitionResetDistance())
            {
                state.HasTransitionPoint = false;
            }
            else if (state.PreviousZone != probeResult.ZoneNumber || state.PreviousObservedIndoor != observedIndoor)
            {
                state.TransitionPointLocalPosition = FindTransitionPoint(state.PreviousProbeLocalPosition, localPosition, state.PreviousZone, probeResult.ZoneNumber);
                state.HasTransitionPoint = true;
            }
        }

        float signedDepth;
        var indoorWeight = ComputeIndoorWeight(consumer, state, localPosition, observedIndoor, out signedDepth);
        state.PreviousProbeLocalPosition = localPosition;
        state.PreviousZone = probeResult.ZoneNumber;
        state.PreviousObservedIndoor = observedIndoor;
        _consumerStates[key] = state;

        result = new EvaluationResult
        {
            MapKey = Context.MapKey,
            ZoneNumber = probeResult.ZoneNumber,
            LeafIndex = probeResult.LeafIndex,
            CurrentZoneHasInfo = hasZoneInfo,
            DistanceFogEnabled = hasZoneInfo && zoneData.DistanceFogEnabled,
            HasDistanceFogEnd = hasZoneInfo && zoneData.HasDistanceFogEnd,
            ObservedIndoor = observedIndoor,
            IsIndoor = indoorWeight >= 0.5f,
            IndoorWeight = indoorWeight,
            SignedTransitionDepth = signedDepth,
            SunShouldAffect = sunShouldAffect,
            ZoneTag = zoneTag,
            MapAverageIndoorFogEnd = Context.MapAverageIndoorFogEnd,
            ActiveSourceFogEnd = ResolveActiveSourceFogEnd(hasZoneInfo, zoneData),
            WorldScaleFactor = ComputeWorldScaleFactor(),
            HasMapSunRotation = Context.HasSunRotation,
            MapSunEulerDegrees = Context.MapSunEulerDegrees,
            HasMapMoonRotation = Context.HasMoonRotation,
            MapMoonEulerDegrees = Context.MapMoonEulerDegrees
        };
        return true;
    }

    public static bool TryFindContaining(Vector3 worldPosition, out L2MapContextVolume volume)
    {
        var volumes = Resources.FindObjectsOfTypeAll<L2MapContextVolume>();
        for (var i = 0; i < volumes.Length; i++)
        {
            var candidate = volumes[i];
            if (candidate == null || !candidate.isActiveAndEnabled)
            {
                continue;
            }

            if (!candidate.gameObject.scene.IsValid() || candidate.transform.parent == null)
            {
                continue;
            }

            // candidate.RefreshContext();
            if (candidate.ContainsWorldPosition(worldPosition))
            {
                volume = candidate;
                return true;
            }
        }

        volume = null;
        return false;
    }

    private void RebuildCaches()
    {
        _zonesByNumber.Clear();
        mapKey = Context != null ? Context.MapKey : string.Empty;
        if (Context == null || Context.Zones == null)
        {
            boundsCenter = Vector3.zero;
            boundsSize = Vector3.zero;
            return;
        }

        for (var i = 0; i < Context.Zones.Length; i++)
        {
            _zonesByNumber[Context.Zones[i].ZoneNumber] = Context.Zones[i];
        }

        boundsCenter = (Context.WorldBoundsMinUnity + Context.WorldBoundsMaxUnity) * 0.5f;
        boundsSize = Context.WorldBoundsMaxUnity - Context.WorldBoundsMinUnity;
    }

    private void EnsureContextColliderSetup()
    {
        if (!AutoConfigureContextCollider || Context == null)
        {
            return;
        }

        _boxCollider = GetComponent<BoxCollider>();
        if (_boxCollider == null)
        {
            _boxCollider = gameObject.AddComponent<BoxCollider>();
        }

        _boxCollider.isTrigger = true;
        _boxCollider.center = boundsCenter;
        _boxCollider.size = new Vector3(
            Mathf.Max(0.01f, Mathf.Abs(boundsSize.x)),
            Mathf.Max(0.01f, Mathf.Abs(boundsSize.y)),
            Mathf.Max(0.01f, Mathf.Abs(boundsSize.z)));
    }

    private float ResolveActiveSourceFogEnd(bool hasZoneInfo, L2MapAtmosphereContextAsset.ZoneData zoneData)
    {
        if (hasZoneInfo && zoneData.HasDistanceFogEnd && zoneData.DistanceFogEnd > 0f)
        {
            return zoneData.DistanceFogEnd;
        }

        if (Context.LevelDistanceFogEnd > 0f)
        {
            return Context.LevelDistanceFogEnd;
        }

        return Context.MapAverageIndoorFogEnd;
    }

    private bool IsForcedOutdoorZone(int zoneNumber)
    {
        if (Context == null || Context.ForcedOutdoorZoneNumbers == null)
        {
            return false;
        }

        for (var i = 0; i < Context.ForcedOutdoorZoneNumbers.Length; i++)
        {
            if (Context.ForcedOutdoorZoneNumbers[i] == zoneNumber)
            {
                return true;
            }
        }

        return false;
    }

    private float ComputeWorldScaleFactor()
    {
        var scale = transform.lossyScale;
        var average = (Mathf.Abs(scale.x) + Mathf.Abs(scale.y) + Mathf.Abs(scale.z)) / 3f;
        return Mathf.Max(0.0001f, average);
    }

    private float ComputeIndoorWeight(L2CameraAtmosphereProbe consumer, ConsumerState state, Vector3 localPosition, bool rawIndoor, out float signedDepth)
    {
        if (!state.HasTransitionPoint)
        {
            signedDepth = rawIndoor
                ? Mathf.Max(0.0001f, consumer.IndoorDepthDistance)
                : -Mathf.Max(0.0001f, consumer.OutdoorApproachDistance);
            return rawIndoor ? 1f : 0f;
        }

        signedDepth = Vector3.Distance(localPosition, state.TransitionPointLocalPosition);
        if (!rawIndoor)
        {
            signedDepth = -signedDepth;
        }

        var outdoorDistance = Mathf.Max(0.0001f, consumer.OutdoorApproachDistance);
        var indoorDistance = Mathf.Max(0.0001f, consumer.IndoorDepthDistance);

        if (signedDepth <= 0f)
        {
            var outsideT = Mathf.InverseLerp(-outdoorDistance, 0f, signedDepth);
            return Mathf.SmoothStep(0f, 0.5f, outsideT);
        }

        var insideT = Mathf.InverseLerp(0f, indoorDistance, signedDepth);
        return Mathf.SmoothStep(0.5f, 1f, insideT);
    }

    private Vector3 FindTransitionPoint(Vector3 startLocalPosition, Vector3 endLocalPosition, int startZone, int endZone)
    {
        if (Context == null || startZone == endZone)
        {
            return endLocalPosition;
        }

        var start = startLocalPosition;
        var end = endLocalPosition;
        for (var i = 0; i < 14; i++)
        {
            var mid = Vector3.Lerp(start, end, 0.5f);
            var midZone = Probe(ToUnrealPosition(mid), Context.Nodes).ZoneNumber;
            if (midZone == startZone)
            {
                start = mid;
            }
            else
            {
                end = mid;
            }
        }

        return end;
    }

    private static ProbeResult Probe(Vector3 pointUnreal, L2MapAtmosphereContextAsset.ProbeNodeData[] nodes)
    {
        if (nodes == null || nodes.Length == 0)
        {
            return new ProbeResult(0, -1);
        }

        var result = ProbeNodeRecursive(0, pointUnreal, nodes, 0);
        return result.HasValue ? result.Value : new ProbeResult(0, -1);
    }

    private static ProbeResult? ProbeNodeRecursive(int nodeIndex, Vector3 pointUnreal, L2MapAtmosphereContextAsset.ProbeNodeData[] nodes, int depth)
    {
        if (nodeIndex < 0 || nodeIndex >= nodes.Length || depth > nodes.Length * 2)
        {
            return null;
        }

        var node = nodes[nodeIndex];
        var distance = Vector3.Dot(node.NormalUnreal, pointUnreal) - node.PlaneWUnreal;

        if (Mathf.Abs(distance) <= 0.01f && node.PlaneNodeIndex >= 0)
        {
            var planeResult = ProbeNodeRecursive(node.PlaneNodeIndex, pointUnreal, nodes, depth + 1);
            if (planeResult.HasValue)
            {
                return planeResult.Value;
            }
        }

        var goFront = distance >= 0f;
        var childIndex = goFront ? node.FrontNodeIndex : node.BackNodeIndex;
        if (childIndex >= 0)
        {
            var childResult = ProbeNodeRecursive(childIndex, pointUnreal, nodes, depth + 1);
            if (childResult.HasValue)
            {
                return childResult.Value;
            }
        }

        return goFront
            ? new ProbeResult(node.Zone1, node.LeafIndex1)
            : new ProbeResult(node.Zone0, node.LeafIndex0);
    }

    private static Vector3 ToUnrealPosition(Vector3 unityPosition)
    {
        return new Vector3(
            unityPosition.x * UnityToUnrealScale,
            unityPosition.z * UnityToUnrealScale,
            unityPosition.y * UnityToUnrealScale);
    }

    private void OnDrawGizmosSelected()
    {
        if (!DrawGizmo || Context == null)
        {
            return;
        }

        EnsureContextColliderSetup();
        Gizmos.color = new Color(0.15f, 0.8f, 1f, 0.18f);
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.DrawCube(boundsCenter, _boxCollider != null ? _boxCollider.size : boundsSize);
        Gizmos.color = new Color(0.15f, 0.8f, 1f, 0.7f);
        Gizmos.DrawWireCube(boundsCenter, _boxCollider != null ? _boxCollider.size : boundsSize);
    }

    [Serializable]
    public struct EvaluationResult
    {
        public string MapKey;
        public int ZoneNumber;
        public int LeafIndex;
        public bool CurrentZoneHasInfo;
        public bool DistanceFogEnabled;
        public bool HasDistanceFogEnd;
        public bool ObservedIndoor;
        public bool IsIndoor;
        public float IndoorWeight;
        public float SignedTransitionDepth;
        public bool SunShouldAffect;
        public string ZoneTag;
        public float WorldScaleFactor;
        public float MapAverageIndoorFogEnd;
        public float ActiveSourceFogEnd;
        public bool HasMapSunRotation;
        public Vector3 MapSunEulerDegrees;
        public bool HasMapMoonRotation;
        public Vector3 MapMoonEulerDegrees;
    }

    private struct ProbeResult
    {
        public ProbeResult(int zoneNumber, int leafIndex)
        {
            ZoneNumber = zoneNumber;
            LeafIndex = leafIndex;
        }

        public int ZoneNumber;
        public int LeafIndex;
    }

    private struct ConsumerState
    {
        public bool HasPreviousProbePosition;
        public Vector3 PreviousProbeLocalPosition;
        public int PreviousZone;
        public bool PreviousObservedIndoor;
        public bool HasTransitionPoint;
        public Vector3 TransitionPointLocalPosition;
    }
}
