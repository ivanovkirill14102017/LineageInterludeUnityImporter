using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class MapImportSceneManager
{
    private const string WorkspaceScenePath = "Assets/MainWorkspace.unity";
    private const string StreamingRootName = "L2SceneStreaming";
    private const string LegacyMapStreamingPrefix = "L2MapStreaming_";

    public static Scene PrepareMapImportScene(MapImportRequest request, Action<string> log)
    {
        EnsureSceneFolders(request);
        EnsureWorkspaceSceneOpen(log);
        RemoveLegacyChunkScenes(request);

        var mapScenePath = GetMapScenePath(request);
        var mapScene = OpenOrCreateScene(mapScenePath);
        EditorSceneManager.SetActiveScene(mapScene);
        log?.Invoke($"[Scenes] Active import scene: {mapScenePath}");
        return mapScene;
    }

    public static bool IsSectionImported(MapImportRequest request, string sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
        {
            return false;
        }

        if (string.Equals(sectionName, "Terrain", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(sectionName, "Context", StringComparison.OrdinalIgnoreCase))
        {
            return HasObjectInMapScene(request, $"{request.ObjectName}_{sectionName}");
        }

        return HasObjectInMapScene(request, $"{request.ObjectName}_{sectionName}");
    }

    public static void RemoveSectionPlacement(MapImportRequest request, string sectionName)
    {
        RemoveNamedObjectFromMapScene(request, $"{request.ObjectName}_{sectionName}");
    }

    public static void FinalizeMapImport(MapImportRequest request, Action<string> log)
    {
        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        RemoveMapLocalLoader(mapRoot);
        MapImportFinalizer.Complete(mapRoot, log);

        EditorSceneManager.SaveScene(mapRoot.scene);
        var configuredWorkspaceLoader = ConfigureWorkspaceLoader(request, BuildMapSceneEntry(request, mapRoot), log);
        SaveWorkspaceScene();
        if (configuredWorkspaceLoader)
        {
            CloseMapScene(mapRoot.scene);
        }
        log?.Invoke(configuredWorkspaceLoader
            ? $"[Scenes] Saved complete map scene. Whole-map streaming is controlled from {WorkspaceScenePath}."
            : $"[Scenes] Saved complete map scene.");
    }

    private static void EnsureWorkspaceSceneOpen(Action<string> log)
    {
        if (!File.Exists(WorkspaceScenePath))
        {
            log?.Invoke($"[Scenes] Workspace scene not found at '{WorkspaceScenePath}'. Keeping current scene as workspace.");
            return;
        }

        var workspace = SceneManager.GetSceneByPath(WorkspaceScenePath);
        if (workspace.IsValid() && workspace.isLoaded)
        {
            return;
        }

        EditorSceneManager.OpenScene(WorkspaceScenePath, OpenSceneMode.Single);
        log?.Invoke($"[Scenes] Opened workspace scene: {WorkspaceScenePath}");
    }

    private static Scene OpenOrCreateScene(string scenePath)
    {
        var existing = SceneManager.GetSceneByPath(scenePath);
        if (existing.IsValid() && existing.isLoaded)
        {
            return existing;
        }

        if (File.Exists(scenePath))
        {
            return EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Additive);
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        EditorSceneManager.SaveScene(scene, scenePath);
        return scene;
    }

    private static bool TryGetWorldBounds(GameObject go, out Bounds bounds)
    {
        var renderers = go.GetComponentsInChildren<Renderer>(true);
        var colliders = go.GetComponentsInChildren<Collider>(true);
        var hasBounds = false;
        bounds = new Bounds(go.transform.position, Vector3.one);

        foreach (var renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        foreach (var collider in colliders)
        {
            if (collider == null)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = collider.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(collider.bounds);
            }
        }

        if (hasBounds)
        {
            return true;
        }

        if (go.GetComponent<Light>() != null || go.GetComponent<ParticleSystem>() != null)
        {
            bounds = new Bounds(go.transform.position, Vector3.one);
            return true;
        }

        return false;
    }

    private static void RemoveMapLocalLoader(GameObject mapRoot)
    {
        var loader = mapRoot != null ? mapRoot.GetComponent<L2MapSceneLoader>() : null;
        if (loader != null)
        {
            UnityEngine.Object.DestroyImmediate(loader);
            EditorUtility.SetDirty(mapRoot);
        }
    }

    private static bool ConfigureWorkspaceLoader(
        MapImportRequest request,
        L2MapSceneLoader.MapScene mapScene,
        Action<string> log)
    {
        var workspace = SceneManager.GetSceneByPath(WorkspaceScenePath);
        if (!workspace.IsValid() || !workspace.isLoaded)
        {
            log?.Invoke($"[Scenes] Workspace scene is not loaded. Map scene streaming metadata was kept only in imported scenes.");
            return false;
        }

        var streamingRoot = GetOrCreateWorkspaceRoot(workspace, StreamingRootName);
        var mapObject = GetOrCreateMapStreamingObject(streamingRoot.transform, request.MapKey);
        var loader = mapObject.GetComponent<L2MapSceneLoader>();
        if (loader == null)
        {
            loader = mapObject.AddComponent<L2MapSceneLoader>();
        }

        loader.Maps = new[] { mapScene };
        loader.LoadMapsInEditMode = true;
        loader.LoadMapsInPlayMode = true;
        loader.NeighborRadius = 1;
        loader.Viewer = null;
        EditorUtility.SetDirty(mapObject);
        EditorUtility.SetDirty(loader);
        return true;
    }

    private static L2MapSceneLoader.MapScene BuildMapSceneEntry(
        MapImportRequest request,
        GameObject mapRoot)
    {
        var bounds = BuildSceneCoverageBounds(mapRoot);
        return new L2MapSceneLoader.MapScene
        {
            ScenePath = GetMapScenePath(request),
            GridX = -1,
            GridZ = -1,
            Center = bounds.center,
            Size = bounds.size
        };
    }

    private static Bounds BuildSceneCoverageBounds(GameObject mapRoot)
    {
        return TryGetWorldBounds(mapRoot, out var bounds)
            ? bounds
            : new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 1000f));
    }

    private static GameObject GetOrCreateWorkspaceRoot(Scene workspace, string name)
    {
        var existing = workspace.GetRootGameObjects()
            .FirstOrDefault(go => go != null && string.Equals(go.name, name, StringComparison.Ordinal));
        if (existing != null)
        {
            return existing;
        }

        var root = new GameObject(name);
        SceneManager.MoveGameObjectToScene(root, workspace);
        EditorUtility.SetDirty(root);
        return root;
    }

    private static GameObject GetOrCreateChild(Transform parent, string name)
    {
        foreach (Transform child in parent)
        {
            if (child != null && string.Equals(child.name, name, StringComparison.Ordinal))
            {
                return child.gameObject;
            }
        }

        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        EditorUtility.SetDirty(go);
        return go;
    }

    private static GameObject GetOrCreateMapStreamingObject(Transform parent, string mapKey)
    {
        foreach (Transform child in parent)
        {
            if (child != null && string.Equals(child.name, mapKey, StringComparison.Ordinal))
            {
                return child.gameObject;
            }
        }

        var legacyName = $"{LegacyMapStreamingPrefix}{mapKey}";
        foreach (Transform child in parent)
        {
            if (child != null && string.Equals(child.name, legacyName, StringComparison.Ordinal))
            {
                child.name = mapKey;
                EditorUtility.SetDirty(child.gameObject);
                return child.gameObject;
            }
        }

        return GetOrCreateChild(parent, mapKey);
    }

    private static void SaveWorkspaceScene()
    {
        var workspace = SceneManager.GetSceneByPath(WorkspaceScenePath);
        if (workspace.IsValid() && workspace.isLoaded)
        {
            EditorSceneManager.SaveScene(workspace);
        }
    }

    private static void CloseMapScene(Scene mapScene)
    {
        if (mapScene.IsValid() && mapScene.isLoaded)
        {
            var workspace = SceneManager.GetSceneByPath(WorkspaceScenePath);
            if (workspace.IsValid() && workspace.isLoaded)
            {
                EditorSceneManager.SetActiveScene(workspace);
            }

            EditorSceneManager.CloseScene(mapScene, true);
        }
    }

    private static void EnsureSceneFolders(MapImportRequest request)
    {
        L2AssetManager.EnsureFolderExists($"{request.OutputDir}/Scenes");
    }

    private static void RemoveLegacyChunkScenes(MapImportRequest request)
    {
        var chunkFolder = $"{request.OutputDir}/Scenes/Chunks";
        for (var index = SceneManager.sceneCount - 1; index >= 0; index--)
        {
            var scene = SceneManager.GetSceneAt(index);
            if (scene.IsValid() &&
                scene.isLoaded &&
                scene.path.StartsWith(chunkFolder + "/", StringComparison.OrdinalIgnoreCase))
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        if (AssetDatabase.IsValidFolder(chunkFolder))
        {
            AssetDatabase.DeleteAsset(chunkFolder);
        }
    }

    private static string GetMapScenePath(MapImportRequest request)
    {
        return $"{request.OutputDir}/Scenes/{request.MapKey}.unity";
    }

    private static bool HasObjectInMapScene(MapImportRequest request, string objectName)
    {
        var scene = SceneManager.GetSceneByPath(GetMapScenePath(request));
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return false;
        }

        return scene.GetRootGameObjects()
            .Any(root => root != null && ContainsObjectNamed(root.transform, objectName));
    }

    private static void RemoveNamedObjectFromMapScene(MapImportRequest request, string objectName)
    {
        var scene = SceneManager.GetSceneByPath(GetMapScenePath(request));
        if (!scene.IsValid() || !scene.isLoaded)
        {
            return;
        }

        foreach (var root in scene.GetRootGameObjects())
        {
            var target = FindObjectNamed(root.transform, objectName);
            if (target != null)
            {
                UnityEngine.Object.DestroyImmediate(target.gameObject);
                return;
            }
        }
    }

    private static Transform FindObjectNamed(Transform root, string objectName)
    {
        if (string.Equals(root.name, objectName, StringComparison.Ordinal))
        {
            return root;
        }

        foreach (Transform child in root)
        {
            var match = FindObjectNamed(child, objectName);
            if (match != null)
            {
                return match;
            }
        }

        return null;
    }

    private static bool ContainsObjectNamed(Transform root, string objectName)
    {
        if (root == null)
        {
            return false;
        }

        if (string.Equals(root.name, objectName, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (Transform child in root)
        {
            if (ContainsObjectNamed(child, objectName))
            {
                return true;
            }
        }

        return false;
    }

}
