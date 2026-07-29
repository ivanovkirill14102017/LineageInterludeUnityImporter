using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2CameraAtmosphereRig : MonoBehaviour
{
#if UNITY_EDITOR
    private const double SceneViewFollowIntervalSeconds = 0.2d;
#endif

    [Header("Rig")]
    public bool AutoBuildRig = true;
    public bool FollowSceneViewCameraInEditMode = true;
    public string ProbeNodeName = "AtmosphereProbe";
    public string DayNightNodeName = "DayNight";
    public string SunNodeName = "SunControl";
    public string FogNodeName = "FogControl";

    [Header("References")]
    [SerializeField] private L2CameraAtmosphereProbe probe;
    [SerializeField] private L2DayNightController dayNight;
    [SerializeField] private L2SunController sun;
    [SerializeField] private L2FogController fog;
    [SerializeField] private L2LocalLightRuntimeOptimizer localLightOptimizer;

#if UNITY_EDITOR
    private double nextSceneViewFollowTime;
#endif

    public L2CameraAtmosphereProbe Probe { get { return probe; } }
    public L2DayNightController DayNight { get { return dayNight; } }
    public L2SunController Sun { get { return sun; } }
    public L2FogController Fog { get { return fog; } }
    public L2LocalLightRuntimeOptimizer LocalLightOptimizer { get { return localLightOptimizer; } }

    private void OnEnable()
    {
        EnsureRig();
#if UNITY_EDITOR
        EditorApplication.update -= OnEditorUpdate;
        EditorApplication.update += OnEditorUpdate;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        EditorApplication.update -= OnEditorUpdate;
#endif
    }

    private void OnValidate()
    {
        EnsureRig();
    }

    [ContextMenu("Rebuild Atmosphere Rig")]
    public void RebuildRig()
    {
        EnsureRig();
    }

    [ContextMenu("Sync To Scene View Camera")]
    public void SyncRigToSceneViewCamera()
    {
        SyncToSceneViewCamera();
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
        localLightOptimizer = GetOrAddComponent<L2LocalLightRuntimeOptimizer>(transform);

        probeNode.localPosition = Vector3.zero;
        probeNode.localRotation = Quaternion.identity;
        probeNode.localScale = Vector3.one;

        dayNight.Probe = probe;
        sun.Probe = probe;
        sun.DayNight = dayNight;
        fog.BindToProbe(probe);

        dayNight.enabled = false;
        dayNight.AutoFindDirectionalLights = true;
        sun.AutoFindReferences = false;
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

        var sceneViewTransform = sceneView.camera.transform;
        if ((transform.position - sceneViewTransform.position).sqrMagnitude > 0.000001f ||
            Quaternion.Angle(transform.rotation, sceneViewTransform.rotation) > 0.01f)
        {
            transform.position = sceneViewTransform.position;
            transform.rotation = sceneViewTransform.rotation;
        }
#endif
    }

#if UNITY_EDITOR
    private void OnEditorUpdate()
    {
        SyncToSceneViewCameraThrottled();
    }

    private void SyncToSceneViewCameraThrottled()
    {
        if (Application.isPlaying || !FollowSceneViewCameraInEditMode)
        {
            return;
        }

        var now = EditorApplication.timeSinceStartup;
        if (now < nextSceneViewFollowTime)
        {
            return;
        }

        nextSceneViewFollowTime = now + SceneViewFollowIntervalSeconds;
        SyncToSceneViewCamera();
    }
#endif

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
