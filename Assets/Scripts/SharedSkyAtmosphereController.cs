using System;
using UnityEngine;
using UnityEngine.Rendering;

[DefaultExecutionOrder(100)]
[DisallowMultipleComponent]
public sealed class SharedSkyAtmosphereController : MonoBehaviour
{
    [Header("Ambient lighting")]
    public Color AmbientColor = new Color(0.161f, 0.224f, 0.322f, 0.35f);
    public float AmbientIntensity = 1f;
    public float ReflectionIntensity = 0.214f;

    [Header("Fog distances")]
    public float IndoorFogAttenuationDistance = 50f;
    public float IndoorFogDistanceMultiplier = 2.5f;
    public float GlobalFogDistanceMultiplier = 1.6f;
    public float OutdoorFogDistanceMultiplier = 1.4285715f;
    public float FallbackAverageSourceFogEnd = 12000f;
    public float FogSourceScaleMin = 0.35f;
    public float FogSourceScaleMax = 2.5f;
    public float OutdoorZoneSourceFogEnd = 15000f;
    public float OutdoorZoneSourceFogEndTolerance = 1f;
    public float OutdoorZoneFogAttenuationDistance = 300f;

    [Header("Fog color")]
    public Color IndoorFogColor = new Color(0.4f, 0.43f, 0.46f, 1f);
    public Color OutdoorFogColor = new Color(0.58f, 0.66f, 0.74f, 1f);
    [Range(0f, 1f)] public float SceneAmbientFogColorBlend = 0.35f;
    public float FogColorIntensity = 0.82f;

    private SharedSkyController _sky;
    private Camera _viewer;
    private L2CameraAtmosphereProbe _probe;
    private bool _bound;
    private float _outdoorFogEnd;
    private bool _originalFogEnabled;
    private FogMode _originalFogMode;
    private float _originalFogStart;
    private float _originalFogEnd;
    private Color _originalFogColor;
    private AmbientMode _originalAmbientMode;
    private Color _originalAmbientLight;
    private float _originalAmbientIntensity;
    private float _originalReflectionIntensity;

    private void Awake()
    {
        if (!Application.isPlaying)
            return;
        _sky = GetComponent<SharedSkyController>();
        if (_sky == null)
            throw new InvalidOperationException("SharedSkyAtmosphereController requires SharedSkyController on the same object.");
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
            return;

        var viewer = _sky.ViewerCamera;
        if (viewer == null)
            throw new InvalidOperationException("SharedSky has no viewer camera.");
        if (!_bound)
            Bind(viewer);
        else if (viewer != _viewer)
            throw new InvalidOperationException("SharedSky viewer changed after atmosphere binding.");

        ApplyFog(false, Time.unscaledDeltaTime);
    }

    private void Bind(Camera viewer)
    {
        ValidateSettings();
        _viewer = viewer;
        if (!_sky.IsStandalonePreview)
        {
            var rig = viewer.GetComponent<L2CameraAtmosphereRig>();
            _probe = rig != null ? rig.Probe : null;
            if (_probe == null)
                throw new InvalidOperationException("The main camera has no atmosphere probe.");
        }

        _originalFogEnabled = RenderSettings.fog;
        _originalFogMode = RenderSettings.fogMode;
        _originalFogStart = RenderSettings.fogStartDistance;
        _originalFogEnd = RenderSettings.fogEndDistance;
        _originalFogColor = RenderSettings.fogColor;
        _originalAmbientMode = RenderSettings.ambientMode;
        _originalAmbientLight = RenderSettings.ambientLight;
        _originalAmbientIntensity = RenderSettings.ambientIntensity;
        _originalReflectionIntensity = RenderSettings.reflectionIntensity;
        if (_originalFogEnd <= _originalFogStart)
            throw new InvalidOperationException("The active scene has no valid outdoor fog distance.");
        _outdoorFogEnd = _originalFogEnd;

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = AmbientColor;
        RenderSettings.ambientIntensity = AmbientIntensity;
        RenderSettings.reflectionIntensity = ReflectionIntensity;
        _bound = true;
        ApplyFog(true, 0f);
    }

