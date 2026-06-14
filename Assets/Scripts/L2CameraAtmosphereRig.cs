using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2CameraAtmosphereRig : MonoBehaviour
{
    [Header("Rig")]
    public bool AutoBuildRig = true;
    public bool FollowSceneViewCameraInEditMode = true;
    public string ProbeNodeName = "AtmosphereProbe";
    public string DayNightNodeName = "DayNight";
    public string SunNodeName = "SunControl";
    public string FogNodeName = "FogControl";
    public float EditorSyncIntervalSeconds = 0.1f;

    [Header("References")]
    [SerializeField] private L2CameraAtmosphereProbe probe;
    [SerializeField] private L2DayNightController dayNight;
    [SerializeField] private L2SunController sun;
    [SerializeField] private L2FogController fog;

    private double _nextEditorSyncTime;

    public L2CameraAtmosphereProbe Probe { get { return probe; } }
    public L2DayNightController DayNight { get { return dayNight; } }
    public L2SunController Sun { get { return sun; } }
    public L2FogController Fog { get { return fog; } }

    private void OnEnable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
        EditorApplication.update += EditorTick;
#endif
        EnsureRig();
        SyncToSceneViewCamera();
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= EditorTick;
#endif
    }

    private void OnValidate()
    {
        EnsureRig();
        SyncToSceneViewCamera();
    }

#if UNITY_EDITOR
    private void EditorTick()
    {
        if (Application.isPlaying || this == null || !isActiveAndEnabled)
        {
            return;
        }

        var now = EditorApplication.timeSinceStartup;
        if (now < _nextEditorSyncTime)
        {
            return;
        }

        _nextEditorSyncTime = now + Mathf.Max(0.01f, EditorSyncIntervalSeconds);
        EnsureRig();
        SyncToSceneViewCamera();
    }
#endif

    [ContextMenu("Rebuild Atmosphere Rig")]
    public void RebuildRig()
    {
        EnsureRig();
    }

    private void EnsureRig()
    {
        if (!AutoBuildRig)
        {
            return;
        }

        var probeNode = GetOrCreateChild(ProbeNodeName);
        var dayNightNode = GetOrCreateChild(DayNightNodeName);
        var sunNode = GetOrCreateChild(SunNodeName);
        var fogNode = GetOrCreateChild(FogNodeName);

        probe = GetOrAddComponent<L2CameraAtmosphereProbe>(probeNode);
        dayNight = GetOrAddComponent<L2DayNightController>(dayNightNode);
        sun = GetOrAddComponent<L2SunController>(sunNode);
        fog = GetOrAddComponent<L2FogController>(fogNode);

        probeNode.localPosition = Vector3.zero;
        probeNode.localRotation = Quaternion.identity;
        probeNode.localScale = Vector3.one;

        dayNight.Probe = probe;
        sun.Probe = probe;
        sun.DayNight = dayNight;
        fog.Probe = probe;

        dayNight.AutoFindDirectionalLights = true;
        sun.AutoFindReferences = false;
        fog.AutoFindReferences = true;
    }

    private void SyncToSceneViewCamera()
    {
#if UNITY_EDITOR
        if (Application.isPlaying || !FollowSceneViewCameraInEditMode)
        {
            return;
        }

        var sceneView = SceneView.lastActiveSceneView;
        if (sceneView == null || sceneView.camera == null)
        {
            return;
        }

        transform.position = sceneView.camera.transform.position;
        transform.rotation = sceneView.camera.transform.rotation;
#endif
    }

    private Transform GetOrCreateChild(string childName)
    {
        var child = transform.Find(childName);
        if (child != null)
        {
            return child;
        }

        var go = new GameObject(childName);
        go.transform.SetParent(transform, false);
        return go.transform;
    }

    private static T GetOrAddComponent<T>(Transform target) where T : Component
    {
        var component = target.GetComponent<T>();
        if (component == null)
        {
            component = target.gameObject.AddComponent<T>();
        }

        return component;
    }
}
