using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2CameraAtmosphereProbe : MonoBehaviour
{
    [Header("Context")]
    public bool UseSceneViewCameraInEditMode = true;
    public float OutdoorApproachDistance = 20f;
    public float IndoorDepthDistance = 0.08f;
    public float TransitionResetDistance = 0f;

    [Header("Performance")]
    public float ContextRefreshIntervalSeconds = 2f;

    [Header("Overlay")]
    public bool DrawOverlay = true;
    public Vector2 OverlayOffset = new Vector2(16f, 16f);

    [Header("Debug")]
    [SerializeField] private string currentMapKey = string.Empty;
    [SerializeField] private int currentZone = -1;
    [SerializeField] private int currentLeaf = -1;
    [SerializeField] private bool currentZoneHasInfo;
    [SerializeField] private bool currentDistanceFogEnabled;
    [SerializeField] private bool currentZoneHasDistanceFogEnd;
    [SerializeField] private bool observedIndoor;
    [SerializeField] private bool isIndoor;
    [SerializeField] private float indoorWeight;
    [SerializeField] private float signedTransitionDepth;
    [SerializeField] private bool sunShouldAffect = true;
    [SerializeField] private string currentZoneTag = string.Empty;
    [SerializeField] private float worldScaleFactor = 1f;
    [SerializeField] private float mapAverageIndoorFogEnd;
    [SerializeField] private float activeSourceFogEnd;
    [SerializeField] private string probeSource = "Transform";
    [SerializeField] private L2MapContextVolume currentContextVolume;

    private double _nextAllowedProbeTime;

    public event Action<L2CameraAtmosphereProbe> ProbeStateChanged;

    public L2MapContextVolume CurrentContextVolume { get { return currentContextVolume; } }
    public string CurrentMapKey { get { return currentMapKey; } }
    public int CurrentZone { get { return currentZone; } }
    public int CurrentLeaf { get { return currentLeaf; } }
    public bool CurrentZoneHasInfo { get { return currentZoneHasInfo; } }
    public bool CurrentDistanceFogEnabled { get { return currentDistanceFogEnabled; } }
    public bool CurrentZoneHasDistanceFogEnd { get { return currentZoneHasDistanceFogEnd; } }
    public bool ObservedIndoor { get { return observedIndoor; } }
    public bool IsIndoor { get { return isIndoor; } }
    public float IndoorWeight { get { return indoorWeight; } }
    public float SignedTransitionDepth { get { return signedTransitionDepth; } }
    public bool SunShouldAffect { get { return sunShouldAffect; } }
    public string CurrentZoneTag { get { return currentZoneTag; } }
    public float WorldScaleFactor { get { return worldScaleFactor; } }
    public float MapAverageIndoorFogEnd { get { return mapAverageIndoorFogEnd; } }
    public float ActiveSourceFogEnd { get { return activeSourceFogEnd; } }
    public bool HasMapSunRotation { get { return currentContextVolume != null && currentContextVolume.Context != null && currentContextVolume.Context.HasSunRotation; } }
    public Vector3 MapSunEulerDegrees { get { return currentContextVolume != null && currentContextVolume.Context != null ? currentContextVolume.Context.MapSunEulerDegrees : Vector3.zero; } }
    public bool HasMapMoonRotation { get { return currentContextVolume != null && currentContextVolume.Context != null && currentContextVolume.Context.HasMoonRotation; } }
    public Vector3 MapMoonEulerDegrees { get { return currentContextVolume != null && currentContextVolume.Context != null ? currentContextVolume.Context.MapMoonEulerDegrees : Vector3.zero; } }

    private void OnEnable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
        EditorApplication.update += EditorTick;
#endif
        ForceUpdateProbe();
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
#endif
        if (currentContextVolume != null)
        {
            currentContextVolume.ClearConsumer(this);
        }
    }

    private void OnValidate()
    {
        ForceUpdateProbe();
    }

    private void FixedUpdate()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        TryUpdateProbe();
    }

#if UNITY_EDITOR
    private void EditorTick()
    {
        if (Application.isPlaying || this == null || !isActiveAndEnabled)
        {
            return;
        }

        if (TryUpdateProbe())
        {
            SceneView.RepaintAll();
        }
    }
#endif

    public float GetAtmosphereBlendDurationSeconds()
    {
        return Mathf.Max(0.0001f, ContextRefreshIntervalSeconds);
    }

    private bool TryUpdateProbe()
    {
        if (!ShouldRefreshProbe())
        {
            return false;
        }

        return UpdateProbe();
    }

    private void ForceUpdateProbe()
    {
        _nextAllowedProbeTime = GetCurrentProbeTime() + GetRefreshIntervalSeconds();
        if (UpdateProbe())
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                SceneView.RepaintAll();
            }
#endif
        }
    }

    private bool ShouldRefreshProbe()
    {
        var now = GetCurrentProbeTime();
        if (now < _nextAllowedProbeTime)
        {
            return false;
        }

        _nextAllowedProbeTime = now + GetRefreshIntervalSeconds();
        return true;
    }

    private double GetCurrentProbeTime()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            return EditorApplication.timeSinceStartup;
        }
