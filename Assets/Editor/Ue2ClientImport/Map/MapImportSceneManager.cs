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
    private const int ChunkGridSize = 10;

    public static Scene PrepareMapImportScene(MapImportRequest request, Action<string> log)
    {
        EnsureSceneFolders(request);
        EnsureWorkspaceSceneOpen(log);

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

        return HasObjectInMapScene(request, $"{request.ObjectName}_{sectionName}") || HasAnyChunkScene(request);
    }

    public static void FinalizeChunkedMapImport(MapImportRequest request, Action<string> log)
    {
        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        var chunks = BuildChunkScenes(request, mapRoot, log);
        ConfigureLoader(mapRoot, chunks);
        MapImportFinalizer.Complete(mapRoot, log);

        EditorSceneManager.SaveScene(mapRoot.scene);
        CloseChunkScenes(chunks);
        log?.Invoke($"[Scenes] Saved map scene with {chunks.Count} chunk scene references.");
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

    private static List<L2ChunkedMapSceneLoader.ChunkScene> BuildChunkScenes(
        MapImportRequest request,
        GameObject mapRoot,
        Action<string> log)
    {
        var movable = CollectMovableObjects(mapRoot).ToArray();
        if (movable.Length == 0)
        {
            var existingLoader = mapRoot.GetComponent<L2ChunkedMapSceneLoader>();
            if (existingLoader != null && existingLoader.Chunks != null && existingLoader.Chunks.Length > 0)
            {
                log?.Invoke("[Scenes] No new map objects to partition. Reusing existing chunk scene metadata.");
                return existingLoader.Chunks.ToList();
            }

            log?.Invoke("[Scenes] No new map objects to partition.");
            return new List<L2ChunkedMapSceneLoader.ChunkScene>();
        }

        var layout = ChunkLayout.FromObjects(movable);
        var chunks = CreateChunkScenes(request, layout);
        var rootsByChunk = new Dictionary<(int X, int Z, string Category), Transform>();

        foreach (var entry in movable)
        {
            var index = layout.ResolveIndex(entry.Bounds.center);
            var chunk = chunks[(index.Z * ChunkGridSize) + index.X];
            var categoryRoot = GetOrCreateChunkCategoryRoot(chunk.ScenePath, chunk.Scene, index.X, index.Z, entry.Category, rootsByChunk);

            entry.GameObject.transform.SetParent(null, true);
            SceneManager.MoveGameObjectToScene(entry.GameObject, chunk.Scene);
            entry.GameObject.transform.SetParent(categoryRoot, true);
        }

        foreach (var chunk in chunks)
        {
            EditorSceneManager.SaveScene(chunk.Scene, chunk.ScenePath);
        }

        log?.Invoke($"[Scenes] Partitioned {movable.Length} objects into {chunks.Count} chunk scenes.");
        return chunks
            .Select(x => new L2ChunkedMapSceneLoader.ChunkScene
            {
                ScenePath = x.ScenePath,
                Center = x.Bounds.center,
                Size = x.Bounds.size
            })
            .ToList();
    }

    private static IReadOnlyList<ChunkSceneHandle> CreateChunkScenes(MapImportRequest request, ChunkLayout layout)
    {
        var result = new List<ChunkSceneHandle>(ChunkGridSize * ChunkGridSize);
        for (var z = 0; z < ChunkGridSize; z++)
        {
            for (var x = 0; x < ChunkGridSize; x++)
            {
                var scenePath = GetChunkScenePath(request, x, z);
                var scene = OpenOrCreateScene(scenePath);
                var bounds = layout.GetChunkBounds(x, z);
                result.Add(new ChunkSceneHandle(scenePath, scene, bounds));
            }
        }

        return result;
    }

    private static Transform GetOrCreateChunkCategoryRoot(
        string scenePath,
        Scene scene,
        int x,
        int z,
        string category,
        Dictionary<(int X, int Z, string Category), Transform> cache)
    {
        var key = (x, z, category ?? string.Empty);
        if (cache.TryGetValue(key, out var cached) && cached != null)
        {
            return cached;
        }

        var rootName = $"{Path.GetFileNameWithoutExtension(scenePath)}_{category}";
        var existing = scene.GetRootGameObjects()
            .FirstOrDefault(go => go != null && string.Equals(go.name, rootName, StringComparison.Ordinal));
        if (existing != null)
        {
            cache[key] = existing.transform;
            return existing.transform;
        }

        var root = new GameObject(rootName);
        SceneManager.MoveGameObjectToScene(root, scene);
        cache[key] = root.transform;
        return root.transform;
    }

    private static IEnumerable<MovableSceneObject> CollectMovableObjects(GameObject mapRoot)
    {
        if (mapRoot == null)
        {
            yield break;
        }

        foreach (Transform categoryTransform in mapRoot.transform)
        {
            if (categoryTransform == null || ShouldKeepInMapScene(categoryTransform.gameObject))
            {
                continue;
            }

            var category = BuildCategoryName(categoryTransform.name);
            foreach (var movable in EnumerateCategoryMovables(categoryTransform, category))
            {
                yield return movable;
            }
        }
    }

    private static IEnumerable<MovableSceneObject> EnumerateCategoryMovables(Transform root, string category)
    {
        foreach (Transform child in root)
        {
            if (child == null)
            {
                continue;
            }

            if (IsGroupingOnly(child.gameObject))
            {
                foreach (var nested in EnumerateCategoryMovables(child, $"{category}_{child.name}"))
                {
                    yield return nested;
                }

                continue;
            }

            if (TryGetWorldBounds(child.gameObject, out var bounds))
            {
                yield return new MovableSceneObject(child.gameObject, category, bounds);
            }
        }
    }

    private static bool ShouldKeepInMapScene(GameObject go)
    {
        return go.name.EndsWith("_Terrain", StringComparison.OrdinalIgnoreCase) ||
               go.name.EndsWith("_Context", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildCategoryName(string rootName)
    {
        var suffixIndex = rootName.LastIndexOf('_');
        return suffixIndex >= 0 && suffixIndex + 1 < rootName.Length
            ? rootName[(suffixIndex + 1)..]
            : rootName;
    }

    private static bool IsGroupingOnly(GameObject go)
    {
        return go.GetComponent<Renderer>() == null &&
               go.GetComponent<Collider>() == null &&
               go.GetComponent<Light>() == null &&
               go.GetComponent<ParticleSystem>() == null &&
               go.GetComponent<Terrain>() == null &&
               go.transform.childCount > 0 &&
               go.GetComponents<Component>().Length <= 1;
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

    private static void ConfigureLoader(GameObject mapRoot, IReadOnlyList<L2ChunkedMapSceneLoader.ChunkScene> chunks)
    {
        var loader = mapRoot.GetComponent<L2ChunkedMapSceneLoader>();
        if (loader == null)
        {
            loader = mapRoot.AddComponent<L2ChunkedMapSceneLoader>();
        }

        loader.Chunks = chunks.ToArray();
        loader.LoadChunksInEditMode = true;
        loader.LoadChunksInPlayMode = true;
        loader.LoadRadius = 240f;
        loader.UnloadRadius = 340f;
        EditorUtility.SetDirty(loader);
    }

    private static void CloseChunkScenes(IReadOnlyList<L2ChunkedMapSceneLoader.ChunkScene> chunks)
    {
        foreach (var chunk in chunks)
        {
            var scene = SceneManager.GetSceneByPath(chunk.ScenePath);
            if (scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    private static void EnsureSceneFolders(MapImportRequest request)
    {
        L2AssetManager.EnsureFolderExists($"{request.OutputDir}/Scenes");
        L2AssetManager.EnsureFolderExists($"{request.OutputDir}/Scenes/Chunks");
    }

    private static string GetMapScenePath(MapImportRequest request)
    {
        return $"{request.OutputDir}/Scenes/{request.MapKey}.unity";
    }

    private static string GetChunkScenePath(MapImportRequest request, int x, int z)
    {
        return $"{request.OutputDir}/Scenes/Chunks/{request.MapKey}_x{x:D2}_z{z:D2}.unity";
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

    private static bool HasAnyChunkScene(MapImportRequest request)
    {
        for (var z = 0; z < ChunkGridSize; z++)
        {
            for (var x = 0; x < ChunkGridSize; x++)
            {
                if (File.Exists(GetChunkScenePath(request, x, z)))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private readonly struct MovableSceneObject
    {
        public MovableSceneObject(GameObject gameObject, string category, Bounds bounds)
        {
            GameObject = gameObject;
            Category = category;
            Bounds = bounds;
        }

        public GameObject GameObject { get; }
        public string Category { get; }
        public Bounds Bounds { get; }
    }

    private readonly struct ChunkSceneHandle
    {
        public ChunkSceneHandle(string scenePath, Scene scene, Bounds bounds)
        {
            ScenePath = scenePath;
            Scene = scene;
            Bounds = bounds;
        }

        public string ScenePath { get; }
        public Scene Scene { get; }
        public Bounds Bounds { get; }
    }

    private readonly struct ChunkLayout
    {
        private readonly Bounds _bounds;
        private readonly float _cellX;
        private readonly float _cellZ;

        private ChunkLayout(Bounds bounds)
        {
            _bounds = bounds;
            _cellX = Mathf.Max(1f, bounds.size.x / ChunkGridSize);
            _cellZ = Mathf.Max(1f, bounds.size.z / ChunkGridSize);
        }

        public static ChunkLayout FromObjects(IReadOnlyList<MovableSceneObject> objects)
        {
            if (objects == null || objects.Count == 0)
            {
                return new ChunkLayout(new Bounds(Vector3.zero, new Vector3(1000f, 1000f, 1000f)));
            }

            var bounds = objects[0].Bounds;
            for (var i = 1; i < objects.Count; i++)
            {
                bounds.Encapsulate(objects[i].Bounds);
            }

            bounds.Expand(new Vector3(2f, 2f, 2f));
            return new ChunkLayout(bounds);
        }

        public (int X, int Z) ResolveIndex(Vector3 position)
        {
            var localX = Mathf.FloorToInt((position.x - _bounds.min.x) / _cellX);
            var localZ = Mathf.FloorToInt((position.z - _bounds.min.z) / _cellZ);
            return (
                Mathf.Clamp(localX, 0, ChunkGridSize - 1),
                Mathf.Clamp(localZ, 0, ChunkGridSize - 1));
        }

        public Bounds GetChunkBounds(int x, int z)
        {
            var center = new Vector3(
                _bounds.min.x + (_cellX * (x + 0.5f)),
                _bounds.center.y,
                _bounds.min.z + (_cellZ * (z + 0.5f)));
            return new Bounds(center, new Vector3(_cellX, _bounds.size.y, _cellZ));
        }
    }
}
