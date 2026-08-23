using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
#endif

[ExecuteAlways]
public sealed class L2ChunkedMapSceneLoader : MonoBehaviour
{
    [Serializable]
    public sealed class ChunkScene
    {
        public string ScenePath;
        public int GridX;
        public int GridZ;
        public Vector3 Center;
        public Vector3 Size;
    }

    public bool LoadChunksInEditMode = true;
    public bool LoadChunksInPlayMode = true;
    public int NeighborRadius = 1;
    public Transform Viewer;
    public ChunkScene[] CommonScenes = Array.Empty<ChunkScene>();
    public ChunkScene[] Chunks = Array.Empty<ChunkScene>();

    private readonly HashSet<string> _requestedLoads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private double _nextUpdateTime;

    private void Update()
    {
        if ((CommonScenes == null || CommonScenes.Length == 0) &&
            (Chunks == null || Chunks.Length == 0))
        {
            return;
        }

        if (Application.isPlaying)
        {
            if (!LoadChunksInPlayMode)
            {
                return;
            }
        }
        else if (!LoadChunksInEditMode)
        {
            return;
        }

        if (!ShouldUpdateNow())
        {
            return;
        }

        var position = ResolveViewerPosition();
        UpdateChunkScenes(position);
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

    private void UpdateChunkScenes(Vector3 viewerPosition)
    {
        var desiredScenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var scene in EnumerateScenes(CommonScenes))
        {
            if (ContainsHorizontal(scene, viewerPosition))
            {
                desiredScenes.Add(scene.ScenePath);
            }
        }

        if (TryResolveViewerChunk(viewerPosition, out var viewerChunk))
        {
            foreach (var chunk in EnumerateScenes(Chunks))
            {
                if (Mathf.Abs(ResolveGridX(chunk) - ResolveGridX(viewerChunk)) <= NeighborRadius &&
                    Mathf.Abs(ResolveGridZ(chunk) - ResolveGridZ(viewerChunk)) <= NeighborRadius)
                {
                    desiredScenes.Add(chunk.ScenePath);
                }
            }
        }

        foreach (var scene in EnumerateScenes(CommonScenes))
        {
            ApplySceneLoadState(scene.ScenePath, desiredScenes);
        }

        foreach (var chunk in EnumerateScenes(Chunks))
        {
            ApplySceneLoadState(chunk.ScenePath, desiredScenes);
        }
    }

    private bool TryResolveViewerChunk(Vector3 viewerPosition, out ChunkScene chunk)
    {
        chunk = null;
        foreach (var candidate in EnumerateScenes(Chunks))
        {
            if (ContainsHorizontal(candidate, viewerPosition))
            {
                chunk = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool ContainsHorizontal(ChunkScene chunk, Vector3 position)
    {
        if (chunk == null || chunk.Size.x <= 0f || chunk.Size.z <= 0f)
        {
            return false;
        }

        var halfX = chunk.Size.x * 0.5f;
        var halfZ = chunk.Size.z * 0.5f;
        return position.x >= chunk.Center.x - halfX &&
               position.x <= chunk.Center.x + halfX &&
               position.z >= chunk.Center.z - halfZ &&
               position.z <= chunk.Center.z + halfZ;
    }

    private static IEnumerable<ChunkScene> EnumerateScenes(ChunkScene[] scenes)
    {
        foreach (var scene in scenes ?? Array.Empty<ChunkScene>())
        {
            if (scene != null && !string.IsNullOrWhiteSpace(scene.ScenePath))
            {
                yield return scene;
            }
        }
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

    private static int ResolveGridX(ChunkScene chunk)
    {
        return TryParseGridIndex(chunk?.ScenePath, out var x, out _) ? x : chunk?.GridX ?? 0;
    }

    private static int ResolveGridZ(ChunkScene chunk)
    {
        return TryParseGridIndex(chunk?.ScenePath, out _, out var z) ? z : chunk?.GridZ ?? 0;
    }

    private static bool TryParseGridIndex(string scenePath, out int x, out int z)
    {
        x = 0;
        z = 0;
        if (string.IsNullOrWhiteSpace(scenePath))
        {
            return false;
        }

        var name = Path.GetFileNameWithoutExtension(scenePath);
        var zMarker = name.LastIndexOf("_z", StringComparison.OrdinalIgnoreCase);
        var xMarker = zMarker > 0
            ? name.LastIndexOf("_x", zMarker, StringComparison.OrdinalIgnoreCase)
            : -1;
        if (xMarker < 0 || zMarker < 0)
        {
            return false;
        }

        return int.TryParse(name.Substring(xMarker + 2, zMarker - xMarker - 2), out x) &&
               int.TryParse(name.Substring(zMarker + 2), out z);
    }

    private void EnsureLoaded(string scenePath)
    {
        if (IsSceneLoaded(scenePath) || !_requestedLoads.Add(scenePath))
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