#endif
        return Time.unscaledTimeAsDouble;
    }

    private double GetRefreshIntervalSeconds()
    {
        return Mathf.Max(0f, ContextRefreshIntervalSeconds);
    }

    public float GetTransitionResetDistance()
    {
        if (TransitionResetDistance > 0f)
        {
            return Mathf.Max(0.0001f, TransitionResetDistance);
        }

        return Mathf.Max(OutdoorApproachDistance * 2f, IndoorDepthDistance * 8f, 1f);
    }

    private bool UpdateProbe()
    {
        var previousState = CaptureState();
        Vector3 worldPosition;
        probeSource = ResolveProbeWorldPosition(out worldPosition);

        if (currentContextVolume == null ||
            !currentContextVolume.isActiveAndEnabled ||
            !currentContextVolume.ContainsWorldPosition(worldPosition))
        {
            L2MapContextVolume resolved;
            if (L2MapContextVolume.TryFindContaining(worldPosition, out resolved))
            {
                currentContextVolume = resolved;
            }
            else
            {
                currentContextVolume = null;
            }
        }

        if (currentContextVolume == null)
        {
            ResetState();
            return NotifyIfStateChanged(previousState);
        }

        L2MapContextVolume.EvaluationResult result;
        if (!currentContextVolume.TryEvaluate(this, worldPosition, out result))
        {
            ResetState();
            return NotifyIfStateChanged(previousState);
        }

        currentMapKey = result.MapKey ?? string.Empty;
        currentZone = result.ZoneNumber;
        currentLeaf = result.LeafIndex;
        currentZoneHasInfo = result.CurrentZoneHasInfo;
        currentDistanceFogEnabled = result.DistanceFogEnabled;
        currentZoneHasDistanceFogEnd = result.HasDistanceFogEnd;
        observedIndoor = result.ObservedIndoor;
        isIndoor = result.IsIndoor;
        indoorWeight = result.IndoorWeight;
        signedTransitionDepth = result.SignedTransitionDepth;
        sunShouldAffect = result.SunShouldAffect;
        currentZoneTag = result.ZoneTag ?? string.Empty;
        worldScaleFactor = result.WorldScaleFactor;
        mapAverageIndoorFogEnd = result.MapAverageIndoorFogEnd;
        activeSourceFogEnd = result.ActiveSourceFogEnd;
        return NotifyIfStateChanged(previousState);
    }

    private string ResolveProbeWorldPosition(out Vector3 worldPosition)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && UseSceneViewCameraInEditMode)
        {
            var sceneView = SceneView.lastActiveSceneView;
            if (sceneView != null && sceneView.camera != null)
            {
                worldPosition = sceneView.camera.transform.position;
                return "SceneView";
            }
        }
