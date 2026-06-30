using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using L2Viewer.PackageCore;
using L2Viewer.SceneDomain.Models;
using UnityEditor;
using UnityEngine;

internal static class TerrainGrassDetailBuilder
{
    private const float UnrealToUnityScale = L2WorldScale.UnrealToUnityScale;
    private const string ManagedPrefabPrefix = "GrassDetail_";
    private const string ManagedTreePrefabPrefix = "TerrainTree_";
    private const int DefaultDetailResolution = 512;
    private const int DefaultResolutionPerPatch = 16;
    private const int MaxDensityPerCell = 256;
    private const float BasePatchOpacity = 20f;
    private const float NoiseFrequency = 0.31f;
    private const float NoiseContrast = 1.35f;
    private const int BlurRadius = 2;
    private const float MinimumTreeTerrainHeightTolerance = 1f;
    public static bool IsGrassInstance(SceneStaticMeshInstance instance)
    {
        return StaticMeshImportUtility.IsGrassInstance(instance);
    }

    public static bool IsGrassMeshReference(string meshReference)
    {
        return StaticMeshImportUtility.IsGrassMeshReference(meshReference);
    }

    public static (SceneStaticMeshInstance[] TerrainInstances, SceneStaticMeshInstance[] RegularInstances) SplitTreeInstancesByTerrainSurface(
        IReadOnlyList<SceneStaticMeshInstance> treeInstances,
        GameObject staticMeshRoot,
        Action<string> log)
    {
        if (treeInstances == null || treeInstances.Count == 0)
        {
            return (Array.Empty<SceneStaticMeshInstance>(), Array.Empty<SceneStaticMeshInstance>());
        }

        var terrain = FindTerrain(staticMeshRoot);
        if (terrain == null || terrain.terrainData == null)
        {
            log?.Invoke("[Terrain/Vegetation] Terrain not found while classifying trees. All tree instances will be placed as regular static meshes.");
            return (Array.Empty<SceneStaticMeshInstance>(), treeInstances.Where(instance => instance != null).ToArray());
        }

        var terrainAccepted = new List<SceneStaticMeshInstance>(treeInstances.Count);
        var regularFallback = new List<SceneStaticMeshInstance>();
        var tolerance = ComputeTreeTerrainHeightTolerance(terrain.terrainData);

        foreach (var instance in treeInstances)
        {
            if (instance == null)
            {
                continue;
            }

            if (IsTreeInstanceOnTerrainSurface(instance, terrain, tolerance))
            {
                terrainAccepted.Add(instance);
            }
            else
            {
                regularFallback.Add(instance);
            }
        }

        log?.Invoke($"[Terrain/Vegetation] Tree terrain classification: {terrainAccepted.Count} snapped to terrain, {regularFallback.Count} kept as regular meshes. Height tolerance: {tolerance:F2}.");
        return (terrainAccepted.ToArray(), regularFallback.ToArray());
    }

