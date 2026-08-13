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
        public Vector3 Center;
        public Vector3 Size;
    }

    public bool LoadChunksInEditMode = true;
    public bool LoadChunksInPlayMode = true;
    public float LoadRadius = 240f;
    public float UnloadRadius = 340f;
    public Transform Viewer;
    public ChunkScene[] Chunks = Array.Empty<ChunkScene>();

    private readonly HashSet<string> _requestedLoads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private double _nextUpdateTime;

    private void Update()
    {
        if (Chunks == null || Chunks.Length == 0)
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
        foreach (var chunk in Chunks)
        {
            if (chunk == null || string.IsNullOrWhiteSpace(chunk.ScenePath))
            {
                continue;
            }

            var distance = HorizontalDistance(viewerPosition, chunk.Center);
            if (distance <= LoadRadius)
            {
                EnsureLoaded(chunk.ScenePath);
            }
            else if (distance >= UnloadRadius)
            {
                EnsureUnloaded(chunk.ScenePath);
            }
        }
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        var dx = a.x - b.x;
        var dz = a.z - b.z;
        return Mathf.Sqrt((dx * dx) + (dz * dz));
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
