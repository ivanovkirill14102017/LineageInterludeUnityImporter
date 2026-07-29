using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2LocalLightRuntimeOptimizer : MonoBehaviour
{
    [Header("Budgets")]
    public int MaxActiveLights = 24;
    public int MaxShadowLights = 2;

    [Header("Distances")]
    public float MaxLightDistance = 28f;
    public float MaxShadowDistance = 12f;

    [Header("Refresh")]
    public float EvaluationIntervalSeconds = 0.25f;
    public float DiscoveryIntervalSeconds = 4f;
    public bool AffectBakedLights = false;

    [Header("Editor Preview")]
    public bool PreviewInEditMode = true;
    public float EditorEvaluationIntervalSeconds = 0.5f;
    public float EditorDiscoveryIntervalSeconds = 4f;

    private readonly List<ManagedLight> _managedLights = new List<ManagedLight>(128);
    private readonly List<LightCandidate> _candidates = new List<LightCandidate>(128);
    private float _nextEvaluationTime;
    private float _nextDiscoveryTime;
    private double _nextEditorEvaluationTime;
    private double _nextEditorDiscoveryTime;

    private void OnEnable()
    {
        if (Application.isPlaying)
        {
            _nextEvaluationTime = 0f;
            _nextDiscoveryTime = 0f;
            DiscoverLights();
            EvaluateLights(force: true);
        }
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
        EditorApplication.update += EditorTick;
        _nextEditorEvaluationTime = 0d;
        _nextEditorDiscoveryTime = 0d;
        if (!Application.isPlaying && PreviewInEditMode)
        {
            DiscoverLights();
            EvaluateLights(force: true);
        }
#endif
    }

    private void Update()
    {
        if (!Application.isPlaying)
        {
            return;
        }

        var now = Time.unscaledTime;
        if (now >= _nextDiscoveryTime)
        {
            DiscoverLights();
            _nextDiscoveryTime = now + Mathf.Max(0.25f, DiscoveryIntervalSeconds);
        }

        if (now >= _nextEvaluationTime)
        {
            EvaluateLights(force: false);
            _nextEvaluationTime = now + Mathf.Max(0.05f, EvaluationIntervalSeconds);
        }
    }

    private void OnDisable()
    {
        if (Application.isPlaying)
        {
            RestoreLights();
        }
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
        if (!Application.isPlaying)
        {
            RestoreLights();
        }
#endif
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            return;
        }

        if (PreviewInEditMode)
        {
            DiscoverLights();
            EvaluateLights(force: true);
            return;
        }

        RestoreLights();
    }

    [ContextMenu("Refresh Managed Lights")]
    public void RefreshManagedLights()
    {
        DiscoverLights();
        EvaluateLights(force: true);
    }

#if UNITY_EDITOR
    private void EditorTick()
    {
        if (Application.isPlaying || this == null || !isActiveAndEnabled)
        {
            return;
        }

        if (!PreviewInEditMode)
        {
            return;
        }

        var now = EditorApplication.timeSinceStartup;
        if (now >= _nextEditorDiscoveryTime)
        {
            DiscoverLights();
            _nextEditorDiscoveryTime = now + System.Math.Max(0.25d, EditorDiscoveryIntervalSeconds);
        }

        if (now >= _nextEditorEvaluationTime)
        {
            EvaluateLights(force: false);
            _nextEditorEvaluationTime = now + System.Math.Max(0.1d, EditorEvaluationIntervalSeconds);
        }
    }
