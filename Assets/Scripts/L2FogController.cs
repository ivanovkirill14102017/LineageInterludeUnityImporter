using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2FogController : MonoBehaviour
{
    [Header("References")]
    public L2CameraAtmosphereProbe Probe;
    public bool AutoFindReferences = true;

    [Header("Indoor Fog")]
    public bool AdjustFog = true;
    public float IndoorFogAttenuationDistance = 50f;
    public float IndoorVolumetricFogDistance = 2f;
    public float IndoorFogDistanceMultiplier = 2.5f;
    public float GlobalFogDistanceMultiplier = 1.6f;
    public bool UseFullDistanceFogGradient = true;
    public bool UseMapAverageFogCalibration = true;
    public float FallbackAverageSourceFogEnd = 12000f;
    public float FogSourceScaleMin = 0.35f;
    public float FogSourceScaleMax = 2.5f;

    [Header("Fog Color")]
    public bool AdjustFogColor = true;
    public Color IndoorFogColor = new Color(0.40f, 0.43f, 0.46f, 1f);
    public Color OutdoorFogColor = new Color(0.58f, 0.66f, 0.74f, 1f);
    [Range(0f, 1f)] public float SceneAmbientFogColorBlend = 0.35f;
    public float FogColorIntensity = 0.82f;

    [Header("Outdoor Zone Override")]
    public bool UseOutdoorZoneFogOverride = true;
    public float OutdoorZoneSourceFogEnd = 15000f;
    public float OutdoorZoneSourceFogEndTolerance = 1f;
    public float OutdoorZoneFogAttenuationDistance = 300f;
    public float OutdoorFogDistanceMultiplier = 1.4285715f;

    [Header("Debug")]
    [SerializeField] private bool fogAvailable;
    [SerializeField] private bool usingOutdoorZoneOverride;
    [SerializeField] private float fogSourceScale = 1f;
    [SerializeField] private float effectiveIndoorFogAttenuationDistance;
    [SerializeField] private float effectiveIndoorVolumetricFogDistance;
    [SerializeField] private float targetFogAttenuationDistance;
    [SerializeField] private float currentFogAttenuationDistance;
    [SerializeField] private float targetVolumetricFogDistance;
    [SerializeField] private float currentVolumetricFogDistance;

    private bool _capturedOutdoorFogValues;
    private float _outdoorFogAttenuationDistance = -1f;
    private float _outdoorVolumetricFogDistance = -1f;
    private L2CameraAtmosphereProbe _boundProbe;
    private bool _transitionActive;
    private double _transitionStartTime;
    private float _transitionDurationSeconds;
    private float _transitionStartFogEndDistance;
    private float _transitionStartFogRange;
    private float _transitionTargetFogEndDistance;
    private float _transitionTargetFogRange;

    public bool FogAvailable { get { return fogAvailable; } }
    public bool UsingOutdoorZoneOverride { get { return usingOutdoorZoneOverride; } }

    private void OnEnable()
    {
        EnsureReferences();
        RebindProbe();
        RefreshFogFromContext(immediate: true);
    }

    private void OnDisable()
    {
        UnbindProbe();
        StopTransition();
    }

    private void OnValidate()
    {
        EnsureReferences();
        RebindProbe();
        RefreshFogFromContext(immediate: true);
    }

    private void Update()
    {
        if (!Application.isPlaying || !_transitionActive)
        {
            return;
        }

        TickTransition();
    }

#if UNITY_EDITOR
    private void EditorTick()
    {
        if (Application.isPlaying || this == null || !_transitionActive)
        {
            return;
        }

        TickTransition();
    }
#endif

    public void BindToProbe(L2CameraAtmosphereProbe probe)
    {
        Probe = probe;
        AutoFindReferences = false;
        RebindProbe();
        RefreshFogFromContext(immediate: true);
    }

    private void EnsureReferences()
    {
        if (!AutoFindReferences)
        {
            return;
        }

        if (Probe == null)
        {
            Probe = GetComponent<L2CameraAtmosphereProbe>() ?? GetComponentInParent<L2CameraAtmosphereProbe>();
        }
    }

    private void RebindProbe()
    {
        if (ReferenceEquals(_boundProbe, Probe))
        {
            return;
        }

        if (_boundProbe != null)
        {
            _boundProbe.ProbeStateChanged -= OnProbeStateChanged;
        }

        _boundProbe = Probe;
        if (_boundProbe != null)
        {
            _boundProbe.ProbeStateChanged += OnProbeStateChanged;
        }
    }

    private void UnbindProbe()
    {
        if (_boundProbe == null)
        {
            return;
        }

        _boundProbe.ProbeStateChanged -= OnProbeStateChanged;
        _boundProbe = null;
    }

    private void OnProbeStateChanged(L2CameraAtmosphereProbe probe)
    {
        if (!ReferenceEquals(probe, Probe))
        {
            return;
        }

        RefreshFogFromContext(immediate: false);
    }

    private void RefreshFogFromContext(bool immediate)
    {
        EnsureReferences();
        ResetDebugState();

        if (!AdjustFog || Probe == null)
        {
            StopTransition();
            return;
        }

        fogAvailable = true;
        CaptureOutdoorFogValuesIfNeeded();

        float resolvedTargetFogEnd;
        float resolvedTargetFogRange;
        ResolveFogTargets(out resolvedTargetFogEnd, out resolvedTargetFogRange);

        targetFogAttenuationDistance = resolvedTargetFogEnd;
        targetVolumetricFogDistance = resolvedTargetFogRange;

        if (immediate)
        {
            StopTransition();
            ApplyFogSettings(resolvedTargetFogEnd, resolvedTargetFogRange);
            RepaintSceneView();
            return;
        }

        StartTransition(resolvedTargetFogEnd, resolvedTargetFogRange, Probe.GetAtmosphereBlendDurationSeconds());
    }

    private void CaptureOutdoorFogValuesIfNeeded()
    {
        if (_capturedOutdoorFogValues)
        {
            return;
        }

        _outdoorFogAttenuationDistance = Mathf.Max(1f, RenderSettings.fogEndDistance);
        _outdoorVolumetricFogDistance = Mathf.Max(0.01f, RenderSettings.fogEndDistance - RenderSettings.fogStartDistance);
        _capturedOutdoorFogValues = true;
    }

    private void ResolveFogTargets(out float resolvedTargetFogEnd, out float resolvedTargetFogRange)
    {
        var averageFogEnd = Probe.MapAverageIndoorFogEnd > 0f ? Probe.MapAverageIndoorFogEnd : Mathf.Max(1f, FallbackAverageSourceFogEnd);
        var activeSourceFogEnd = Probe.ActiveSourceFogEnd > 0f ? Probe.ActiveSourceFogEnd : averageFogEnd;
        fogSourceScale = 1f;
        usingOutdoorZoneOverride = false;
        if (UseMapAverageFogCalibration && averageFogEnd > 0f)
        {
            fogSourceScale = Mathf.Clamp(
                activeSourceFogEnd / averageFogEnd,
                Mathf.Max(0.01f, FogSourceScaleMin),
                Mathf.Max(FogSourceScaleMin, FogSourceScaleMax));
        }

        var worldScale = Mathf.Max(0.0001f, Probe.WorldScaleFactor);
        var indoorFogDistanceMultiplier = Mathf.Max(0.01f, IndoorFogDistanceMultiplier);
        effectiveIndoorFogAttenuationDistance = IndoorFogAttenuationDistance * indoorFogDistanceMultiplier * fogSourceScale * worldScale;
        effectiveIndoorVolumetricFogDistance = IndoorVolumetricFogDistance * indoorFogDistanceMultiplier * fogSourceScale * worldScale;

        var blend = Probe.IndoorWeight;
        var outdoorFogDistanceMultiplier = Mathf.Max(0.01f, OutdoorFogDistanceMultiplier);
        var outdoorFogAttenuation = (_capturedOutdoorFogValues ? _outdoorFogAttenuationDistance : IndoorFogAttenuationDistance) * outdoorFogDistanceMultiplier;
        var outdoorVolumetricDistance = (_capturedOutdoorFogValues ? _outdoorVolumetricFogDistance : IndoorVolumetricFogDistance) * outdoorFogDistanceMultiplier;

        if (ShouldUseOutdoorZoneFogOverride(activeSourceFogEnd))
        {
            outdoorFogAttenuation = OutdoorZoneFogAttenuationDistance * outdoorFogDistanceMultiplier * worldScale;
            usingOutdoorZoneOverride = true;
        }

        var globalFogDistanceMultiplier = Mathf.Max(0.01f, GlobalFogDistanceMultiplier);
        resolvedTargetFogEnd = Mathf.Lerp(outdoorFogAttenuation, effectiveIndoorFogAttenuationDistance, blend) * globalFogDistanceMultiplier;
        resolvedTargetFogRange = Mathf.Lerp(outdoorVolumetricDistance, effectiveIndoorVolumetricFogDistance, blend) * globalFogDistanceMultiplier;
    }

    private void StartTransition(float targetFogEnd, float targetFogRange, float durationSeconds)
    {
        var currentFogEnd = RenderSettings.fogEndDistance > 0f ? RenderSettings.fogEndDistance : targetFogEnd;
        var currentFogRange = Mathf.Max(0.01f, currentFogEnd - RenderSettings.fogStartDistance);

        if (Approximately(currentFogEnd, targetFogEnd) && Approximately(currentFogRange, targetFogRange))
        {
            StopTransition();
            ApplyFogSettings(targetFogEnd, targetFogRange);
            RepaintSceneView();
            return;
        }

        _transitionStartFogEndDistance = currentFogEnd;
        _transitionStartFogRange = currentFogRange;
        _transitionTargetFogEndDistance = targetFogEnd;
        _transitionTargetFogRange = targetFogRange;
        _transitionDurationSeconds = Mathf.Max(0.0001f, durationSeconds);
        _transitionStartTime = GetCurrentTime();
        _transitionActive = true;

        BeginTransition();
        ApplyTransitionState(0f);
        RepaintSceneView();
    }

    private void TickTransition()
    {
        if (!_transitionActive)
        {
            return;
        }

        var elapsed = Mathf.Max(0f, (float)(GetCurrentTime() - _transitionStartTime));
        var normalizedTime = Mathf.Clamp01(elapsed / Mathf.Max(0.0001f, _transitionDurationSeconds));
        ApplyTransitionState(normalizedTime);

        if (normalizedTime >= 1f)
        {
            StopTransition();
        }

        RepaintSceneView();
    }

    private void ApplyTransitionState(float normalizedTime)
    {
        var fogEnd = Mathf.Lerp(_transitionStartFogEndDistance, _transitionTargetFogEndDistance, normalizedTime);
        var fogRange = Mathf.Lerp(_transitionStartFogRange, _transitionTargetFogRange, normalizedTime);
        ApplyFogSettings(fogEnd, fogRange);
    }

    private void ApplyFogSettings(float fogEndDistance, float fogRange)
    {
        var clampedFogEnd = Mathf.Max(0.01f, fogEndDistance);
        var clampedFogRange = Mathf.Max(0.01f, fogRange);
        var fogStartDistance = UseFullDistanceFogGradient
            ? 0f
            : Mathf.Max(0f, clampedFogEnd - clampedFogRange);

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = fogStartDistance;
        RenderSettings.fogEndDistance = Mathf.Max(fogStartDistance + 0.01f, clampedFogEnd);
        ApplyFogColor();

        currentFogAttenuationDistance = RenderSettings.fogEndDistance;
        currentVolumetricFogDistance = RenderSettings.fogEndDistance - RenderSettings.fogStartDistance;
    }

    private void ApplyFogColor()
    {
        if (!AdjustFogColor)
        {
            return;
        }

        var indoorWeight = Probe != null ? Probe.IndoorWeight : 0f;
        var targetColor = Color.Lerp(OutdoorFogColor, IndoorFogColor, indoorWeight);
        var ambientColor = ResolveSceneAmbientColor();
        targetColor = Color.Lerp(targetColor, ambientColor, Mathf.Clamp01(SceneAmbientFogColorBlend));
        targetColor *= Mathf.Max(0f, FogColorIntensity);
        targetColor.a = 1f;
        RenderSettings.fogColor = targetColor;
    }

    private static Color ResolveSceneAmbientColor()
    {
        if (RenderSettings.ambientMode == UnityEngine.Rendering.AmbientMode.Trilight)
        {
            return Color.Lerp(RenderSettings.ambientEquatorColor, RenderSettings.ambientSkyColor, 0.5f);
        }

        return RenderSettings.ambientLight.maxColorComponent > 0.001f
            ? RenderSettings.ambientLight
            : RenderSettings.fogColor;
    }

    private void ResetDebugState()
    {
        fogAvailable = false;
        usingOutdoorZoneOverride = false;
        fogSourceScale = 1f;
        effectiveIndoorFogAttenuationDistance = 0f;
        effectiveIndoorVolumetricFogDistance = 0f;
        targetFogAttenuationDistance = 0f;
        currentFogAttenuationDistance = 0f;
        targetVolumetricFogDistance = 0f;
        currentVolumetricFogDistance = 0f;
    }

    private bool ShouldUseOutdoorZoneFogOverride(float activeSourceFogEnd)
    {
        if (!UseOutdoorZoneFogOverride || Probe == null)
        {
            return false;
        }

        if (!Probe.CurrentZoneHasInfo || !Probe.CurrentDistanceFogEnabled || Probe.IsIndoor)
        {
            return false;
        }

        return Mathf.Abs(activeSourceFogEnd - OutdoorZoneSourceFogEnd) <= Mathf.Max(0f, OutdoorZoneSourceFogEndTolerance);
    }

    private void BeginTransition()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorApplication.update -= EditorTick;
            EditorApplication.update += EditorTick;
        }
#endif
    }

    private void StopTransition()
    {
        _transitionActive = false;
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
#endif
    }

    private double GetCurrentTime()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            return EditorApplication.timeSinceStartup;
        }
#endif
        return Time.unscaledTimeAsDouble;
    }

    private static bool Approximately(float left, float right)
    {
        return Mathf.Abs(left - right) <= 0.0001f;
    }

    private static void RepaintSceneView()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            SceneView.RepaintAll();
        }
#endif
    }
}
