using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using UnityEditor;
using UnityEngine;

internal static class L2StaticMeshAssetBuilder
{
    public static IReadOnlyDictionary<string, GameObject> EnsureStaticMeshPrefabs(
        IReadOnlyDictionary<string, SceneStaticMeshDefinition> meshDefinitions,
        string clientPath,
        string mapKey,
        Action<string> log,
        bool reuseExistingMaterialTextureAssets = true,
        MapImportExecutionContext context = null)
    {
        if (meshDefinitions == null || meshDefinitions.Count == 0)
        {
            return new Dictionary<string, GameObject>(StringComparer.OrdinalIgnoreCase);
        }

        var meshDir = L2AssetManager.SharedStaticMeshesRoot;
        var prefabDir = L2AssetManager.ManagedStaticMeshPrefabsRoot;
        var materialDir = L2AssetManager.SharedMaterialsRoot;
        var textureDir = L2AssetManager.SharedTexturesRoot;
        EnsureStaticMeshAssetFolders(meshDir, prefabDir, materialDir, textureDir);

        var filteredDefinitions = StaticMeshImportUtility.FilterMeshDefinitions(meshDefinitions);
        var shader = StaticMeshImportUtility.FindDefaultShader();

        log($"[StaticMesh/Ensure] Importing {filteredDefinitions.Count} mesh definitions for dependent assets.");
        context?.Report("Particles/Static Mesh Dependencies", "Texture import", 0.32f);

        var textureCatalog = StaticMeshTextureImporter.ImportTextures(
            filteredDefinitions,
            clientPath,
            mapKey,
            textureDir,
            reuseExistingMaterialTextureAssets,
            log);
        context?.ThrowIfCancellationRequested();
        context?.Report("Particles/Static Mesh Dependencies", "Material import", 0.50f);
        var materialCatalog = StaticMeshMaterialImporter.ImportMaterials(
            filteredDefinitions,
            mapKey,
            materialDir,
            shader,
            textureCatalog,
            reuseExistingMaterialTextureAssets);
        context?.ThrowIfCancellationRequested();
        context?.Report("Particles/Static Mesh Dependencies", "Mesh asset build", 0.68f);
        var meshCache = BuildMeshAssets(filteredDefinitions, meshDir, mapKey, context);
        var collisionMeshCache = BuildCollisionMeshAssets(filteredDefinitions, meshDir, mapKey, context);
        context?.ThrowIfCancellationRequested();
        context?.Report("Particles/Static Mesh Dependencies", "Prefab asset build", 0.86f);
        return BuildPlacementAssets(meshCache, collisionMeshCache, materialCatalog, prefabDir, context)
            .Where(pair => pair.Value?.Prefab != null)
            .ToDictionary(pair => pair.Key, pair => pair.Value.Prefab, StringComparer.OrdinalIgnoreCase);
    }

