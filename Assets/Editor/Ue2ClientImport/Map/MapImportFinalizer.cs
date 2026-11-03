using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using System;
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
        var camera = FindMainCamera();

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

    private static Camera FindMainCamera()
    {
        var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include, FindObjectsSortMode.None)
            .Where(camera => camera != null && camera.gameObject.scene.IsValid() &&
                             camera.CompareTag("MainCamera"))
            .ToArray();
        if (cameras.Length != 1)
            throw new InvalidOperationException($"Expected one camera tagged MainCamera, found {cameras.Length}.");
        return cameras[0];
    }
}
