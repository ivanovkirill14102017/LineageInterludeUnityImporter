using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

internal static class ModernSkyboxImporter
{
    private const string SkyboxRoot = "Assets/L2Global/ModernSkybox";
    private const string PrefabPath = SkyboxRoot + "/L2ModernSkybox.prefab";
    private const string MaterialPath = SkyboxRoot + "/L2ModernSkyDome.mat";
    private const string MeshPath = SkyboxRoot + "/L2ModernSkyDome.asset";
    private const string InstanceName = "L2ModernSkybox";
    private const float DomeScale = 900f;

    [MenuItem("L2/Ensure Modern Skybox")]
    public static void EnsureModernSkyboxMenu()
    {
        EnsureModernSkybox(Debug.Log);
    }

    public static GameObject EnsureModernSkybox(Action<string> log = null)
    {
        L2AssetManager.EnsureFolderExists(SkyboxRoot);
        var prefab = EnsurePrefab(log);
        if (prefab == null)
        {
            log?.Invoke("[Skybox] Prefab is unavailable.");
            return null;
        }

        var camera = FindPreferredCamera();
        if (camera == null)
        {
            log?.Invoke("[Skybox] No scene camera found. Prefab exists, scene instance was not created.");
            return null;
        }

        var existing = camera.transform.Find(InstanceName);
        if (existing != null)
        {
            log?.Invoke($"[Skybox] Reusing existing '{InstanceName}' under camera '{camera.name}'.");
            return existing.gameObject;
        }

        var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
        if (instance == null)
        {
            log?.Invoke("[Skybox] Failed to instantiate modern skybox prefab.");
            return null;
        }

        instance.name = InstanceName;
        instance.transform.SetParent(camera.transform, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        WireSceneReferences(instance, camera);

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        log?.Invoke($"[Skybox] Created '{InstanceName}' under camera '{camera.name}'.");
        return instance;
    }

    private static GameObject EnsurePrefab(Action<string> log)
    {
        var existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existingPrefab != null)
        {
            log?.Invoke($"[Skybox] Reusing existing prefab: {PrefabPath}");
            return existingPrefab;
        }

        var material = EnsureMaterial();
        var mesh = EnsureMesh();
        var root = new GameObject(InstanceName);
        var controller = root.AddComponent<L2ModernSkyboxController>();

        var dome = new GameObject("SkyDome");
        dome.transform.SetParent(root.transform, false);
        dome.transform.localPosition = Vector3.zero;
        dome.transform.localRotation = Quaternion.identity;
        dome.transform.localScale = Vector3.one * DomeScale;

        var meshFilter = dome.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = mesh;

        var meshRenderer = dome.AddComponent<MeshRenderer>();
        meshRenderer.sharedMaterial = material;
        meshRenderer.shadowCastingMode = ShadowCastingMode.Off;
        meshRenderer.receiveShadows = false;
        meshRenderer.lightProbeUsage = LightProbeUsage.Off;
        meshRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
        meshRenderer.allowOcclusionWhenDynamic = false;

        var sun = new GameObject("Sun");
        sun.transform.SetParent(root.transform, false);

        var moon = new GameObject("Moon");
        moon.transform.SetParent(root.transform, false);

        controller.SkyDomeRenderer = meshRenderer;
        controller.SunNode = sun.transform;
        controller.MoonNode = moon.transform;
        controller.DisableUnitySkybox = true;
        controller.ForceCameraSolidColor = true;
        controller.KeepWorldRotation = true;

        var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        UnityEngine.Object.DestroyImmediate(root);
        AssetDatabase.SaveAssets();
        log?.Invoke($"[Skybox] Created managed prefab: {PrefabPath}");
        return prefab;
    }

    private static Material EnsureMaterial()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (existing != null)
        {
            return existing;
        }

        var shader = Shader.Find("L2/Modern Sky Dome") ?? Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        var material = new Material(shader)
        {
            name = "L2ModernSkyDome",
            renderQueue = 1000
        };

        if (material.HasProperty("_BaseColor"))
        {
            material.SetColor("_BaseColor", new Color(0.46f, 0.64f, 0.92f, 1f));
        }

        if (material.HasProperty("_Color"))
        {
            material.SetColor("_Color", new Color(0.46f, 0.64f, 0.92f, 1f));
        }

        AssetDatabase.CreateAsset(material, MaterialPath);
        return material;
    }

    private static Mesh EnsureMesh()
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(MeshPath);
        if (existing != null)
        {
            return existing;
        }

        var mesh = BuildInvertedSphereMesh(32, 16);
        mesh.name = "L2ModernSkyDome";
        AssetDatabase.CreateAsset(mesh, MeshPath);
        return mesh;
    }

    private static Mesh BuildInvertedSphereMesh(int horizontalSegments, int verticalSegments)
    {
        var vertices = new Vector3[(horizontalSegments + 1) * (verticalSegments + 1)];
        var uvs = new Vector2[vertices.Length];
        var triangles = new int[horizontalSegments * verticalSegments * 6];

        var vertexIndex = 0;
        for (var y = 0; y <= verticalSegments; y++)
        {
            var v = y / (float)verticalSegments;
            var theta = v * Mathf.PI;
            var sinTheta = Mathf.Sin(theta);
            var cosTheta = Mathf.Cos(theta);

            for (var x = 0; x <= horizontalSegments; x++)
            {
                var u = x / (float)horizontalSegments;
                var phi = u * Mathf.PI * 2f;
                vertices[vertexIndex] = new Vector3(
                    Mathf.Sin(phi) * sinTheta,
                    cosTheta,
                    Mathf.Cos(phi) * sinTheta);
                uvs[vertexIndex] = new Vector2(u, 1f - v);
                vertexIndex++;
            }
        }

        var triangleIndex = 0;
        for (var y = 0; y < verticalSegments; y++)
        {
            for (var x = 0; x < horizontalSegments; x++)
            {
                var a = y * (horizontalSegments + 1) + x;
                var b = a + horizontalSegments + 1;
                var c = b + 1;
                var d = a + 1;

                triangles[triangleIndex++] = a;
                triangles[triangleIndex++] = c;
                triangles[triangleIndex++] = b;
                triangles[triangleIndex++] = a;
                triangles[triangleIndex++] = d;
                triangles[triangleIndex++] = c;
            }
        }

        var mesh = new Mesh
        {
            vertices = vertices,
            uv = uvs,
            triangles = triangles
        };
        mesh.RecalculateBounds();
        mesh.RecalculateNormals();
        return mesh;
    }

    private static void WireSceneReferences(GameObject instance, Camera camera)
    {
        var controller = instance.GetComponent<L2ModernSkyboxController>();
        if (controller == null)
        {
            return;
        }

        var rig = camera.GetComponent<L2CameraAtmosphereRig>();
        controller.TargetCamera = camera;
        controller.Probe = rig != null ? rig.Probe : camera.GetComponentInChildren<L2CameraAtmosphereProbe>(true);
        controller.DayNight = rig != null ? rig.DayNight : camera.GetComponentInChildren<L2DayNightController>(true);
        controller.SkyDomeRenderer = controller.SkyDomeRenderer != null
            ? controller.SkyDomeRenderer
            : instance.GetComponentsInChildren<Renderer>(true).FirstOrDefault();
        controller.SunNode = controller.SunNode != null ? controller.SunNode : instance.transform.Find("Sun");
        controller.MoonNode = controller.MoonNode != null ? controller.MoonNode : instance.transform.Find("Moon");
        EditorUtility.SetDirty(controller);
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
