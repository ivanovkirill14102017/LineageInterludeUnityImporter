using UnityEngine;
#if UNITY_2021_2_OR_NEWER
using UnityEngine.Rendering;
#endif
#if UNITY_EDITOR
using UnityEditor;
#endif

#if UNITY_EDITOR
[InitializeOnLoad]
#endif
public static class GlobalFPSLimiter
{
    private const int TargetFPS = 30;

#if UNITY_EDITOR
    static GlobalFPSLimiter()
    {
        ApplyFPSLimit();
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
    }

    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        ApplyFPSLimit();
    }
#endif

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void RuntimeInit()
    {
        ApplyFPSLimit();
    }

    private static void ApplyFPSLimit()
    {
        if (QualitySettings.vSyncCount != 0)
        {
            QualitySettings.vSyncCount = 0;
        }

        if (Application.targetFrameRate != TargetFPS)
        {
            Application.targetFrameRate = TargetFPS;
        }

#if UNITY_2021_2_OR_NEWER
        var refreshRate = (float)Screen.currentResolution.refreshRateRatio.value;
        if (refreshRate > 0f)
        {
            var renderFrameInterval = Mathf.Max(1, Mathf.RoundToInt(refreshRate / TargetFPS));
            if (OnDemandRendering.renderFrameInterval != renderFrameInterval)
            {
                OnDemandRendering.renderFrameInterval = renderFrameInterval;
            }
        }
#endif
    }
}