    public static void BuildStaticMeshes(
        SceneInstancedMeshResult instancedResult,
        GameObject parent,
        string clientPath,
        string mapKey,
        string outputDir,
        Action<string> log,
        bool reuseExistingMaterialTextureAssets = true,
        bool placeRegularInstances = true,
        bool placeTerrainDecorations = true,
        bool convertTerrainDecorationsToTerrainVegetation = false,
        bool convertTreeInstancesToTerrainVegetation = true,
        bool placeTreeInstancesAsRegularInstances = false,
        TerrainImportData terrainImport = null,
        bool populateTerrainVegetation = true,
        bool removeExistingConvertedTerrainVegetationFallback = false,
        MapImportExecutionContext context = null)
    {
        var meshDir = L2AssetManager.SharedStaticMeshesRoot;
        var prefabDir = L2AssetManager.ManagedStaticMeshPrefabsRoot;
        var materialDir = L2AssetManager.SharedMaterialsRoot;
        var textureDir = L2AssetManager.SharedTexturesRoot;
        EnsureStaticMeshAssetFolders(meshDir, prefabDir, materialDir, textureDir);

        var meshDefinitions = StaticMeshImportUtility.FilterMeshDefinitions(instancedResult.UniqueMeshes);
        var shader = StaticMeshImportUtility.FindDefaultShader();

        log($"Building {meshDefinitions.Count} unique mesh assets...");

        log("[StaticMesh/Pipeline] START Texture import");
        context?.Report("Static Meshes", "Texture import", 0.34f);
        var textureStopwatch = Stopwatch.StartNew();
        var textureCatalog = StaticMeshTextureImporter.ImportTextures(
                meshDefinitions,
                ConstInfo.L2GameClientPath,
                mapKey,
                textureDir,
                reuseExistingMaterialTextureAssets,
                log);
        textureStopwatch.Stop();
        log($"[StaticMesh/Pipeline] DONE Texture import ({textureStopwatch.Elapsed.TotalSeconds:F2}s)");

        log("[StaticMesh/Pipeline] START Material import");
        context?.ThrowIfCancellationRequested();
        context?.Report("Static Meshes", "Material import", 0.46f);
        var materialStopwatch = Stopwatch.StartNew();
        var materialCatalog = StaticMeshMaterialImporter.ImportMaterials(
                meshDefinitions,
                mapKey,
                materialDir,
                shader,
                textureCatalog,
                reuseExistingMaterialTextureAssets);
        materialStopwatch.Stop();
        log($"[StaticMesh/Pipeline] DONE Material import ({materialStopwatch.Elapsed.TotalSeconds:F2}s)");

        log("[StaticMesh/Pipeline] START Geometry asset build");
        context?.ThrowIfCancellationRequested();
        context?.Report("Static Meshes", "Geometry asset build", 0.58f);
        var geometryStopwatch = Stopwatch.StartNew();
        var meshCache = BuildMeshAssets(meshDefinitions, meshDir, mapKey, context);
        geometryStopwatch.Stop();
        log($"[StaticMesh/Pipeline] DONE Geometry asset build ({geometryStopwatch.Elapsed.TotalSeconds:F2}s)");

        log("[StaticMesh/Pipeline] START Collider prefab asset build");
        context?.ThrowIfCancellationRequested();
        context?.Report("Static Meshes", "Collider prefab asset build", 0.70f);
        var prefabStopwatch = Stopwatch.StartNew();
        var collisionMeshCache = BuildCollisionMeshAssets(meshDefinitions, meshDir, mapKey, context);
        var assetCache = BuildPlacementAssets(meshCache, collisionMeshCache, materialCatalog, prefabDir, context);
        prefabStopwatch.Stop();
        log($"[StaticMesh/Pipeline] DONE Collider prefab asset build ({prefabStopwatch.Elapsed.TotalSeconds:F2}s)");

        log("[StaticMesh/Pipeline] START Instance placement");
        context?.ThrowIfCancellationRequested();
        context?.Report("Static Meshes", "Instance placement", 0.82f);
        var placementStopwatch = Stopwatch.StartNew();
        var regularInstances = instancedResult.Instances
            .Where(instance =>
                !StaticMeshImportUtility.IsGrassInstance(instance) &&
                (!convertTreeInstancesToTerrainVegetation || !StaticMeshImportUtility.IsTreeInstance(instance)))
            .ToArray();
        var grassInstances = instancedResult.Instances
            .Where(StaticMeshImportUtility.IsGrassInstance)
            .ToArray();
        var treeInstances = instancedResult.Instances
            .Where(StaticMeshImportUtility.IsTreeInstance)
            .ToArray();
        SceneStaticMeshInstance[] terrainTreeInstances;
        SceneStaticMeshInstance[] regularTreeInstances;
        if (convertTreeInstancesToTerrainVegetation)
        {
            var split = TerrainGrassDetailBuilder.SplitTreeInstancesByTerrainSurface(treeInstances, meshCache, parent, terrainImport, log);
            terrainTreeInstances = split.TerrainInstances;
            regularTreeInstances = split.RegularInstances;
        }
        else
        {
            terrainTreeInstances = Array.Empty<SceneStaticMeshInstance>();
            regularTreeInstances = treeInstances;
        }

        var treeToTerrainCount = terrainTreeInstances.Length;
        var treeAsRegularCount = regularTreeInstances.Length;
        log($"[StaticMesh/Pipeline] Regular instances: {regularInstances.Length}, grass-to-terrain instances: {grassInstances.Length}, tree-to-terrain instances: {treeToTerrainCount}, tree-as-regular instances: {treeAsRegularCount}.");

        SceneTerrainDecorationLayer[] terrainDecorationTerrainLayers = Array.Empty<SceneTerrainDecorationLayer>();
        SceneTerrainDecorationLayer[] terrainDecorationRegularLayers = Array.Empty<SceneTerrainDecorationLayer>();
        if (convertTerrainDecorationsToTerrainVegetation)
        {
            var split = TerrainGrassDetailBuilder.SplitTerrainDecorationLayersByTerrainSurface(
                instancedResult.TerrainDecorations,
                parent,
                terrainImport,
                clientPath,
                log);
            terrainDecorationTerrainLayers = split.TerrainLayers;
            terrainDecorationRegularLayers = split.RegularLayers;
        }
        else
        {
            terrainDecorationRegularLayers = instancedResult.TerrainDecorations?.ToArray() ?? Array.Empty<SceneTerrainDecorationLayer>();
        }

        if (removeExistingConvertedTerrainVegetationFallback)
        {
            context?.ThrowIfCancellationRequested();
            RemoveExistingTerrainVegetationFallback(parent, terrainTreeInstances, terrainDecorationTerrainLayers, log);
        }

        if (placeRegularInstances)
        {
            context?.ThrowIfCancellationRequested();
            StaticMeshInstancePlacer.PlaceInstances(regularInstances, parent, assetCache, log);
        }

        var shouldPlaceRegularTreeInstances = regularTreeInstances.Length > 0 &&
                                              (placeRegularInstances || placeTreeInstancesAsRegularInstances);
        if (shouldPlaceRegularTreeInstances)
        {
            context?.ThrowIfCancellationRequested();
            StaticMeshInstancePlacer.PlaceInstances(regularTreeInstances, parent, assetCache, log);
        }

        if (populateTerrainVegetation)
        {
            context?.ThrowIfCancellationRequested();
            TerrainGrassDetailBuilder.PopulateTerrainVegetation(
                grassInstances,
                terrainTreeInstances,
                convertTerrainDecorationsToTerrainVegetation ? terrainDecorationTerrainLayers : null,
                parent,
                meshCache,
                materialCatalog,
                clientPath,
                outputDir,
                mapKey,
                log);
        }

        if (placeTerrainDecorations)
        {
            context?.ThrowIfCancellationRequested();
            TerrainDecorationInstancePlacer.PlaceDecorations(terrainDecorationRegularLayers, parent, assetCache, clientPath, log);
        }
        placementStopwatch.Stop();
        log($"[StaticMesh/Pipeline] DONE Instance placement ({placementStopwatch.Elapsed.TotalSeconds:F2}s)");
    }