    public static void PopulateTerrainVegetation(
        IReadOnlyList<SceneStaticMeshInstance> grassInstances,
        IReadOnlyList<SceneStaticMeshInstance> treeInstances,
        IReadOnlyList<SceneTerrainDecorationLayer> terrainDecorationLayers,
        GameObject staticMeshRoot,
        IReadOnlyDictionary<string, Mesh> meshCache,
        StaticMeshMaterialCatalog materialCatalog,
        string outputDir,
        string mapKey,
        Action<string> log)
    {
        var grassCount = grassInstances?.Count ?? 0;
        var treeCount = treeInstances?.Count ?? 0;
        var terrainDecorationCount = terrainDecorationLayers?.Count ?? 0;
        if (grassCount == 0 && treeCount == 0 && terrainDecorationCount == 0)
        {
            log?.Invoke("[Terrain/Vegetation] No grass/tree terrain vegetation sources were found.");
            return;
        }

        var terrain = FindTerrain(staticMeshRoot);
        if (terrain == null || terrain.terrainData == null)
        {
            log?.Invoke("[Terrain/Vegetation] Terrain not found in scene. Skipping terrain vegetation conversion.");
            return;
        }

        var terrainData = terrain.terrainData;
        EnsureDetailResolution(terrainData);
        ClearManagedVegetation(terrainData);

        var detailsRootDir = $"{outputDir}/Terrain/Details";
        var detailPrefabDir = $"{detailsRootDir}/Prefabs";
        var detailPreviewDir = $"{detailsRootDir}/Previews";
        var treePrefabDir = $"{outputDir}/Terrain/Trees/Prefabs";
        var treeMaterialDir = $"{outputDir}/Terrain/Trees/Materials";
        L2AssetManager.EnsureFolderExists(detailsRootDir);
        L2AssetManager.EnsureFolderExists(detailPrefabDir);
        L2AssetManager.EnsureFolderExists(detailPreviewDir);
        L2AssetManager.EnsureFolderExists($"{outputDir}/Terrain/Trees");
        L2AssetManager.EnsureFolderExists(treePrefabDir);
        L2AssetManager.EnsureFolderExists(treeMaterialDir);

        var detailPrototypeList = new List<DetailPrototype>();
        var treePrototypeList = new List<TreePrototype>();
        var treeInstanceList = new List<TreeInstance>();
        var detailWidth = terrainData.detailWidth;
        var detailHeight = terrainData.detailHeight;

        var grassGroups = (grassInstances ?? Array.Empty<SceneStaticMeshInstance>())
            .Where(instance => instance != null && !string.IsNullOrWhiteSpace(instance.MeshReference))
            .GroupBy(instance => instance.MeshReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var terrainDecorationGrassGroups = (terrainDecorationLayers ?? Array.Empty<SceneTerrainDecorationLayer>())
            .Where(layer => layer != null &&
                            !string.IsNullOrWhiteSpace(layer.MeshReference) &&
                            StaticMeshImportUtility.IsGrassMeshReference(layer.MeshReference) &&
                            layer.DensityMapTexture != null &&
                            layer.DensityMapTexture.Width > 0 &&
                            layer.DensityMapTexture.Height > 0)
            .GroupBy(layer => layer.MeshReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var treeGroups = (treeInstances ?? Array.Empty<SceneStaticMeshInstance>())
            .Where(instance => instance != null && !string.IsNullOrWhiteSpace(instance.MeshReference))
            .GroupBy(instance => instance.MeshReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var createdGrassLayers = 0;
        var skippedGrassGroups = 0;
        foreach (var group in grassGroups)
        {
            if (!meshCache.TryGetValue(group.Key, out var mesh) || mesh == null || mesh.vertexCount == 0)
            {
                skippedGrassGroups++;
                continue;
            }

            var materials = StaticMeshRendererMaterialUtility.BuildRendererMaterials(group.Key, mesh, materialCatalog);
            if (materials == null || materials.Length == 0)
            {
                skippedGrassGroups++;
                continue;
            }

            var prefabPath = BuildDetailPrefabPath(detailPrefabDir, group.Key);
            var prefab = CreateOrUpdateDetailPrefab(prefabPath, mesh, materials);
            var prototypeIndex = GetOrAddDetailPrototype(detailPrototypeList, prefab);
            var densityMap = BuildDensityMap(group.ToArray(), terrain, mesh, detailWidth, detailHeight);
            terrainData.detailPrototypes = detailPrototypeList.ToArray();
            terrainData.SetDetailLayer(0, 0, prototypeIndex, densityMap);
            SaveDensityPreview(densityMap, $"{detailPreviewDir}/{mapKey}_{group.Key}_Detail.png");
            createdGrassLayers++;
        }

        foreach (var group in terrainDecorationGrassGroups)
        {
            if (!meshCache.TryGetValue(group.Key, out var mesh) || mesh == null || mesh.vertexCount == 0)
            {
                skippedGrassGroups++;
                continue;
            }

            var materials = StaticMeshRendererMaterialUtility.BuildRendererMaterials(group.Key, mesh, materialCatalog);
            if (materials == null || materials.Length == 0)
            {
                skippedGrassGroups++;
                continue;
            }

            var prefabPath = BuildDetailPrefabPath(detailPrefabDir, group.Key);
            var prefab = CreateOrUpdateDetailPrefab(prefabPath, mesh, materials);
            var prototypeIndex = GetOrAddDetailPrototype(detailPrototypeList, prefab);
            var densityMap = BuildDensityMap(group.ToArray(), detailWidth, detailHeight);
            terrainData.detailPrototypes = detailPrototypeList.ToArray();
            terrainData.SetDetailLayer(0, 0, prototypeIndex, densityMap);
            SaveDensityPreview(densityMap, $"{detailPreviewDir}/{mapKey}_{group.Key}_TerrainDecoDetail.png");
            createdGrassLayers++;
        }

        var createdTreeGroups = 0;
        var skippedTreeGroups = 0;
        foreach (var group in treeGroups)
        {
            if (!meshCache.TryGetValue(group.Key, out var mesh) || mesh == null || mesh.vertexCount == 0)
            {
                skippedTreeGroups++;
                continue;
            }

            var materials = StaticMeshRendererMaterialUtility.BuildRendererMaterials(group.Key, mesh, materialCatalog);
            if (materials == null || materials.Length == 0)
            {
                skippedTreeGroups++;
                continue;
            }

            var prefabPath = BuildTreePrefabPath(treePrefabDir, group.Key);
            var prefab = CreateOrUpdateTreePrefab(prefabPath, mesh, group.Key, materials, treeMaterialDir, log);
            var prototypeIndex = AddTreePrototype(treePrototypeList, prefab);
            AddTreeInstances(treeInstanceList, prototypeIndex, group.ToArray(), terrain);
            createdTreeGroups++;
        }

        terrainData.treePrototypes = treePrototypeList.ToArray();
        terrainData.SetTreeInstances(treeInstanceList.ToArray(), true);

        if (createdGrassLayers > 0 || createdTreeGroups > 0)
        {
            terrain.Flush();
            EditorUtility.SetDirty(terrain);
            EditorUtility.SetDirty(terrainData);
            AssetDatabase.SaveAssets();
        }

        log?.Invoke($"[Terrain/Vegetation] Grass layers: {createdGrassLayers}/{grassGroups.Length + terrainDecorationGrassGroups.Length} groups from staticGrass={grassCount}, terrainDecoGrass={terrainDecorationCount}. Tree prototypes: {createdTreeGroups}/{treeGroups.Length} groups from {treeCount} instances. Skipped grass groups: {skippedGrassGroups}. Skipped tree groups: {skippedTreeGroups}. Tree instances written: {treeInstanceList.Count}.");
    }

    private static Terrain FindTerrain(GameObject staticMeshRoot)
    {
        if (staticMeshRoot == null)
        {
            return null;
        }

        var root = staticMeshRoot.transform.root;
        return root.GetComponentInChildren<Terrain>(true);
    }

    private static void EnsureDetailResolution(TerrainData terrainData)
    {
        if (terrainData.detailWidth > 0 && terrainData.detailHeight > 0)
        {
            return;
        }

        var targetResolution = Mathf.Clamp(
            Mathf.NextPowerOfTwo(Mathf.Max(DefaultDetailResolution, terrainData.alphamapResolution)),
            DefaultResolutionPerPatch,
            2048);
        terrainData.SetDetailResolution(targetResolution, DefaultResolutionPerPatch);
    }

    private static string BuildDetailPrefabPath(string detailPrefabDir, string meshReference)
    {
        var fileName = $"{ManagedPrefabPrefix}{meshReference}.prefab";
        return $"{detailPrefabDir}/{fileName}";
    }

    private static string BuildTreePrefabPath(string treePrefabDir, string meshReference)
    {
        var fileName = $"{ManagedTreePrefabPrefix}{meshReference}.prefab";
        return $"{treePrefabDir}/{fileName}";
    }

    private static GameObject CreateOrUpdateDetailPrefab(string prefabPath, Mesh mesh, Material[] materials)
    {
        var prefabRoot = new GameObject(Path.GetFileNameWithoutExtension(prefabPath));
        try
        {
            prefabRoot.transform.localScale = Vector3.one;
            prefabRoot.isStatic = true;

            var filter = prefabRoot.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = prefabRoot.AddComponent<MeshRenderer>();
            renderer.sharedMaterials = materials;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            renderer.receiveShadows = true;

            var prefab = PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate);
            return prefab;
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(prefabRoot);
        }
    }

    private static GameObject CreateOrUpdateTreePrefab(
        string prefabPath,
        Mesh mesh,
        string meshReference,
        Material[] materials,
        string treeMaterialDir,
        Action<string> log)
    {
        return CreateOrUpdateDetailPrefab(prefabPath, mesh, PrepareTreeMaterials(meshReference, materials, treeMaterialDir, log));
    }

    private static Material[] PrepareTreeMaterials(string meshReference, Material[] materials, string treeMaterialDir, Action<string> log)
    {
        if (materials == null || materials.Length == 0)
        {
            return materials;
        }

        var prepared = new Material[materials.Length];
        for (var i = 0; i < materials.Length; i++)
        {
            prepared[i] = PrepareTreeMaterialAsset(meshReference, treeMaterialDir, materials[i], i, log);
        }

        return prepared;
    }

    private static Material PrepareTreeMaterialAsset(
        string meshReference,
        string treeMaterialDir,
        Material source,
        int index,
        Action<string> log)
    {
        var sourceName = source != null ? source.name : $"TreeMaterial_{index}";
        var assetPath = $"{treeMaterialDir}/{meshReference}_{index:D2}_{sourceName}.mat";
        var assetName = Path.GetFileNameWithoutExtension(assetPath);
        var material = AssetDatabase.LoadAssetAtPath<Material>(assetPath);
        if (material == null)
        {
            material = source != null
                ? new Material(source)
                : new Material(ResolveCompatibleTreeShader(source));
            material.name = assetName;
            AssetDatabase.CreateAsset(material, assetPath);
        }

        ConfigureTreeMaterial(material, source, index, assetName);
        EditorUtility.SetDirty(material);
        AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        log?.Invoke($"[Terrain/Vegetation] Tree material '{sourceName}' on '{meshReference}' uses compatible shader '{material.shader?.name ?? "<null>"}' -> '{assetPath}'.");
        return material;
    }

    private static Shader ResolveCompatibleTreeShader(Material source)
    {
        if (source != null && source.shader != null && source.shader.isSupported)
        {
            return source.shader;
        }

        return StaticMeshImportUtility.FindDefaultShader();
    }

    private static void ConfigureTreeMaterial(Material target, Material source, int index, string assetName)
    {
        if (source != null)
        {
            target.shader = source.shader;
            target.CopyPropertiesFromMaterial(source);
            target.shaderKeywords = source.shaderKeywords;
            target.renderQueue = source.renderQueue;
            target.doubleSidedGI = source.doubleSidedGI;
            target.globalIlluminationFlags = source.globalIlluminationFlags;
        }
        else
        {
            target.shader = ResolveCompatibleTreeShader(source);
        }

        target.name = assetName;
        target.enableInstancing = true;
    }

    private static int GetOrAddDetailPrototype(List<DetailPrototype> prototypeList, GameObject prefab)
    {
        for (var i = 0; i < prototypeList.Count; i++)
        {
            if (prototypeList[i] != null && prototypeList[i].prototype == prefab)
            {
                return i;
            }
        }

        var prototype = new DetailPrototype
        {
            prototype = prefab,
            usePrototypeMesh = true,
            useInstancing = true,
            renderMode = DetailRenderMode.VertexLit,
            minWidth = 1.15f,
            maxWidth = 2.35f,
            minHeight = 0.95f,
            maxHeight = 2.1f,
            noiseSeed = 1337,
            noiseSpread = 0.32f,
            healthyColor = Color.white,
            dryColor = Color.white
        };
        prototypeList.Add(prototype);
        return prototypeList.Count - 1;
    }

    private static int AddTreePrototype(List<TreePrototype> prototypeList, GameObject prefab)
    {
        var prototype = new TreePrototype
        {
            prefab = prefab,
            bendFactor = 0f
        };
        prototypeList.Add(prototype);
        return prototypeList.Count - 1;
    }

    private static void AddTreeInstances(
        List<TreeInstance> treeInstances,
        int prototypeIndex,
        IReadOnlyList<SceneStaticMeshInstance> instances,
        Terrain terrain)
    {
        var terrainData = terrain.terrainData;
        var terrainPosition = terrain.transform.position;
        var terrainSize = terrainData.size;

        foreach (var instance in instances)
        {
            var worldPosition = instance.WorldLocation.TransformFromUnrealToUnityWithScale();
            var localPosition = worldPosition - terrainPosition;
            if (terrainSize.x <= 0f || terrainSize.y <= 0f || terrainSize.z <= 0f)
            {
                continue;
            }

            var normalizedX = localPosition.x / terrainSize.x;
            var normalizedY = localPosition.y / terrainSize.y;
            var normalizedZ = localPosition.z / terrainSize.z;
            if (normalizedX < 0f || normalizedX > 1f || normalizedZ < 0f || normalizedZ > 1f)
            {
                continue;
            }

            var treeInstance = new TreeInstance
            {
                prototypeIndex = prototypeIndex,
                position = new Vector3(
                    Mathf.Clamp01(normalizedX),
                    Mathf.Clamp01(normalizedY),
                    Mathf.Clamp01(normalizedZ)),
                widthScale = 1f,
                heightScale = 1f,
                rotation = -instance.RotationEulerDegrees.Y * Mathf.Deg2Rad,
                color = Color.white,
                lightmapColor = Color.white
            };
            treeInstances.Add(treeInstance);
        }
    }

    private static bool IsTreeInstanceOnTerrainSurface(
        SceneStaticMeshInstance instance,
        Terrain terrain,
        float heightTolerance)
    {
        if (instance == null || terrain == null || terrain.terrainData == null)
        {
            return false;
        }

        var terrainData = terrain.terrainData;
        var terrainPosition = terrain.transform.position;
        var terrainSize = terrainData.size;
        if (terrainSize.x <= 0f || terrainSize.z <= 0f)
        {
            return false;
        }

        var worldPosition = instance.WorldLocation.TransformFromUnrealToUnityWithScale();
        var localPosition = worldPosition - terrainPosition;
        var normalizedX = localPosition.x / terrainSize.x;
        var normalizedZ = localPosition.z / terrainSize.z;
        if (normalizedX < 0f || normalizedX > 1f || normalizedZ < 0f || normalizedZ > 1f)
        {
            return false;
        }

        var terrainSurfaceY = terrainPosition.y + terrainData.GetInterpolatedHeight(normalizedX, normalizedZ);
        return Mathf.Abs(worldPosition.y - terrainSurfaceY) <= heightTolerance;
    }

    private static float ComputeTreeTerrainHeightTolerance(TerrainData terrainData)
    {
        if (terrainData == null)
        {
            return MinimumTreeTerrainHeightTolerance;
        }

        var heightmapScale = terrainData.heightmapScale;
        var adaptiveTolerance = Mathf.Max(heightmapScale.x, heightmapScale.z) * 0.5f;
        return Mathf.Max(MinimumTreeTerrainHeightTolerance, adaptiveTolerance);
    }

    private static void ClearManagedVegetation(TerrainData terrainData)
    {
        terrainData.detailPrototypes = Array.Empty<DetailPrototype>();
        terrainData.SetTreeInstances(Array.Empty<TreeInstance>(), true);
        terrainData.treePrototypes = Array.Empty<TreePrototype>();
    }

    private static int[,] BuildDensityMap(
        IReadOnlyList<SceneStaticMeshInstance> instances,
        Terrain terrain,
        Mesh mesh,
        int detailWidth,
        int detailHeight)
    {
        var terrainData = terrain.terrainData;
        var weights = new float[detailHeight, detailWidth];
        var terrainPosition = terrain.transform.position;
        var terrainSize = terrainData.size;
        var meshBounds = mesh.bounds;
        var seed = StableHash(mesh.name);

        foreach (var instance in instances)
        {
            var worldPosition = instance.WorldLocation.TransformFromUnrealToUnityWithScale();
            var localPosition = worldPosition - terrainPosition;

            if (terrainSize.x <= 0f || terrainSize.z <= 0f)
            {
                continue;
            }

            var normalizedX = localPosition.x / terrainSize.x;
            var normalizedZ = localPosition.z / terrainSize.z;
            if (normalizedX < 0f || normalizedX > 1f || normalizedZ < 0f || normalizedZ > 1f)
            {
                continue;
            }

            var centerX = Mathf.Clamp(Mathf.RoundToInt(normalizedX * (detailWidth - 1)), 0, detailWidth - 1);
            var centerY = Mathf.Clamp(Mathf.RoundToInt(normalizedZ * (detailHeight - 1)), 0, detailHeight - 1);
            var radii = ComputeBrushRadii(meshBounds, instance, terrainSize, detailWidth, detailHeight);
            PaintSoftPatch(weights, centerX, centerY, radii.x, radii.y, seed);
            seed = (seed * 397) ^ centerX ^ (centerY << 8);
        }

        var blurredWeights = Blur(weights, BlurRadius);
        return QuantizeDensity(blurredWeights);
    }

    private static void PaintSoftPatch(float[,] weights, int centerX, int centerY, int radiusX, int radiusY, int seed)
    {
        var height = weights.GetLength(0);
        var width = weights.GetLength(1);
        var patchScale = BasePatchOpacity * Mathf.Lerp(0.85f, 1.25f, Hash01(seed ^ 17));

        for (var y = centerY - radiusY; y <= centerY + radiusY; y++)
        {
            if (y < 0 || y >= height)
            {
                continue;
            }

            for (var x = centerX - radiusX; x <= centerX + radiusX; x++)
            {
                if (x < 0 || x >= width)
                {
                    continue;
                }

                var nx = (x - centerX) / Mathf.Max(1f, radiusX);
                var ny = (y - centerY) / Mathf.Max(1f, radiusY);
                var dist = Mathf.Sqrt((nx * nx) + (ny * ny));
                if (dist > 1f)
                {
                    continue;
                }

                var falloff = Mathf.Pow(1f - dist, 1.8f);
                var noise = SamplePatchNoise(x, y, seed);
                var ragged = Mathf.Clamp01(Mathf.Lerp(noise, 1f, 0.35f));
                var value = falloff * ragged * patchScale;
                weights[y, x] += value;
            }
        }
    }

    private static Vector2Int ComputeBrushRadii(
        Bounds meshBounds,
        SceneStaticMeshInstance instance,
        Vector3 terrainSize,
        int detailWidth,
        int detailHeight)
    {
        var scaleX = Math.Abs(instance.Scale.X);
        var scaleZ = Math.Abs(instance.Scale.Y);
        var footprintX = Mathf.Max(0.05f, meshBounds.size.x * scaleX);
        var footprintZ = Mathf.Max(0.05f, meshBounds.size.z * scaleZ);
        var cellSizeX = terrainSize.x / Mathf.Max(1, detailWidth);
        var cellSizeZ = terrainSize.z / Mathf.Max(1, detailHeight);
        var radiusX = Mathf.Clamp(Mathf.CeilToInt((footprintX / Mathf.Max(0.0001f, cellSizeX)) * 2.6f), 4, 20);
        var radiusY = Mathf.Clamp(Mathf.CeilToInt((footprintZ / Mathf.Max(0.0001f, cellSizeZ)) * 2.6f), 4, 20);
        radiusX = Mathf.RoundToInt(radiusX * Mathf.Lerp(0.85f, 1.35f, Hash01(StableHash(instance.MeshReference) ^ (int)instance.WorldLocation.X)));
        radiusY = Mathf.RoundToInt(radiusY * Mathf.Lerp(0.85f, 1.35f, Hash01(StableHash(instance.MeshReference) ^ (int)instance.WorldLocation.Y)));
        return new Vector2Int(Mathf.Clamp(radiusX, 4, 20), Mathf.Clamp(radiusY, 4, 20));
    }

    private static float[,] Blur(float[,] source, int radius)
    {
        if (radius <= 0)
        {
            return source;
        }

        var height = source.GetLength(0);
        var width = source.GetLength(1);
        var temp = new float[height, width];
        var result = new float[height, width];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sum = 0f;
                var weightSum = 0f;
                for (var k = -radius; k <= radius; k++)
                {
                    var sx = Mathf.Clamp(x + k, 0, width - 1);
                    var weight = radius + 1 - Mathf.Abs(k);
                    sum += source[y, sx] * weight;
                    weightSum += weight;
                }

                temp[y, x] = sum / Mathf.Max(0.0001f, weightSum);
            }
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var sum = 0f;
                var weightSum = 0f;
                for (var k = -radius; k <= radius; k++)
                {
                    var sy = Mathf.Clamp(y + k, 0, height - 1);
                    var weight = radius + 1 - Mathf.Abs(k);
                    sum += temp[sy, x] * weight;
                    weightSum += weight;
                }

                result[y, x] = sum / Mathf.Max(0.0001f, weightSum);
            }
        }

