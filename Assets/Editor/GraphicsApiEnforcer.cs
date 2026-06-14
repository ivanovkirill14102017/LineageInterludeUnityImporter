using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

[InitializeOnLoad]
internal static class GraphicsApiEnforcer
{
    static GraphicsApiEnforcer()
    {
        EditorApplication.delayCall += EnsureStandaloneD3D11;
    }

    private static void EnsureStandaloneD3D11()
    {
        ConfigureStandaloneTarget(BuildTarget.StandaloneWindows64);
        ConfigureStandaloneTarget(BuildTarget.StandaloneWindows);
    }

    private static void ConfigureStandaloneTarget(BuildTarget target)
    {
        try
        {
            var currentApis = PlayerSettings.GetGraphicsAPIs(target);
            var alreadyConfigured = !PlayerSettings.GetUseDefaultGraphicsAPIs(target) &&
                                    currentApis != null &&
                                    currentApis.Length == 1 &&
                                    currentApis[0] == GraphicsDeviceType.Direct3D11;
            if (alreadyConfigured)
            {
                return;
            }

            PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
            PlayerSettings.SetGraphicsAPIs(target, new[] { GraphicsDeviceType.Direct3D11 });
            Debug.Log($"[GraphicsApiEnforcer] Forced {target} to Direct3D11.");
        }
        catch (Exception exception)
        {
            Debug.LogWarning($"[GraphicsApiEnforcer] Failed to force Direct3D11 for {target}: {exception.Message}");
        }
    }
}
