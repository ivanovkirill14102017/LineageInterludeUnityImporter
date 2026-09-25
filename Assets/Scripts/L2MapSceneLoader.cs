using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Serialization;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

[ExecuteAlways]
public sealed class L2MapSceneLoader : MonoBehaviour
{
    [Serializable]
    public sealed class MapScene
    {
        public string ScenePath;
        public int GridX;
        public int GridZ;
        public Vector3 Center;
        public Vector3 Size;
    }

    [FormerlySerializedAs("LoadChunksInEditMode")]
    public bool LoadMapsInEditMode = true;
    [FormerlySerializedAs("LoadChunksInPlayMode")]
    public bool LoadMapsInPlayMode = true;
    public int NeighborRadius = 1;
    public Transform Viewer;
    [FormerlySerializedAs("CommonScenes")]
    public MapScene[] Maps = Array.Empty<MapScene>();

    private readonly HashSet<string> _requestedLoads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private double _nextUpdateTime;

    private void Update()
    {
        if (Maps == null || Maps.Length == 0)
        {
            return;
        }

        if (Application.isPlaying)
        {
            if (!LoadMapsInPlayMode)
            {
                return;
            }
        }
        else if (!LoadMapsInEditMode)
        {
            return;
        }

        if (!ShouldUpdateNow())
        {
            return;
        }

        var position = ResolveViewerPosition();
        UpdateMapScenes(position);
    }

    private bool ShouldUpdateNow()
    {
#if UNITY_EDITOR
        var now = EditorApplication.timeSinceStartup;
#else
        var now = Time.realtimeSinceStartupAsDouble;
#endif
        if (now < _nextUpdateTime)
        {
            return false;
        }

        _nextUpdateTime = now + 0.35d;
        return true;
    }

    private Vector3 ResolveViewerPosition()
    {
        if (Viewer != null)
        {
            return Viewer.position;
        }

        var mainCamera = Camera.main;
        if (mainCamera != null)
        {
            return mainCamera.transform.position;
        }

#if UNITY_EDITOR
        var sceneView = SceneView.lastActiveSceneView;
        if (sceneView != null && sceneView.camera != null)
        {
            return sceneView.camera.transform.position;
        }
#endif

        return transform.position;
    }

    private void UpdateMapScenes(Vector3 viewerPosition)
    {
        var desiredScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var mapIsActive = IsMapInStreamingRange(viewerPosition);
        if (mapIsActive)
        {
            foreach (var scene in EnumerateScenes(Maps))
            {
                desiredScenes.Add(scene.ScenePath);
            }

        }

        foreach (var scene in EnumerateScenes(Maps))
        {
            ApplySceneLoadState(scene.ScenePath, desiredScenes);
        }

    }

    private bool IsMapInStreamingRange(Vector3 viewerPosition)
    {
        var parent = transform.parent;
        var loaders = parent != null
            ? parent.GetComponentsInChildren<L2MapSceneLoader>(true)
            : new[] { this };
        var nearest = loaders
            .Where(loader => loader != null && loader.TryGetMapScene(out _))
            .OrderBy(loader => loader.DistanceToMap(viewerPosition))
            .FirstOrDefault();
        if (nearest == null || !nearest.TryGetMapScene(out var nearestMap) || !TryGetMapScene(out var ownMap))
        {
            return nearest == this;
        }

        return Mathf.Abs(ResolveMapGridX(ownMap) - ResolveMapGridX(nearestMap)) <= NeighborRadius &&
               Mathf.Abs(ResolveMapGridZ(ownMap) - ResolveMapGridZ(nearestMap)) <= NeighborRadius;
    }

    private bool TryGetMapScene(out MapScene map)
    {
        map = EnumerateScenes(Maps).FirstOrDefault();
        return map != null;
    }

    private float DistanceToMap(Vector3 viewerPosition)
    {
        return TryGetMapScene(out var map)
            ? HorizontalDistanceSquared(map, viewerPosition)
            : float.PositiveInfinity;
    }