        return result;
    }

    private static int[,] QuantizeDensity(float[,] weights)
    {
        var height = weights.GetLength(0);
        var width = weights.GetLength(1);
        var densityMap = new int[height, width];

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var softened = Mathf.Pow(Mathf.Max(0f, weights[y, x]), 0.92f);
                densityMap[y, x] = Mathf.Clamp(Mathf.RoundToInt(softened), 0, MaxDensityPerCell);
            }
        }

        return densityMap;
    }

    private static int[,] QuantizeDensityNormalized(float[,] weights)
    {
        var height = weights.GetLength(0);
        var width = weights.GetLength(1);
        var densityMap = new int[height, width];
        var maxWeight = 0f;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                maxWeight = Mathf.Max(maxWeight, weights[y, x]);
            }
        }

        if (maxWeight <= 0.0001f)
        {
            return densityMap;
        }

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var normalized = Mathf.Clamp01(weights[y, x] / maxWeight);
                if (normalized <= 0.02f)
                {
                    densityMap[y, x] = 0;
                    continue;
                }

                var boosted = Mathf.Pow(normalized, 0.55f);
                densityMap[y, x] = Mathf.Clamp(Mathf.CeilToInt(boosted * MaxDensityPerCell), 1, MaxDensityPerCell);
            }
        }

        return densityMap;
    }

    private static int[,] BuildDensityMap(
        IReadOnlyList<SceneTerrainDecorationLayer> layers,
        int detailWidth,
        int detailHeight)
    {
        var weights = new float[detailHeight, detailWidth];

        foreach (var layer in layers)
        {
            var densityTexture = layer.DensityMapTexture;
            if (densityTexture == null || densityTexture.Width <= 0 || densityTexture.Height <= 0)
            {
                continue;
            }

            var densityScale = layer.DensityMultiplier?.Max ?? 100f;
            var maxPerQuad = Math.Max(1, layer.MaxPerQuad);
            var layerScale = Mathf.Max(1f, (densityScale / 100f) * maxPerQuad * 8f);

            for (var y = 0; y < detailHeight; y++)
            {
                var v = detailHeight <= 1 ? 0f : y / (float)(detailHeight - 1);
                for (var x = 0; x < detailWidth; x++)
                {
                    var u = detailWidth <= 1 ? 0f : x / (float)(detailWidth - 1);
                    var density = SampleGray01(densityTexture, u, v);
                    if (density <= 0.01f)
                    {
                        continue;
                    }

                    weights[y, x] += Mathf.Pow(density, 0.75f) * layerScale;
                }
            }
        }

        var blurredWeights = Blur(weights, BlurRadius);
        return QuantizeDensityNormalized(blurredWeights);
    }

    private static float SamplePatchNoise(int x, int y, int seed)
    {
        var coarse = Mathf.PerlinNoise((x + (seed * 0.13f)) * NoiseFrequency, (y - (seed * 0.07f)) * NoiseFrequency);
        var fine = Mathf.PerlinNoise((x - (seed * 0.11f)) * (NoiseFrequency * 2.1f), (y + (seed * 0.17f)) * (NoiseFrequency * 2.1f));
        var combined = Mathf.Lerp(coarse, fine, 0.4f);
        return Mathf.Clamp01(Mathf.Pow(combined, NoiseContrast));
    }

    private static float SampleGray01(TextureData texture, float u, float v)
    {
        var x = Mathf.Clamp(Mathf.RoundToInt(u * Mathf.Max(1, texture.Width - 1)), 0, texture.Width - 1);
        var y = Mathf.Clamp(Mathf.RoundToInt(v * Mathf.Max(1, texture.Height - 1)), 0, texture.Height - 1);
        var src = (y * texture.Width + x) * 4;
        return ((0.299f * texture.RgbaBytes[src + 0]) +
                (0.587f * texture.RgbaBytes[src + 1]) +
                (0.114f * texture.RgbaBytes[src + 2])) / 255f;
    }

    private static int StableHash(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        unchecked
        {
            var hash = 23;
            for (var i = 0; i < value.Length; i++)
            {
                hash = (hash * 31) + value[i];
            }

            return hash;
        }
    }

    private static float Hash01(int value)
    {
        unchecked
        {
            var hash = value;
            hash ^= hash >> 16;
            hash = (int)((uint)hash * 0x7feb352dU);
            hash ^= hash >> 15;
            hash = (int)((uint)hash * 0x846ca68bU);
            hash ^= hash >> 16;
            return (hash & 0x7fffffff) / (float)int.MaxValue;
        }
    }

    private static void SaveDensityPreview(int[,] densityMap, string assetPath)
    {
        var height = densityMap.GetLength(0);
        var width = densityMap.GetLength(1);
        var max = 0;

        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                max = Mathf.Max(max, densityMap[y, x]);
            }
        }

        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, true)
        {
            name = Path.GetFileNameWithoutExtension(assetPath)
        };

        try
        {
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var normalized = max <= 0 ? 0f : densityMap[y, x] / (float)max;
                    texture.SetPixel(x, y, new Color(normalized, normalized, normalized, 1f));
                }
            }

            texture.Apply(false, false);
            File.WriteAllBytes(assetPath, texture.EncodeToPNG());
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }
    }
}
