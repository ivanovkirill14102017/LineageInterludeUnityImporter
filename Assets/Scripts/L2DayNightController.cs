using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2DayNightController : MonoBehaviour
{
    private const float DefaultEditorDeltaTime = 0.05f;
    private const float DefaultOutdoorSunIntensity = 4f;

    [Header("References")]
    public L2CameraAtmosphereProbe Probe;
    public Light Sun;
    public Light Moon;
    public bool AutoFindDirectionalLights = true;

    [Header("Time Of Day")]
    public bool ControlTimeOfDay = true;
    public bool AnimateTimeOfDay = false;
    public bool AnimateTimeOfDayInEditMode = false;
    [Range(0f, 24f)]
    public float TimeOfDayHours = 12f;
    public float DayLengthMinutes = 20f;
    public float SunriseHour = 6f;
    public float SunsetHour = 18f;
    public float TwilightSoftnessHours = 1.5f;

    [Header("Sun Profile")]
    public float OutdoorSunIntensity = DefaultOutdoorSunIntensity;
    public float MaxSunElevation = 55f;
    public float NoonAzimuth = 0f;
    public bool UseMapSunRotation = true;
    public bool CaptureCurrentSunYawAsNoonAzimuth = true;
    public float SunriseSunIntensity = 0.35f;
    public float NightSunIntensity = 0f;
    public float NoonColorTemperature = 6500f;
    public float SunriseSunColorTemperature = 3800f;
    public float NightSunColorTemperature = 2200f;

    [Header("Moon Profile")]
    public bool ControlMoon = true;
    public float MoonIntensity = 0.08f;
    public float MaxMoonElevation = 35f;
    public float MidnightAzimuth = 180f;
    public bool UseMapMoonRotation = true;
    public float MoonColorTemperature = 9000f;

    [Header("Debug")]
    [SerializeField] private float currentTimeOfDayHours;
    [SerializeField] private float dayFactor;
    [SerializeField] private float solarElevation;
    [SerializeField] private float lunarElevation;
    [SerializeField] private float currentOutdoorSunIntensity;
    [SerializeField] private float currentOutdoorSunColorTemperature;
    [SerializeField] private float currentMoonIntensity;

    private bool _capturedSunOutdoorIntensity;
    private bool _capturedSunYaw;
    private bool _capturedMoonYaw;
    private float _capturedSunNoonAzimuth;
    private float _capturedMoonMidnightAzimuth;

    public float CurrentTimeOfDayHours { get { return currentTimeOfDayHours; } }
    public float DayFactor { get { return dayFactor; } }
    public float SolarElevation { get { return solarElevation; } }
    public float LunarElevation { get { return lunarElevation; } }
    public float CurrentOutdoorSunIntensity { get { return currentOutdoorSunIntensity; } }
    public float CurrentOutdoorSunColorTemperature { get { return currentOutdoorSunColorTemperature; } }
    public float CurrentMoonIntensity { get { return currentMoonIntensity; } }

    private void OnEnable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
        EditorApplication.update += EditorTick;
#endif
        EnsureReferences();
        UpdateCycle();
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
#endif
    }

    private void OnValidate()
    {
        EnsureReferences();
        UpdateCycle();
    }

    private void Update()
    {
        EnsureReferences();
        AdvanceTimeOfDay(GetDeltaTime(true));
        UpdateCycle();
    }

#if UNITY_EDITOR
    private void EditorTick()
    {
        if (Application.isPlaying || this == null || !isActiveAndEnabled)
        {
            return;
        }

        EnsureReferences();
        AdvanceTimeOfDay(GetDeltaTime(false));
        UpdateCycle();
    }