    private static void RemoveExistingTerrainVegetationFallback(
        GameObject parent,
        IReadOnlyList<SceneStaticMeshInstance> terrainTreeInstances,
        IReadOnlyList<SceneTerrainDecorationLayer> terrainDecorationTerrainLayers,
        Action<string> log)
    {
        var staticMeshRoot = FindExistingStaticMeshRoot(parent);
        if (staticMeshRoot == null)
        {
            return;
        }

        var removedTreeCount = RemoveChildrenByExactName(
            staticMeshRoot.transform,
            terrainTreeInstances?
                .Where(instance => instance != null && !string.IsNullOrWhiteSpace(instance.StableName))
                .Select(instance => instance.StableName));
        var removedDecorationCount = RemoveTerrainDecorationChildren(staticMeshRoot.transform, terrainDecorationTerrainLayers);

        if (removedTreeCount > 0 || removedDecorationCount > 0)
        {
            log?.Invoke($"[Terrain/Vegetation] Removed {removedTreeCount} terrain-tree fallback instances and {removedDecorationCount} terrain-decoration fallback instances from '{staticMeshRoot.name}'.");
        }
    }

    private static GameObject FindExistingStaticMeshRoot(GameObject parent)
    {
        if (parent == null)
        {
            return null;
        }

        if (parent.name.EndsWith("_StaticMeshes", StringComparison.OrdinalIgnoreCase))
        {
            return parent;
        }

        var root = parent.transform.root;
        if (root == null)
        {
            return null;
        }

        var expectedRoot = root.Find($"{root.name}_StaticMeshes");
        return expectedRoot != null ? expectedRoot.gameObject : null;
    }

