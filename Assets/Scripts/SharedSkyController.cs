using System;
using UnityEngine;
using UnityEngine.SceneManagement;

[DisallowMultipleComponent]
public sealed class SharedSkyController : MonoBehaviour
{
    [Serializable]
    public struct Layer
    {
        public SkyLayerRole Role;
        public Renderer Renderer;
    }

    [Serializable]
    public struct CelestialBody
    {
        public Transform Transform;
        public Renderer Renderer;
    }

    public SkyTimeProfileData TimeProfiles = new SkyTimeProfileData();
    public Transform GeometryRoot;
    public float MaxLayerRadius;
    public Layer[] Layers = Array.Empty<Layer>();
    public CelestialBody Sun;
    public CelestialBody Moon;
    public float CloudPanRate;
    [Range(0f, 1f)] public float CloudMinimumOpacity = 0.55f;
    [Range(0f, 1f)] public float CloudMaximumOpacity = 0.85f;
    [Range(0f, 1f)] public float CloudMinimumBrightness = 0.7f;
    public float CloudVariationPeriodSeconds = 75f;
    public int EnvironmentType;
    [Range(0f, 24f)] public float TimeOfDayHours = 12f;
    public bool ShowTimeSlider = true;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int BaseMapStId = Shader.PropertyToID("_BaseMap_ST");
    private Camera _viewer;
    private CameraClearFlags _previousClearFlags;
    private Color _previousBackgroundColor;
    private bool _viewerConfigured;
    private MaterialPropertyBlock _properties;
    private bool _validated;

    public Camera ViewerCamera => _viewer;
    public Vector3 SunDirection { get; private set; }
    public Vector3 MoonDirection => -SunDirection;
    public bool IsStandalonePreview => SceneManager.GetActiveScene().handle == gameObject.scene.handle;

    private void ValidateImportedScene()
    {
        if (TimeProfiles == null)
            throw new InvalidOperationException("SharedSky time profiles are missing. Import the sky scene.");
        TimeProfiles.Validate();
        if (GeometryRoot == null || MaxLayerRadius <= 0f || Layers == null || Layers.Length == 0)
            throw new InvalidOperationException("SharedSky scene geometry is missing. Import the sky scene.");
        if (Sun.Transform == null || Sun.Renderer == null || Moon.Transform == null || Moon.Renderer == null ||
            CloudVariationPeriodSeconds <= 0f || CloudMinimumOpacity > CloudMaximumOpacity)
            throw new InvalidOperationException("SharedSky celestial bodies or cloud settings are invalid. Import the sky scene.");
        _validated = true;
    }

    private void OnDestroy()
    {
        RestoreViewer();
    }

    private void LateUpdate()
    {
        if (!Application.isPlaying)
            return;

        if (!_validated)
            ValidateImportedScene();
        BindViewer();
        if (GeometryRoot == null)
            throw new InvalidOperationException("SharedSky geometry root is missing.");
        if (MaxLayerRadius <= 0f)
            throw new InvalidOperationException("SharedSky geometry has no valid radius.");
        GeometryRoot.position = _viewer.transform.position;
        GeometryRoot.localScale = Vector3.one * (0.1f * _viewer.farClipPlane / MaxLayerRadius);

        var profile = TimeProfiles.GetProfile(EnvironmentType);
        var orbit = (Mathf.Repeat(TimeOfDayHours, 24f) - 6f) * (Mathf.PI / 12f);
        const float orbitInclination = 55f * Mathf.Deg2Rad;
        SunDirection = new Vector3(Mathf.Cos(orbit),
            Mathf.Sin(orbit) * Mathf.Sin(orbitInclination),
            -Mathf.Sin(orbit) * Mathf.Cos(orbitInclination));
        _viewer.backgroundColor = profile.Evaluate(SkyLayerRole.SkyBox, TimeOfDayHours);
        _properties ??= new MaterialPropertyBlock();
        var cloudWave = 0.5f + 0.25f * Mathf.Sin(Time.time * 2f * Mathf.PI / CloudVariationPeriodSeconds)
            + 0.25f * Mathf.Sin(Time.time * 2f * Mathf.PI / (CloudVariationPeriodSeconds * 1.73f));
        foreach (var layer in Layers)
        {
            if (layer.Renderer == null)
                throw new InvalidOperationException("SharedSky layer renderer was lost.");

            layer.Renderer.GetPropertyBlock(_properties);
            var color = profile.Evaluate(layer.Role, TimeOfDayHours);
            if (layer.Role == SkyLayerRole.Cloud1)
            {
                color *= Mathf.Lerp(CloudMinimumBrightness, 1f, cloudWave);
                color.a = Mathf.Lerp(CloudMinimumOpacity, CloudMaximumOpacity, cloudWave);
                _properties.SetVector(BaseMapStId,
                    new Vector4(1f, 1f, Mathf.Repeat(Time.time * CloudPanRate, 1f), 0f));
            }
            _properties.SetColor(BaseColorId, color);
            layer.Renderer.SetPropertyBlock(_properties);
        }
        PositionBody(Sun, SunDirection, 97f, new Color(1f, 0.9f, 0.72f, 1f));
        PositionBody(Moon, MoonDirection, 97.2f, new Color(0.75f, 0.84f, 1f, 1f));
    }

    private void PositionBody(CelestialBody body, Vector3 direction, float radius, Color tint)
    {
        body.Transform.localPosition = direction * radius;
        body.Transform.localRotation = Quaternion.LookRotation(-direction, Vector3.up);
        var visibility = Mathf.Clamp01((direction.y + 0.05f) / 0.15f);
        body.Renderer.enabled = visibility > 0f;
        body.Renderer.GetPropertyBlock(_properties);
        tint.r *= visibility;
        tint.g *= visibility;
        tint.b *= visibility;
        tint.a = visibility;
        _properties.SetColor(BaseColorId, tint);
        body.Renderer.SetPropertyBlock(_properties);
    }

    private void BindViewer()
    {
        Camera camera = null;
        foreach (var candidate in Camera.allCameras)
        {
            if (!candidate.CompareTag("MainCamera"))
                continue;
            if (camera != null)
                throw new InvalidOperationException("SharedSky found multiple active MainCamera cameras.");
            camera = candidate;
        }
        if (camera == null)
            throw new InvalidOperationException("SharedSky requires one active camera tagged MainCamera.");
        if (camera == _viewer && _viewerConfigured)
            return;

        RestoreViewer();
        _viewer = camera;

        _previousClearFlags = _viewer.clearFlags;
        _previousBackgroundColor = _viewer.backgroundColor;
        _viewer.clearFlags = CameraClearFlags.SolidColor;
        _viewerConfigured = true;
    }

    private void RestoreViewer()
    {
        if (_viewer != null && _viewerConfigured)
        {
            _viewer.clearFlags = _previousClearFlags;
            _viewer.backgroundColor = _previousBackgroundColor;
        }

        _viewerConfigured = false;
    }

    private void OnGUI()
    {
        if (!Application.isPlaying || !ShowTimeSlider)
            return;

        var rect = new Rect(Screen.width - 330f, 16f, 314f, 72f);
        GUILayout.BeginArea(rect, GUI.skin.window);
        var hour = Mathf.FloorToInt(TimeOfDayHours) % 24;
        var minute = Mathf.FloorToInt((TimeOfDayHours - Mathf.Floor(TimeOfDayHours)) * 60f);
        GUILayout.Label($"Sky time  {hour:00}:{minute:00}");
        TimeOfDayHours = GUILayout.HorizontalSlider(TimeOfDayHours, 0f, 24f);
        GUILayout.EndArea();
    }
}