#endif

    private float GetDeltaTime(bool playMode)
    {
        return playMode ? Time.deltaTime : DefaultEditorDeltaTime;
    }

    private void EnsureReferences()
    {
        if (Probe == null)
        {
            Probe = GetComponent<L2CameraAtmosphereProbe>() ?? GetComponentInParent<L2CameraAtmosphereProbe>();
        }

        if (AutoFindDirectionalLights)
        {
            if (Sun == null)
            {
                Sun = RenderSettings.sun;
                if (Sun == null)
                {
                    Sun = FindDirectionalLight(false);
                }
            }

            if (Moon == null)
            {
                Moon = FindDirectionalLight(true);
            }
        }

        if (!_capturedSunOutdoorIntensity && Sun != null)
        {
            if (OutdoorSunIntensity < 0f)
            {
                OutdoorSunIntensity = DefaultOutdoorSunIntensity;
            }

            _capturedSunOutdoorIntensity = true;
        }

        if (OutdoorSunIntensity < 0f)
        {
            OutdoorSunIntensity = DefaultOutdoorSunIntensity;
        }

        if (!_capturedSunYaw && Sun != null)
        {
            _capturedSunNoonAzimuth = Sun.transform.eulerAngles.y;
            _capturedSunYaw = true;
        }

        if (!_capturedMoonYaw && Moon != null)
        {
            _capturedMoonMidnightAzimuth = Moon.transform.eulerAngles.y;
            _capturedMoonYaw = true;
        }
    }

    private Light FindDirectionalLight(bool preferMoon)
    {
        var lights = Resources.FindObjectsOfTypeAll<Light>();
        for (var i = 0; i < lights.Length; i++)
        {
            var light = lights[i];
            if (light == null || !light.gameObject.scene.IsValid() || light.type != LightType.Directional)
            {
                continue;
            }

            var lowerName = light.name.ToLowerInvariant();
            if (preferMoon)
            {
                if (lowerName.Contains("moon"))
                {
                    return light;
                }
            }
            else
            {
                if (!lowerName.Contains("moon"))
                {
                    return light;
                }
            }
        }

        return null;
    }

    private void AdvanceTimeOfDay(float deltaTime)
    {
        if (!ControlTimeOfDay || !AnimateTimeOfDay)
        {
            currentTimeOfDayHours = WrapHour(TimeOfDayHours);
            return;
        }

        if (!Application.isPlaying && !AnimateTimeOfDayInEditMode)
        {
            currentTimeOfDayHours = WrapHour(TimeOfDayHours);
            return;
        }

        var dayLengthSeconds = Mathf.Max(1f, DayLengthMinutes * 60f);
        var deltaHours = 24f * deltaTime / dayLengthSeconds;
        TimeOfDayHours = WrapHour(TimeOfDayHours + deltaHours);
        currentTimeOfDayHours = TimeOfDayHours;
    }

    private void UpdateCycle()
    {
        currentTimeOfDayHours = WrapHour(TimeOfDayHours);
        if (!ControlTimeOfDay)
        {
            currentOutdoorSunIntensity = OutdoorSunIntensity >= 0f ? OutdoorSunIntensity : DefaultOutdoorSunIntensity;
            currentOutdoorSunColorTemperature = NoonColorTemperature;
            dayFactor = 1f;
            solarElevation = 45f;
            lunarElevation = -25f;
            return;
        }

        ComputeSunProfile();
        ApplySun();
        ApplyMoon();
    }

    private void ComputeSunProfile()
    {
        var dayIntensity = OutdoorSunIntensity >= 0f ? OutdoorSunIntensity : DefaultOutdoorSunIntensity;
        var cycleRadians = ((currentTimeOfDayHours - 6f) / 24f) * Mathf.PI * 2f;
        solarElevation = Mathf.Sin(cycleRadians) * Mathf.Max(0f, MaxSunElevation);
        lunarElevation = Mathf.Sin(cycleRadians + Mathf.PI) * Mathf.Max(0f, MaxMoonElevation);

        var twilightHours = Mathf.Max(0.01f, TwilightSoftnessHours);
        var sunriseStart = SunriseHour - twilightHours;
        var sunriseEnd = SunriseHour + twilightHours;
        var sunsetStart = SunsetHour - twilightHours;
        var sunsetEnd = SunsetHour + twilightHours;

        var sunriseBlend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(sunriseStart, sunriseEnd, currentTimeOfDayHours));
        var sunsetBlend = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(sunsetStart, sunsetEnd, currentTimeOfDayHours));
        dayFactor = Mathf.Clamp01(Mathf.Min(sunriseBlend, sunsetBlend));

        var horizonFactor = Mathf.Clamp01(Mathf.InverseLerp(0f, Mathf.Max(0.0001f, MaxSunElevation), Mathf.Max(0f, solarElevation)));
        currentOutdoorSunIntensity = Mathf.Lerp(NightSunIntensity, SunriseSunIntensity, dayFactor);
        currentOutdoorSunIntensity = Mathf.Lerp(currentOutdoorSunIntensity, dayIntensity, horizonFactor);

        var warmTemperature = Mathf.Lerp(NightSunColorTemperature, SunriseSunColorTemperature, dayFactor);
        currentOutdoorSunColorTemperature = Mathf.Lerp(warmTemperature, NoonColorTemperature, horizonFactor);
        currentMoonIntensity = ControlMoon ? Mathf.Lerp(MoonIntensity, 0f, dayFactor) : 0f;
    }

    private void ApplySun()
    {
        if (Sun == null)
        {
            return;
        }

        var azimuthBase = ResolveNoonAzimuth();
        var azimuth = azimuthBase + ((currentTimeOfDayHours - 12f) / 24f) * 360f;
        Sun.transform.rotation = Quaternion.Euler(solarElevation, azimuth, 0f);
        Sun.useColorTemperature = true;
        Sun.colorTemperature = currentOutdoorSunColorTemperature;
    }

    private void ApplyMoon()
    {
        if (!ControlMoon || Moon == null)
        {
            return;
        }

        var azimuthBase = ResolveMidnightAzimuth();
        var azimuth = azimuthBase + ((currentTimeOfDayHours - 0f) / 24f) * 360f;
        Moon.transform.rotation = Quaternion.Euler(lunarElevation, azimuth, 0f);
        Moon.useColorTemperature = true;
        Moon.colorTemperature = MoonColorTemperature;
        Moon.intensity = currentMoonIntensity;
    }

    private float ResolveNoonAzimuth()
    {
        if (UseMapSunRotation && Probe != null && Probe.HasMapSunRotation)
        {
            return Probe.MapSunEulerDegrees.y;
        }

        if (CaptureCurrentSunYawAsNoonAzimuth && _capturedSunYaw)
        {
            return _capturedSunNoonAzimuth;
        }

        return NoonAzimuth;
    }

    private float ResolveMidnightAzimuth()
    {
        if (UseMapMoonRotation && Probe != null && Probe.HasMapMoonRotation)
        {
            return Probe.MapMoonEulerDegrees.y;
        }

        if (_capturedMoonYaw)
        {
            return _capturedMoonMidnightAzimuth;
        }

        return MidnightAzimuth;
    }

    private static float WrapHour(float hour)
    {
        while (hour < 0f)
        {
            hour += 24f;
        }

        while (hour >= 24f)
        {
            hour -= 24f;
        }

        return hour;
    }
}