    private static int RemoveChildrenByExactName(Transform parent, IEnumerable<string> names)
    {
        if (parent == null || names == null)
        {
            return 0;
        }

        var targets = new HashSet<string>(names.Where(name => !string.IsNullOrWhiteSpace(name)), StringComparer.Ordinal);
        if (targets.Count == 0)
        {
            return 0;
        }

        var toRemove = new List<GameObject>();
        foreach (Transform child in parent)
        {
            if (child != null && targets.Contains(child.name))
            {
                toRemove.Add(child.gameObject);
            }
        }

        for (var i = 0; i < toRemove.Count; i++)
        {
            UnityEngine.Object.DestroyImmediate(toRemove[i]);
        }

        return toRemove.Count;
    }

    private static int RemoveTerrainDecorationChildren(Transform staticMeshRoot, IReadOnlyList<SceneTerrainDecorationLayer> layers)
    {
        if (staticMeshRoot == null || layers == null || layers.Count == 0)
        {
            return 0;
        }

        var terrainDecorationRoot = staticMeshRoot.Find("TerrainDecorations");
        if (terrainDecorationRoot == null)
        {
            return 0;
        }

        var prefixes = layers
            .Where(layer => layer != null && !string.IsNullOrWhiteSpace(layer.TerrainActorName))
            .Select(layer => $"{layer.TerrainActorName}:Deco{layer.LayerIndex:D2}:")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (prefixes.Length == 0)
        {
            return 0;
        }

        var toRemove = new List<GameObject>();
        foreach (Transform child in terrainDecorationRoot)
        {
            if (child == null)
            {
                continue;
            }

            if (prefixes.Any(prefix => child.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            {
                toRemove.Add(child.gameObject);
            }
        }

        for (var i = 0; i < toRemove.Count; i++)
        {
            UnityEngine.Object.DestroyImmediate(toRemove[i]);
        }

        if (terrainDecorationRoot.childCount == 0)
        {
            UnityEngine.Object.DestroyImmediate(terrainDecorationRoot.gameObject);
        }

        return toRemove.Count;
    }

    private static void EnsureStaticMeshAssetFolders(string meshDir, string prefabDir, string materialDir, string textureDir)
    {
        L2AssetManager.EnsureFolderExists(meshDir);
        L2AssetManager.EnsureFolderExists(prefabDir);
        L2AssetManager.EnsureFolderExists(materialDir);
        L2AssetManager.EnsureFolderExists(textureDir);
    }

    private static Dictionary<string, StaticMeshPlacementAsset> BuildPlacementAssets(
        IReadOnlyDictionary<string, Mesh> meshCache,
        IReadOnlyDictionary<string, Mesh> collisionMeshCache,
        StaticMeshMaterialCatalog materialCatalog,
        string prefabDir,
        MapImportExecutionContext context = null)
    {
        var assetCache = new Dictionary<string, StaticMeshPlacementAsset>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in meshCache)
        {
            context?.ThrowIfCancellationRequested();
            var meshReference = pair.Key;
            var mesh = pair.Value;
            if (mesh == null || mesh.vertexCount == 0 || mesh.subMeshCount == 0)
            {
                continue;
            }

            var materials = StaticMeshRendererMaterialUtility.BuildRendererMaterials(meshReference, mesh, materialCatalog);
            if (materials == null || materials.Length == 0)
            {
                continue;
            }

            var prefabPath = L2AssetManager.BuildClientPackageAssetPath(
                prefabDir,
                meshReference,
                "PF",
                "prefab",
                "StaticMeshPrefabs");

            materialCatalog.FlipbooksByMeshReference.TryGetValue(meshReference, out var flipbooks);
            collisionMeshCache.TryGetValue(meshReference, out var collisionMesh);
            GameObject prefab = null;
            if (collisionMesh != null)
            {
                prefab = LoadOrCreatePrefab(prefabPath, mesh, materials, flipbooks, collisionMesh);
            }

            assetCache[meshReference] = new StaticMeshPlacementAsset(mesh, materials, flipbooks, prefab);
        }

        return assetCache;
    }

    private static Dictionary<string, Mesh> BuildCollisionMeshAssets(
        IReadOnlyDictionary<string, SceneStaticMeshDefinition> meshDefinitions,
        string meshDir,
        string mapKey,
        MapImportExecutionContext context = null)
    {
        var meshCache = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);

        UnityAssetDatabaseUtility.RunAssetEditingBatch(() =>
        {
            foreach (var pair in meshDefinitions)
            {
                context?.ThrowIfCancellationRequested();
                var meshReference = pair.Key;
                var definition = pair.Value;
                if (definition.CollisionGeometry == null ||
                    definition.CollisionGeometry.Triangles == null ||
                    definition.CollisionGeometry.Triangles.Count == 0)
                {
                    continue;
                }

                var meshAssetPath = L2AssetManager.BuildClientPackageAssetPath(
                    meshDir,
                    meshReference,
                    "SMC",
                    "asset",
                    $"{mapKey}/StaticMeshColliders");

                var mesh = LoadOrCreateMeshAsset(definition.CollisionGeometry, meshAssetPath);
                if (mesh == null || mesh.vertexCount == 0 || mesh.subMeshCount == 0)
                {
                    continue;
                }

                meshCache[meshReference] = mesh;
            }
        });

        return meshCache;
    }

