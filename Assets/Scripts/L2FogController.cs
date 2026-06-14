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
    public bool UseMapAverageFogCalibration = true;
    public float FallbackAverageSourceFogEnd = 12000f;
    public float FogSourceScaleMin = 0.35f;
    public float FogSourceScaleMax = 2.5f;

    [Header("Outdoor Zone Override")]
    public bool UseOutdoorZoneFogOverride = true;
    public float OutdoorZoneSourceFogEnd = 15000f;
    public float OutdoorZoneSourceFogEndTolerance = 1f;
    public float OutdoorZoneFogAttenuationDistance = 300f;

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
    private double _lastUpdateTime = -1d;

    public bool FogAvailable { get { return fogAvailable; } }
    public bool UsingOutdoorZoneOverride { get { return usingOutdoorZoneOverride; } }

    private void OnEnable()
    {
        EnsureReferences();
        UpdateFog();
    }

    private void OnValidate()
    {
        EnsureReferences();
        UpdateFog();
    }

    private void Update()
    {
        EnsureReferences();
        UpdateFog();
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

    private void UpdateFog()
    {
        fogAvailable = false;
        targetFogAttenuationDistance = 0f;
        currentFogAttenuationDistance = 0f;
        targetVolumetricFogDistance = 0f;
        currentVolumetricFogDistance = 0f;

        if (!AdjustFog || Probe == null)
        {
            return;
        }

        fogAvailable = true;
        if (!_capturedOutdoorFogValues)
        {
            _outdoorFogAttenuationDistance = Mathf.Max(1f, RenderSettings.fogEndDistance);
            _outdoorVolumetricFogDistance = Mathf.Max(0.01f, RenderSettings.fogEndDistance - RenderSettings.fogStartDistance);
            _capturedOutdoorFogValues = true;
        }

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
        effectiveIndoorFogAttenuationDistance = IndoorFogAttenuationDistance * fogSourceScale * worldScale;
        effectiveIndoorVolumetricFogDistance = IndoorVolumetricFogDistance * fogSourceScale * worldScale;

        var blend = Probe.IndoorWeight;
        var outdoorFogAttenuation = _capturedOutdoorFogValues ? _outdoorFogAttenuationDistance : IndoorFogAttenuationDistance;
        var outdoorVolumetricDistance = _capturedOutdoorFogValues ? _outdoorVolumetricFogDistance : IndoorVolumetricFogDistance;

        if (ShouldUseOutdoorZoneFogOverride(activeSourceFogEnd))
        {
            outdoorFogAttenuation = OutdoorZoneFogAttenuationDistance * worldScale;
            usingOutdoorZoneOverride = true;
        }

        targetFogAttenuationDistance = Mathf.Lerp(outdoorFogAttenuation, effectiveIndoorFogAttenuationDistance, blend);
        targetVolumetricFogDistance = Mathf.Lerp(outdoorVolumetricDistance, effectiveIndoorVolumetricFogDistance, blend);

        var deltaTime = GetDeltaTime();
        var blendDuration = Probe.GetAtmosphereBlendDurationSeconds();
        var blendFactor = blendDuration <= 0.0001f
            ? 1f
            : Mathf.Clamp01(deltaTime / blendDuration);

        var currentFogEnd = RenderSettings.fogEndDistance > 0f ? RenderSettings.fogEndDistance : targetFogAttenuationDistance;
        var currentFogRange = Mathf.Max(0.01f, currentFogEnd - RenderSettings.fogStartDistance);
        var nextFogEnd = Mathf.Lerp(currentFogEnd, targetFogAttenuationDistance, blendFactor);
        var nextFogRange = Mathf.Lerp(currentFogRange, targetVolumetricFogDistance, blendFactor);
        var nextFogStart = Mathf.Max(0f, nextFogEnd - Mathf.Max(0.01f, nextFogRange));

        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = nextFogStart;
        RenderSettings.fogEndDistance = Mathf.Max(nextFogStart + 0.01f, nextFogEnd);

        currentFogAttenuationDistance = RenderSettings.fogEndDistance;
        currentVolumetricFogDistance = RenderSettings.fogEndDistance - RenderSettings.fogStartDistance;
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

    private float GetDeltaTime()
    {
        if (Application.isPlaying)
        {
            return Time.unscaledDeltaTime;
        }

#if UNITY_EDITOR
        var now = EditorApplication.timeSinceStartup;
        if (_lastUpdateTime < 0d)
        {
            _lastUpdateTime = now;
            return 0f;
        }

        var delta = (float)(now - _lastUpdateTime);
        _lastUpdateTime = now;
        return Mathf.Max(0f, delta);
#else
        return 0f;
#endif
    }
}
