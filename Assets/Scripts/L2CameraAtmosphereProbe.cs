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

        UpdateProbe();
        return true;
    }

    private void ForceUpdateProbe()
    {
        _nextAllowedProbeTime = GetCurrentProbeTime() + GetRefreshIntervalSeconds();
        UpdateProbe();
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

    private void UpdateProbe()
    {
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
            return;
        }

        L2MapContextVolume.EvaluationResult result;
        if (!currentContextVolume.TryEvaluate(this, worldPosition, out result))
        {
            ResetState();
            return;
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
