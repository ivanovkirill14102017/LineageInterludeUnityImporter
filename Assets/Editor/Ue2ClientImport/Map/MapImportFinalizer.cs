using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
using System.Diagnostics;
using System.Linq;

internal static class MapImportFinalizer
{
    public static void Complete(GameObject mapRoot, Action<string> log = null)
    {
        AssetDatabase.SaveAssets();

        AssetDatabase.Refresh();

        EnsureCameraAtmosphereRig(log);

        Selection.activeObject = mapRoot;
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
    }

    private static void EnsureCameraAtmosphereRig(Action<string> log)
    {
        var camera = FindPreferredCamera();
        if (camera == null)
        {
            log?.Invoke("[Finalizer] No scene camera found. Skipping automatic atmosphere rig setup.");
            return;
        }

        var rig = camera.GetComponent<L2CameraAtmosphereRig>();
        if (rig == null)
        {
            rig = camera.gameObject.AddComponent<L2CameraAtmosphereRig>();
            log?.Invoke($"[Finalizer] Added L2CameraAtmosphereRig to camera '{camera.name}'.");
        }
        else
        {
            log?.Invoke($"[Finalizer] Reusing existing L2CameraAtmosphereRig on camera '{camera.name}'.");
        }

        rig.AutoBuildRig = true;
        rig.RebuildRig();
        EditorUtility.SetDirty(camera.gameObject);
    }

    private static Camera FindPreferredCamera()
    {
        var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(camera => camera != null && camera.gameObject.scene.IsValid())
            .ToArray();
        if (cameras.Length == 0)
        {
            return null;
        }

        var taggedMainCamera = cameras.FirstOrDefault(camera => camera.CompareTag("MainCamera"));
        return taggedMainCamera != null ? taggedMainCamera : cameras[0];
    }
}
