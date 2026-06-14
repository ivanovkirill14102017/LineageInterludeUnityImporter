using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2SunController : MonoBehaviour
{
    [Header("References")]
    public L2CameraAtmosphereProbe Probe;
    public L2DayNightController DayNight;
    public Light Sun;
    public bool AutoFindReferences = true;

    [Header("Indoor Sun")]
    public float OutdoorIntensityOverride = -1f;
    public float IndoorIntensity = 0f;
    public float TransitionDurationSeconds = 3f;
    public float DisabledIntensityThreshold = 0.001f;
    public float MaxSunIntensity = 4f;
    public float MaxColorTemperatureDeltaPerSecond = 2000f;

    [Header("Debug")]
    [SerializeField] private float targetSunIntensity;
    [SerializeField] private float currentSunIntensity;
    [SerializeField] private float targetSunColorTemperature;
    [SerializeField] private float currentSunColorTemperature;

    private bool _capturedOutdoorIntensity;
    private float _capturedOutdoorIntensityValue = 1f;
    private double _lastUpdateTime = -1d;
    private bool _cachedShadowModeCaptured;
    private LightShadows _cachedShadowMode = LightShadows.None;

    private void OnEnable()
    {
        EnsureReferences();
        UpdateSun();
    }

    private void OnValidate()
    {
        EnsureReferences();
        UpdateSun();
    }

    private void Update()
    {
        EnsureReferences();
        UpdateSun();
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

        if (DayNight == null)
        {
            DayNight = GetComponent<L2DayNightController>();
        }

        if (Sun == null)
        {
            Sun = DayNight != null ? DayNight.Sun : RenderSettings.sun;
        }

        if (!_capturedOutdoorIntensity && Sun != null)
        {
            _capturedOutdoorIntensityValue = Sun.intensity;
            _cachedShadowMode = Sun.shadows;
            _cachedShadowModeCaptured = true;
            _capturedOutdoorIntensity = true;
        }
    }

    private void UpdateSun()
    {
        var outdoorIntensity = ResolveOutdoorIntensity();
        var blend = Probe != null ? Probe.IndoorWeight : 0f;
        targetSunIntensity = Mathf.Lerp(outdoorIntensity, IndoorIntensity, blend);
        targetSunColorTemperature = DayNight != null
            ? DayNight.CurrentOutdoorSunColorTemperature
            : (Sun != null ? Sun.colorTemperature : 6500f);

        if (Sun == null)
        {
            currentSunIntensity = targetSunIntensity;
            currentSunColorTemperature = targetSunColorTemperature;
            return;
        }

        if (targetSunIntensity <= DisabledIntensityThreshold)
        {
            SetSunActive(false);
            Sun.intensity = 0f;
            currentSunIntensity = 0f;
            currentSunColorTemperature = targetSunColorTemperature;
            return;
        }

        SetSunActive(true);

        var deltaTime = GetDeltaTime();
        var transitionDuration = Mathf.Max(0.0001f, TransitionDurationSeconds);
        var intensitySpeedPerSecond = Mathf.Max(0.0001f, MaxSunIntensity) / transitionDuration;
        Sun.intensity = Mathf.MoveTowards(Sun.intensity, targetSunIntensity, intensitySpeedPerSecond * deltaTime);
        if (DayNight == null)
        {
            Sun.useColorTemperature = true;
            var colorTemperatureSpeedPerSecond = Mathf.Max(0.0001f, MaxColorTemperatureDeltaPerSecond) / transitionDuration;
            Sun.colorTemperature = Mathf.MoveTowards(Sun.colorTemperature, targetSunColorTemperature, colorTemperatureSpeedPerSecond * deltaTime);
        }

        currentSunIntensity = Sun.intensity;
        currentSunColorTemperature = Sun.colorTemperature;
    }

    private void SetSunActive(bool active)
    {
        if (Sun == null)
        {
            return;
        }

        if (!_cachedShadowModeCaptured)
        {
            _cachedShadowMode = Sun.shadows;
            _cachedShadowModeCaptured = true;
        }

        if (active)
        {
            if (!Sun.gameObject.activeSelf)
            {
                Sun.gameObject.SetActive(true);
            }

            Sun.enabled = true;
            Sun.shadows = _cachedShadowMode;
            return;
        }

        Sun.shadows = LightShadows.None;
        Sun.enabled = false;
        if (Sun.gameObject.activeSelf)
        {
            Sun.gameObject.SetActive(false);
        }
    }

    private float ResolveOutdoorIntensity()
    {
        if (DayNight != null && DayNight.ControlTimeOfDay)
        {
            return DayNight.CurrentOutdoorSunIntensity;
        }

        if (OutdoorIntensityOverride >= 0f)
        {
            return OutdoorIntensityOverride;
        }

        return _capturedOutdoorIntensityValue;
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