    private void ValidateSettings()
    {
        if (AmbientIntensity < 0f || ReflectionIntensity < 0f ||
            IndoorFogAttenuationDistance <= 0f || IndoorFogDistanceMultiplier <= 0f ||
            GlobalFogDistanceMultiplier <= 0f || OutdoorFogDistanceMultiplier <= 0f ||
            FallbackAverageSourceFogEnd <= 0f || FogSourceScaleMin <= 0f ||
            FogSourceScaleMax < FogSourceScaleMin || OutdoorZoneSourceFogEnd <= 0f ||
            OutdoorZoneSourceFogEndTolerance < 0f || OutdoorZoneFogAttenuationDistance <= 0f ||
            SceneAmbientFogColorBlend < 0f || SceneAmbientFogColorBlend > 1f || FogColorIntensity < 0f)
            throw new InvalidOperationException("SharedSky atmosphere settings contain an invalid fog or ambient value.");
    }

    private void ApplyFog(bool immediate, float deltaTime)
    {
        var indoorWeight = _probe != null ? _probe.IndoorWeight : 0f;
        var averageFogEnd = _probe != null && _probe.MapAverageIndoorFogEnd > 0f
            ? _probe.MapAverageIndoorFogEnd : FallbackAverageSourceFogEnd;
        var sourceFogEnd = _probe != null && _probe.ActiveSourceFogEnd > 0f
            ? _probe.ActiveSourceFogEnd : averageFogEnd;
        var sourceScale = Mathf.Clamp(sourceFogEnd / Mathf.Max(1f, averageFogEnd), FogSourceScaleMin, FogSourceScaleMax);
        var worldScale = _probe != null ? _probe.WorldScaleFactor : 1f;
        var indoorEnd = IndoorFogAttenuationDistance * IndoorFogDistanceMultiplier * sourceScale * worldScale;
        var outdoorEnd = _outdoorFogEnd * OutdoorFogDistanceMultiplier;
        if (_probe != null && _probe.CurrentZoneHasInfo && _probe.CurrentDistanceFogEnabled &&
            !_probe.IsIndoor && Mathf.Abs(sourceFogEnd - OutdoorZoneSourceFogEnd) <= OutdoorZoneSourceFogEndTolerance)
            outdoorEnd = OutdoorZoneFogAttenuationDistance * OutdoorFogDistanceMultiplier * worldScale;
        var targetEnd = Mathf.Lerp(outdoorEnd, indoorEnd, indoorWeight) * GlobalFogDistanceMultiplier;
        if (targetEnd <= 0f)
            throw new InvalidOperationException("SharedSky computed a nonpositive fog distance.");

        var targetColor = Color.Lerp(OutdoorFogColor, IndoorFogColor, indoorWeight);
        targetColor = Color.Lerp(targetColor, AmbientColor, SceneAmbientFogColorBlend) * FogColorIntensity;
        targetColor.a = 1f;
        var duration = _probe != null ? _probe.GetAtmosphereBlendDurationSeconds() : 0.01f;
        var maxDistanceStep = Mathf.Abs(outdoorEnd - indoorEnd) * GlobalFogDistanceMultiplier /
                              Mathf.Max(0.01f, duration) * deltaTime;
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 0f;
        RenderSettings.fogEndDistance = immediate
            ? targetEnd
            : Mathf.MoveTowards(RenderSettings.fogEndDistance, targetEnd, Mathf.Max(0.01f, maxDistanceStep));
        RenderSettings.fogColor = immediate
            ? targetColor
            : Color.Lerp(RenderSettings.fogColor, targetColor, Mathf.Clamp01(deltaTime / Mathf.Max(0.01f, duration)));
    }

    private void OnDisable()
    {
        Restore();
    }

    private void OnDestroy()
    {
        Restore();
    }

    private void Restore()
    {
        if (!_bound)
            return;
        _bound = false;
        RenderSettings.fog = _originalFogEnabled;
        RenderSettings.fogMode = _originalFogMode;
        RenderSettings.fogStartDistance = _originalFogStart;
        RenderSettings.fogEndDistance = _originalFogEnd;
        RenderSettings.fogColor = _originalFogColor;
        RenderSettings.ambientMode = _originalAmbientMode;
        RenderSettings.ambientLight = _originalAmbientLight;
        RenderSettings.ambientIntensity = _originalAmbientIntensity;
        RenderSettings.reflectionIntensity = _originalReflectionIntensity;
    }
}