#endif
        worldPosition = transform.position;
        return "Transform";
    }

    private void ResetState()
    {
        currentMapKey = string.Empty;
        currentZone = -1;
        currentLeaf = -1;
        currentZoneHasInfo = false;
        currentDistanceFogEnabled = false;
        currentZoneHasDistanceFogEnd = false;
        observedIndoor = false;
        isIndoor = false;
        indoorWeight = 0f;
        signedTransitionDepth = 0f;
        sunShouldAffect = true;
        currentZoneTag = string.Empty;
        worldScaleFactor = 1f;
        mapAverageIndoorFogEnd = 0f;
        activeSourceFogEnd = 0f;
    }

    private ProbeStateSnapshot CaptureState()
    {
        return new ProbeStateSnapshot(
            currentMapKey,
            currentZone,
            currentLeaf,
            currentZoneHasInfo,
            currentDistanceFogEnabled,
            currentZoneHasDistanceFogEnd,
            observedIndoor,
            isIndoor,
            indoorWeight,
            signedTransitionDepth,
            sunShouldAffect,
            currentZoneTag,
            worldScaleFactor,
            mapAverageIndoorFogEnd,
            activeSourceFogEnd,
            probeSource,
            currentContextVolume);
    }

    private bool NotifyIfStateChanged(ProbeStateSnapshot previousState)
    {
        var changed = CaptureState().DiffersFrom(previousState);
        if (changed)
        {
            var handler = ProbeStateChanged;
            if (handler != null)
            {
                handler(this);
            }
        }

        return changed;
    }

    private readonly struct ProbeStateSnapshot
    {
        private const float FloatEpsilon = 0.0001f;

        private readonly string _mapKey;
        private readonly int _zone;
        private readonly int _leaf;
        private readonly bool _zoneHasInfo;
        private readonly bool _distanceFogEnabled;
        private readonly bool _zoneHasDistanceFogEnd;
        private readonly bool _observedIndoor;
        private readonly bool _isIndoor;
        private readonly float _indoorWeight;
        private readonly float _signedTransitionDepth;
        private readonly bool _sunShouldAffect;
        private readonly string _zoneTag;
        private readonly float _worldScaleFactor;
        private readonly float _mapAverageIndoorFogEnd;
        private readonly float _activeSourceFogEnd;
        private readonly string _probeSource;
        private readonly L2MapContextVolume _contextVolume;

        public ProbeStateSnapshot(
            string mapKey,
            int zone,
            int leaf,
            bool zoneHasInfo,
            bool distanceFogEnabled,
            bool zoneHasDistanceFogEnd,
            bool observedIndoor,
            bool isIndoor,
            float indoorWeight,
            float signedTransitionDepth,
            bool sunShouldAffect,
            string zoneTag,
            float worldScaleFactor,
            float mapAverageIndoorFogEnd,
            float activeSourceFogEnd,
            string probeSource,
            L2MapContextVolume contextVolume)
        {
            _mapKey = mapKey;
            _zone = zone;
            _leaf = leaf;
            _zoneHasInfo = zoneHasInfo;
            _distanceFogEnabled = distanceFogEnabled;
            _zoneHasDistanceFogEnd = zoneHasDistanceFogEnd;
            _observedIndoor = observedIndoor;
            _isIndoor = isIndoor;
            _indoorWeight = indoorWeight;
            _signedTransitionDepth = signedTransitionDepth;
            _sunShouldAffect = sunShouldAffect;
            _zoneTag = zoneTag;
            _worldScaleFactor = worldScaleFactor;
            _mapAverageIndoorFogEnd = mapAverageIndoorFogEnd;
            _activeSourceFogEnd = activeSourceFogEnd;
            _probeSource = probeSource;
            _contextVolume = contextVolume;
        }

        public bool DiffersFrom(ProbeStateSnapshot other)
        {
            return !string.Equals(_mapKey, other._mapKey, StringComparison.Ordinal) ||
                   _zone != other._zone ||
                   _leaf != other._leaf ||
                   _zoneHasInfo != other._zoneHasInfo ||
                   _distanceFogEnabled != other._distanceFogEnabled ||
                   _zoneHasDistanceFogEnd != other._zoneHasDistanceFogEnd ||
                   _observedIndoor != other._observedIndoor ||
                   _isIndoor != other._isIndoor ||
                   Mathf.Abs(_indoorWeight - other._indoorWeight) > FloatEpsilon ||
                   Mathf.Abs(_signedTransitionDepth - other._signedTransitionDepth) > FloatEpsilon ||
                   _sunShouldAffect != other._sunShouldAffect ||
                   !string.Equals(_zoneTag, other._zoneTag, StringComparison.Ordinal) ||
                   Mathf.Abs(_worldScaleFactor - other._worldScaleFactor) > FloatEpsilon ||
                   Mathf.Abs(_mapAverageIndoorFogEnd - other._mapAverageIndoorFogEnd) > FloatEpsilon ||
                   Mathf.Abs(_activeSourceFogEnd - other._activeSourceFogEnd) > FloatEpsilon ||
                   !string.Equals(_probeSource, other._probeSource, StringComparison.Ordinal) ||
                   !ReferenceEquals(_contextVolume, other._contextVolume);
        }
    }

    private void OnGUI()
    {
        if (!DrawOverlay)
        {
            return;
        }

        var lines = new List<string>
        {
            "L2 Camera Atmosphere Probe",
            "Source: " + probeSource,
            "Map: " + (string.IsNullOrWhiteSpace(currentMapKey) ? "<none>" : currentMapKey),
            "Zone: " + currentZone,
            "Leaf: " + currentLeaf,
            "ZoneInfo: " + (currentZoneHasInfo ? "yes" : "no"),
            "DistanceFogEnabled: " + (currentDistanceFogEnabled ? "yes" : "no"),
            "Observed Indoor: " + (observedIndoor ? "yes" : "no"),
            "Indoor: " + (isIndoor ? "yes" : "no"),
            string.Format("Indoor Weight: {0:0.##}", indoorWeight),
            string.Format("Boundary Depth: {0:0.##}", signedTransitionDepth),
            string.Format("World Scale: {0:0.###}", worldScaleFactor),
            string.Format("Map Avg FogEnd: {0:0.##}", mapAverageIndoorFogEnd),
            string.Format("Active FogEnd: {0:0.##}", activeSourceFogEnd),
            string.Format("Refresh Interval: {0:0.##} sec", ContextRefreshIntervalSeconds)
        };

        if (!string.IsNullOrWhiteSpace(currentZoneTag))
        {
            lines.Add("ZoneTag: " + currentZoneTag);
        }

        var content = string.Join("\n", lines.ToArray());
        var size = GUI.skin.box.CalcSize(new GUIContent(content));
        var rect = new Rect(
            OverlayOffset.x,
            OverlayOffset.y,
            Mathf.Max(280f, size.x + 20f),
            Mathf.Max(120f, size.y + 20f));
        GUI.Box(rect, content);
    }
}
