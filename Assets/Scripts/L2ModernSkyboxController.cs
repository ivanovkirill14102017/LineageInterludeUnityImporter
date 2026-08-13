using UnityEngine;
using UnityEngine.Rendering;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2ModernSkyboxController : MonoBehaviour
{
    [Header("References")]
    public Camera TargetCamera;
    public L2CameraAtmosphereProbe Probe;
    public L2DayNightController DayNight;
    public Renderer SkyDomeRenderer;
    public Transform SunNode;
    public Transform MoonNode;

    [Header("Unity Environment")]
    public bool DisableUnitySkybox = true;
    public bool ForceCameraSolidColor = true;
    public bool KeepWorldRotation = true;

    [Header("Sky Colors")]
    public Color DaySkyColor = new Color(0.46f, 0.64f, 0.92f, 1f);
    public Color TwilightSkyColor = new Color(0.86f, 0.55f, 0.38f, 1f);
    public Color NightSkyColor = new Color(0.025f, 0.035f, 0.07f, 1f);
    public Color IndoorSkyColor = new Color(0.04f, 0.045f, 0.05f, 1f);
    [Range(0f, 1f)] public float AmbientColorFactor = 0.35f;

    [Header("Debug")]
    [SerializeField] private string activeMapKey = string.Empty;
    [SerializeField] private string activeSkyBackgroundReference = string.Empty;
    [SerializeField] private string activeCloudReference = string.Empty;
    [SerializeField] private string activeSunReference = string.Empty;
    [SerializeField] private string activeMoonReference = string.Empty;
    [SerializeField] private Color currentSkyColor;

    private MaterialPropertyBlock _propertyBlock;
    private static readonly int BaseColorProperty = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorProperty = Shader.PropertyToID("_Color");

    private void OnEnable()
    {
        EnsureReferences();
        ApplySkyState();
    }

    private void OnValidate()
    {
        EnsureReferences();
        ApplySkyState();
    }

    private void LateUpdate()
    {
        EnsureReferences();
        ApplySkyState();
    }

    private void EnsureReferences()
    {
        if (TargetCamera == null)
        {
            TargetCamera = GetComponentInParent<Camera>();
        }

        if (Probe == null)
        {
            var rig = GetComponentInParent<L2CameraAtmosphereRig>();
            Probe = rig != null
                ? rig.Probe
                : GetComponentInChildren<L2CameraAtmosphereProbe>() ?? GetComponentInParent<L2CameraAtmosphereProbe>();
        }

        if (DayNight == null)
        {
            var rig = GetComponentInParent<L2CameraAtmosphereRig>();
            DayNight = rig != null
                ? rig.DayNight
                : GetComponentInChildren<L2DayNightController>() ?? GetComponentInParent<L2DayNightController>();
        }
    }

    private void ApplySkyState()
    {
        if (KeepWorldRotation && TargetCamera != null && transform.parent == TargetCamera.transform)
        {
            transform.localRotation = Quaternion.Inverse(TargetCamera.transform.rotation);
        }

        CaptureActiveSourceReferences();
        currentSkyColor = ResolveSkyColor();

        if (SkyDomeRenderer != null)
        {
            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            SkyDomeRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(BaseColorProperty, currentSkyColor);
            _propertyBlock.SetColor(ColorProperty, currentSkyColor);
            SkyDomeRenderer.SetPropertyBlock(_propertyBlock);
        }

        ApplyUnityEnvironment();
    }

    private void CaptureActiveSourceReferences()
    {
        activeMapKey = Probe != null ? Probe.CurrentMapKey : string.Empty;
        activeSkyBackgroundReference = string.Empty;
        activeCloudReference = string.Empty;
        activeSunReference = string.Empty;
        activeMoonReference = string.Empty;

        var context = Probe != null && Probe.CurrentContextVolume != null
            ? Probe.CurrentContextVolume.Context
            : null;
        if (context == null || context.SkySourceReferences == null)
        {
            return;
        }

        for (var i = 0; i < context.SkySourceReferences.Length; i++)
        {
            var reference = context.SkySourceReferences[i];
            var role = reference.Role ?? string.Empty;
            var value = reference.Reference ?? string.Empty;
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (string.IsNullOrEmpty(activeSkyBackgroundReference) &&
                (role == "SkySurfaceMaterial" || role == "SkyZoneTexture") &&
                LooksLikeSkyBackground(value))
            {
                activeSkyBackgroundReference = value;
            }

            if (string.IsNullOrEmpty(activeCloudReference) && LooksLikeCloud(value))
            {
                activeCloudReference = value;
            }

            if (string.IsNullOrEmpty(activeSunReference) && role == "SunSkin")
            {
                activeSunReference = value;
            }

            if (string.IsNullOrEmpty(activeMoonReference) && role == "MoonSkin")
            {
                activeMoonReference = value;
            }
        }
    }

    private Color ResolveSkyColor()
    {
        var indoorWeight = Probe != null ? Probe.IndoorWeight : 0f;
        var dayFactor = DayNight != null && DayNight.ControlTimeOfDay ? DayNight.DayFactor : 1f;
        var solarElevation = DayNight != null ? DayNight.SolarElevation : 45f;

        var outdoorColor = Color.Lerp(NightSkyColor, DaySkyColor, Mathf.Clamp01(dayFactor));
        if (solarElevation > -5f && solarElevation < 15f)
        {
            var twilightWeight = 1f - Mathf.Abs(Mathf.InverseLerp(-5f, 15f, solarElevation) - 0.5f) * 2f;
            outdoorColor = Color.Lerp(outdoorColor, TwilightSkyColor, Mathf.Clamp01(twilightWeight));
        }

        return Color.Lerp(outdoorColor, IndoorSkyColor, Mathf.Clamp01(indoorWeight));
    }

    private void ApplyUnityEnvironment()
    {
        if (DisableUnitySkybox)
        {
            RenderSettings.skybox = null;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = currentSkyColor * Mathf.Clamp01(AmbientColorFactor);
        }

        if (ForceCameraSolidColor && TargetCamera != null)
        {
            TargetCamera.clearFlags = CameraClearFlags.SolidColor;
            TargetCamera.backgroundColor = currentSkyColor;
        }
    }

    private static bool LooksLikeSkyBackground(string reference)
    {
        var lower = reference.ToLowerInvariant();
        return lower.Contains("skybackground") || lower.Contains("haze") || lower.Contains("starfield");
    }

    private static bool LooksLikeCloud(string reference)
    {
        return reference.ToLowerInvariant().Contains("cloud");
    }
}