    private static Dictionary<string, Mesh> BuildMeshAssets(
        IReadOnlyDictionary<string, SceneStaticMeshDefinition> meshDefinitions,
        string meshDir,
        string mapKey,
        MapImportExecutionContext context = null)
    {
        var meshCache = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);

        UnityAssetDatabaseUtility.RunAssetEditingBatch(() =>
        {
            foreach (var pair in meshDefinitions)
            {
                context?.ThrowIfCancellationRequested();
                var meshReference = pair.Key;
                var definition = pair.Value;
                if (definition.RenderGeometry == null || definition.RenderGeometry.Triangles == null || definition.RenderGeometry.Triangles.Count == 0)
                {
                    continue;
                }

                var meshAssetPath = L2AssetManager.BuildClientPackageAssetPath(
                    meshDir,
                    meshReference,
                    "SM",
                    "asset",
                    $"{mapKey}/StaticMeshes");

                var mesh = LoadOrCreateMeshAsset(definition.RenderGeometry, meshAssetPath);
                if (mesh == null || mesh.vertexCount == 0 || mesh.subMeshCount == 0)
                {
                    continue;
                }

                meshCache[meshReference] = mesh;
            }
        });

        return meshCache;
    }

    private struct VertexKey : IEquatable<VertexKey>
    {
        public Vector3 Position;
        public Vector3 Normal;
        public Vector2 UV;

        public bool Equals(VertexKey other)
        {
            return Position == other.Position && Normal == other.Normal && UV == other.UV;
        }

        public override bool Equals(object obj) => obj is VertexKey other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 23 + Position.GetHashCode();
                hash = hash * 23 + Normal.GetHashCode();
                hash = hash * 23 + UV.GetHashCode();
                return hash;
            }
        }
    }

    private static Mesh LoadOrCreateMeshAsset(SceneTriangleMeshData meshData, string assetPath)
    {
        var existing = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (existing != null)
        {
            return existing;
        }

        return CreateMeshAsset(meshData, assetPath);
    }

    private static Mesh CreateMeshAsset(SceneTriangleMeshData meshData, string assetPath)
    {
        var unityMesh = new Mesh
        {
            name = meshData.Name ?? "StaticMesh",
            indexFormat = UnityEngine.Rendering.IndexFormat.UInt32
        };

        var subMeshIndices = new Dictionary<int, List<int>>();
        var materialIds = StaticMeshImportUtility.CollectMaterialIds(meshData);
        foreach (var materialId in materialIds)
        {
            subMeshIndices[materialId] = new List<int>();
        }

        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var vertexCache = new Dictionary<VertexKey, int>();

        int GetOrAddVertex(Vector3 pos, Vector3 norm, Vector2 uv)
        {
            var key = new VertexKey { Position = pos, Normal = norm, UV = uv };
            if (vertexCache.TryGetValue(key, out var index))
            {
                return index;
            }

            index = vertices.Count;
            vertices.Add(pos);
            normals.Add(norm);
            uvs.Add(uv);
            vertexCache[key] = index;
            return index;
        }

        foreach (var triangle in meshData.Triangles)
        {
            var indices = subMeshIndices[triangle.MaterialId];

            var pA = ConvertPosition(triangle.A.Position);
            var nA = ConvertPosition(triangle.A.Normal);
            var uvA = new Vector2(triangle.A.UV.X, 1.0f - triangle.A.UV.Y);
            indices.Add(GetOrAddVertex(pA, nA, uvA));

            var pC = ConvertPosition(triangle.C.Position);
            var nC = ConvertPosition(triangle.C.Normal);
            var uvC = new Vector2(triangle.C.UV.X, 1.0f - triangle.C.UV.Y);
            indices.Add(GetOrAddVertex(pC, nC, uvC));

            var pB = ConvertPosition(triangle.B.Position);
            var nB = ConvertPosition(triangle.B.Normal);
            var uvB = new Vector2(triangle.B.UV.X, 1.0f - triangle.B.UV.Y);
            indices.Add(GetOrAddVertex(pB, nB, uvB));
        }

        unityMesh.vertices = vertices.ToArray();
        unityMesh.normals = normals.ToArray();
        unityMesh.uv = uvs.ToArray();
        unityMesh.subMeshCount = materialIds.Count;

        for (var i = 0; i < materialIds.Count; i++)
        {
            unityMesh.SetTriangles(subMeshIndices[materialIds[i]].ToArray(), i);
        }

        unityMesh.RecalculateBounds();
        L2AssetManager.EnsureParentFolderExists(assetPath);
        AssetDatabase.CreateAsset(unityMesh, assetPath);
        return unityMesh;
    }

    private static GameObject LoadOrCreatePrefab(
        string prefabPath,
        Mesh mesh,
        Material[] materials,
        Texture2D[][] flipbooks,
        Mesh collisionMesh)
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null)
        {
            return existing;
        }

        return CreatePrefab(prefabPath, mesh, materials, flipbooks, collisionMesh);
    }

    private static GameObject CreatePrefab(
        string prefabPath,
        Mesh mesh,
        Material[] materials,
        Texture2D[][] flipbooks,
        Mesh collisionMesh)
    {
        var prefabRoot = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
        try
        {
            prefabRoot.transform.localScale = Vector3.one;
            prefabRoot.isStatic = true;

            var geometryRoot = new GameObject("Geometry");
            geometryRoot.isStatic = true;
            geometryRoot.transform.SetParent(prefabRoot.transform, false);
            geometryRoot.transform.localPosition = Vector3.zero;
            geometryRoot.transform.localRotation = Quaternion.identity;
            geometryRoot.transform.localScale = Vector3.one;

            var filter = geometryRoot.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = geometryRoot.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            StaticMeshFlipbookUtility.ApplyFlipbooks(geometryRoot, renderer, flipbooks);

            if (collisionMesh != null)
            {
                var colliderRoot = new GameObject("Collider");
                colliderRoot.isStatic = true;
                colliderRoot.transform.SetParent(prefabRoot.transform, false);
                colliderRoot.transform.localPosition = Vector3.zero;
                colliderRoot.transform.localRotation = Quaternion.identity;
                colliderRoot.transform.localScale = Vector3.one;

                var collider = colliderRoot.AddComponent<MeshCollider>();
                collider.sharedMesh = collisionMesh;
            }

            L2AssetManager.EnsureParentFolderExists(prefabPath);
            var prefab = PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            return prefab;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(prefabRoot);
        }
    }

    private static Vector3 ConvertPosition(System.Numerics.Vector3 raw)
    {
        return new Vector3(raw.X * L2WorldScale.BakeUnrealToUnityScale, raw.Z * L2WorldScale.BakeUnrealToUnityScale, raw.Y * L2WorldScale.BakeUnrealToUnityScale);
    }
}