#endif

    private void DiscoverLights()
    {
        var previousLights = new Dictionary<int, ManagedLight>(_managedLights.Count);
        for (var i = 0; i < _managedLights.Count; i++)
        {
            var managed = _managedLights[i];
            if (managed.Light == null)
            {
                continue;
            }

            previousLights[managed.Light.GetInstanceID()] = managed;
        }

        _managedLights.Clear();

        var lights = FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (var i = 0; i < lights.Length; i++)
        {
            var light = lights[i];
            if (!ShouldManage(light))
            {
                continue;
            }

            if (previousLights.TryGetValue(light.GetInstanceID(), out var managed))
            {
                _managedLights.Add(managed);
                continue;
            }

            _managedLights.Add(new ManagedLight(light, light.enabled, light.shadows));
        }
    }

    private void EvaluateLights(bool force)
    {
        if (_managedLights.Count == 0)
        {
            return;
        }

        _candidates.Clear();
        var origin = transform.position;
        var maxLightDistanceSqr = Mathf.Max(0.01f, MaxLightDistance) * Mathf.Max(0.01f, MaxLightDistance);

        for (var i = 0; i < _managedLights.Count; i++)
        {
            var light = _managedLights[i].Light;
            if (light == null)
            {
                continue;
            }

            var distanceSqr = (light.transform.position - origin).sqrMagnitude;
            if (distanceSqr > maxLightDistanceSqr)
            {
                continue;
            }

            _candidates.Add(new LightCandidate(i, distanceSqr));
        }

        _candidates.Sort(static (left, right) => left.DistanceSqr.CompareTo(right.DistanceSqr));

        var activeBudget = Mathf.Max(0, MaxActiveLights);
        var shadowBudget = Mathf.Max(0, MaxShadowLights);
        var shadowDistanceSqr = Mathf.Max(0.01f, MaxShadowDistance) * Mathf.Max(0.01f, MaxShadowDistance);

        for (var i = 0; i < _managedLights.Count; i++)
        {
            var managed = _managedLights[i];
            if (managed.Light == null)
            {
                continue;
            }

            ApplyLightState(managed.Light, enabled: false, shadows: LightShadows.None, force);
        }

        for (var i = 0; i < _candidates.Count && i < activeBudget; i++)
        {
            var candidate = _candidates[i];
            var managed = _managedLights[candidate.Index];
            if (managed.Light == null)
            {
                continue;
            }

            var allowShadows = i < shadowBudget &&
                               candidate.DistanceSqr <= shadowDistanceSqr &&
                               managed.OriginalShadows != LightShadows.None;
            var targetShadows = allowShadows ? managed.OriginalShadows : LightShadows.None;
            ApplyLightState(managed.Light, enabled: managed.OriginalEnabled, shadows: targetShadows, force);
        }
    }

    private void RestoreLights()
    {
        for (var i = 0; i < _managedLights.Count; i++)
        {
            var managed = _managedLights[i];
            if (managed.Light == null)
            {
                continue;
            }

            managed.Light.enabled = managed.OriginalEnabled;
            managed.Light.shadows = managed.OriginalShadows;
        }
    }

    private bool ShouldManage(Light light)
    {
        if (light == null || !light.gameObject.scene.IsValid())
        {
            return false;
        }

        if (light.type != LightType.Point && light.type != LightType.Spot)
        {
            return false;
        }

        if (!AffectBakedLights && light.lightmapBakeType == LightmapBakeType.Baked)
        {
            return false;
        }

        return true;
    }

    private static void ApplyLightState(Light light, bool enabled, LightShadows shadows, bool force)
    {
        if (light == null)
        {
            return;
        }

        if (force || light.enabled != enabled)
        {
            light.enabled = enabled;
        }

        if (force || light.shadows != shadows)
        {
            light.shadows = shadows;
        }
    }

    private readonly struct ManagedLight
    {
        public ManagedLight(Light light, bool originalEnabled, LightShadows originalShadows)
        {
            Light = light;
            OriginalEnabled = originalEnabled;
            OriginalShadows = originalShadows;
        }

        public Light Light { get; }
        public bool OriginalEnabled { get; }
        public LightShadows OriginalShadows { get; }
    }

    private readonly struct LightCandidate
    {
        public LightCandidate(int index, float distanceSqr)
        {
            Index = index;
            DistanceSqr = distanceSqr;
        }

        public int Index { get; }
        public float DistanceSqr { get; }
    }
}