    private static float HorizontalDistanceSquared(MapScene scene, Vector3 position)
    {
        if (scene == null || scene.Size.x <= 0f || scene.Size.z <= 0f)
        {
            return float.PositiveInfinity;
        }

        var halfX = scene.Size.x * 0.5f;
        var halfZ = scene.Size.z * 0.5f;
        var dx = Mathf.Max(Mathf.Abs(position.x - scene.Center.x) - halfX, 0f);
        var dz = Mathf.Max(Mathf.Abs(position.z - scene.Center.z) - halfZ, 0f);
        return (dx * dx) + (dz * dz);
    }

    private static int ResolveMapGridX(MapScene map)
    {
        return TryParseMapGrid(map?.ScenePath, out var x, out _) ? x : map?.GridX ?? 0;
    }

    private static int ResolveMapGridZ(MapScene map)
    {
        return TryParseMapGrid(map?.ScenePath, out _, out var z) ? z : map?.GridZ ?? 0;
    }

    private static bool TryParseMapGrid(string scenePath, out int x, out int z)
    {
        x = 0;
        z = 0;
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            return false;
        }

        var parts = Path.GetFileNameWithoutExtension(scenePath).Split('_');
        return parts.Length == 2 &&
               int.TryParse(parts[0], out x) &&
               int.TryParse(parts[1], out z);
    }

    private static IEnumerable<MapScene> EnumerateScenes(MapScene[] scenes)
    {
        foreach (var scene in scenes ?? Array.Empty<MapScene>())
        {
            if (scene != null &&
                !string.IsNullOrWhiteSpace(scene.ScenePath) &&
                SceneExists(scene.ScenePath))
            {
                yield return scene;
            }
        }
    }

    private static bool SceneExists(string scenePath)
    {
        if (IsSceneLoaded(scenePath))
        {
            return true;
        }

#if UNITY_EDITOR
        return AssetDatabase.LoadAssetAtPath<SceneAsset>(scenePath) != null;
#else
        return Application.CanStreamedLevelBeLoaded(Path.GetFileNameWithoutExtension(scenePath));
#endif
    }

    private void ApplySceneLoadState(string scenePath, HashSet<string> desiredScenes)
    {
        if (desiredScenes.Contains(scenePath))
        {
            EnsureLoaded(scenePath);
        }
        else
        {
            EnsureUnloaded(scenePath);
        }
    }

    private void EnsureLoaded(string scenePath)
    {
        if (!SceneExists(scenePath) || IsSceneLoaded(scenePath) || !_requestedLoads.Add(scenePath))
        {
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
            _requestedLoads.Remove(scenePath);
            return;
        }
#endif

        var sceneName = Path.GetFileNameWithoutExtension(scenePath);
#if UNITY_EDITOR
        if (Application.isPlaying)
        {
            var parameters = new LoadSceneParameters(LoadSceneMode.Additive);
            var editorLoad = EditorSceneManager.LoadSceneAsyncInPlayMode(scenePath, parameters);
            if (editorLoad != null)
            {
                editorLoad.completed += _ => _requestedLoads.Remove(scenePath);
            }
            else
            {
                _requestedLoads.Remove(scenePath);
            }

            return;
        }
#endif

        SceneManager.LoadSceneAsync(sceneName, LoadSceneMode.Additive).completed += _ => _requestedLoads.Remove(scenePath);
    }

    private void EnsureUnloaded(string scenePath)
    {
        var scene = SceneManager.GetSceneByPath(scenePath);
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying)
        {
            EditorSceneManager.CloseScene(scene, true);
            return;
        }
#endif

        SceneManager.UnloadSceneAsync(scene);
    }

    private static bool IsSceneLoaded(string scenePath)
    {
        var scene = SceneManager.GetSceneByPath(scenePath);
        return scene.IsValid() && scene.isLoaded;
    }
}
