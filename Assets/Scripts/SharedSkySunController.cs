using System;
using UnityEngine;

[DefaultExecutionOrder(110)]
[DisallowMultipleComponent]
public sealed class SharedSkySunController : MonoBehaviour
{
    public Light SunLight;
    public Light MoonLight;
    public float OutdoorSunIntensity = 4f;
    public float NightOutdoorIntensity = 0.8f;
    public float MoonOutdoorIntensity = 0.25f;
    public float IndoorSunIntensity;
    public Color OutdoorSunColor = new Color(1f, 0.95686275f, 0.8392157f, 1f);
    public Color NightOutdoorColor = new Color(0.55f, 0.7f, 1f, 1f);
    public Color MoonColor = new Color(0.55f, 0.68f, 1f, 1f);
    public float SunTransitionSeconds = 3f;

    private SharedSkyController _sky;
    private Camera _viewer;
    private L2CameraAtmosphereProbe _probe;
    private bool _bound;
    private Light _originalRenderSun;
    private float _indoorBlend;

    private void Awake()
    {
        if (!Application.isPlaying)
            return;
        _sky = GetComponent<SharedSkyController>();
        if (_sky == null)
            throw new InvalidOperationException("SharedSkySunController requires SharedSkyController on the same object.");
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
            throw new InvalidOperationException("SharedSky viewer changed after light binding.");

        _indoorBlend = Mathf.MoveTowards(_indoorBlend,
            _probe != null ? _probe.IndoorWeight : 0f,
            Time.unscaledDeltaTime / SunTransitionSeconds);
        ApplyLights();
    }

    private void Bind(Camera viewer)
    {
        if (SunLight == null || MoonLight == null || SunLight.type != LightType.Directional ||
            MoonLight.type != LightType.Directional || OutdoorSunIntensity <= 0f ||
            NightOutdoorIntensity <= 0f || MoonOutdoorIntensity < 0f ||
            IndoorSunIntensity < 0f || SunTransitionSeconds <= 0f)
            throw new InvalidOperationException("SharedSky light setup is invalid. Import the sky scene.");

        _viewer = viewer;
        if (!_sky.IsStandalonePreview)
        {
            var rig = viewer.GetComponent<L2CameraAtmosphereRig>();
            _probe = rig != null ? rig.Probe : null;
            if (_probe == null)
                throw new InvalidOperationException("The main camera has no atmosphere probe.");
        }

        _originalRenderSun = RenderSettings.sun;
        RenderSettings.sun = SunLight;
        _indoorBlend = _probe != null ? _probe.IndoorWeight : 0f;
        _bound = true;
        ApplyLights();
    }

    private void ApplyLights()
    {
        var daylight = Mathf.SmoothStep(0f, 1f,
            Mathf.InverseLerp(-0.05f, 0.25f, _sky.SunDirection.y));
        var sunLightDirection = _sky.SunDirection;
        sunLightDirection.y = Mathf.Abs(sunLightDirection.y);
        SunLight.transform.rotation = Quaternion.LookRotation(-sunLightDirection);
        MoonLight.transform.rotation = Quaternion.LookRotation(_sky.SunDirection);
        var outdoorIntensity = Mathf.Lerp(NightOutdoorIntensity, OutdoorSunIntensity, daylight);
        SunLight.intensity = Mathf.Lerp(outdoorIntensity, IndoorSunIntensity, _indoorBlend);
        SunLight.color = Color.Lerp(NightOutdoorColor, OutdoorSunColor, daylight);
        SunLight.enabled = SunLight.intensity > 0.001f;

        var moonVisibility = Mathf.Clamp01((-_sky.SunDirection.y + 0.05f) / 0.15f);
        MoonLight.intensity = MoonOutdoorIntensity * (1f - daylight) * moonVisibility * (1f - _indoorBlend);
        MoonLight.color = MoonColor;
        MoonLight.enabled = MoonLight.intensity > 0.001f;
    }

    private void OnDisable() => Restore();
    private void OnDestroy() => Restore();

    private void Restore()
    {
        if (!_bound)
            return;
        _bound = false;
        RenderSettings.sun = _originalRenderSun;
        if (SunLight != null)
            SunLight.enabled = false;
        if (MoonLight != null)
            MoonLight.enabled = false;
    }
}
