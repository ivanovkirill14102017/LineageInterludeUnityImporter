using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using L2Viewer.PackageCore;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Services.MaterialServices;
using UnityEditor;
using UnityEngine;

internal static class TerrainGrassDetailBuilder
{
    private readonly struct TerrainSurfaceDescriptor
    {
        public TerrainSurfaceDescriptor(Vector3 position, Vector3 size, int width, int height, IReadOnlyList<float> heightSamples, float heightTolerance)
        {
            Position = position;
            Size = size;
            Width = width;
            Height = height;
            HeightSamples = heightSamples;
            HeightTolerance = heightTolerance;
        }

        public Vector3 Position { get; }
        public Vector3 Size { get; }
        public int Width { get; }
        public int Height { get; }
        public IReadOnlyList<float> HeightSamples { get; }
        public float HeightTolerance { get; }
    }

    private const float UnrealToUnityScale = L2WorldScale.UnrealToUnityScale;
    private const string TerrainDetailPrefabSuffix = "grass_detail";
    private const string TerrainTreePrefabSuffix = "terrain_tree";
    private const int DefaultDetailResolution = 512;
    private const int DefaultResolutionPerPatch = 16;
    private const int MaxDensityPerCell = 256;
    private const int MaxTerrainDecorationSampleResolution = 128;
    private const float TerrainDecorationTerrainAcceptanceThreshold = 0.65f;
    private const float GrassPatchRadiusMultiplier = 2f;
    private const float GrassDensityMultiplier = 1.3f;
    private const float GrassRandomJitter = 0.10f;
    private const float GrassSizeBaseMultiplier = 1.5f;
    private const float GrassSizeRandomRangeMultiplier = 2f;
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

    public static (SceneTerrainDecorationLayer[] TerrainLayers, SceneTerrainDecorationLayer[] RegularLayers) SplitTerrainDecorationLayersByTerrainSurface(
        IReadOnlyList<SceneTerrainDecorationLayer> terrainDecorationLayers,
        GameObject staticMeshRoot,
        TerrainImportData terrainImport,
        string clientPath,
        Action<string> log)
    {
        if (terrainDecorationLayers == null || terrainDecorationLayers.Count == 0)
        {
            return (Array.Empty<SceneTerrainDecorationLayer>(), Array.Empty<SceneTerrainDecorationLayer>());
        }

        var terrain = FindTerrain(staticMeshRoot);
        if (terrain == null || terrain.terrainData == null)
        {
            if (!TryBuildTerrainSurface(terrainImport, out var terrainSurface))
            {
                log?.Invoke("[Terrain/Vegetation] Terrain surface not found while classifying terrain decoration layers. All layers will be kept as regular decorations.");
                return (Array.Empty<SceneTerrainDecorationLayer>(), terrainDecorationLayers.Where(layer => layer != null).ToArray());
            }

            return SplitTerrainDecorationLayersByRawTerrainSurface(terrainDecorationLayers, terrainSurface, clientPath, log);
        }

        var tolerance = ComputeTreeTerrainHeightTolerance(terrain.terrainData);
        var textureManager = new BspTextureManager(clientPath);
        var heightTextureRequests = terrainDecorationLayers
            .Where(layer => layer?.HeightMapResource != null)
            .Select(layer => new SceneTextureRequest(layer.HeightMapResource.PackageName, layer.HeightMapResource.ObjectName))
            .Distinct()
            .ToArray();
        var resolvedHeightTextures = ResolveTerrainDecorationHeightTextures(textureManager, heightTextureRequests);
        var terrainLayers = new List<SceneTerrainDecorationLayer>(terrainDecorationLayers.Count);
        var regularLayers = new List<SceneTerrainDecorationLayer>();

        foreach (var layer in terrainDecorationLayers)
        {
            if (layer == null)
            {
                continue;
            }

            if (IsTerrainDecorationLayerOnTerrainSurface(layer, terrain, resolvedHeightTextures, tolerance))
            {
                terrainLayers.Add(layer);
            }
            else
            {
                regularLayers.Add(layer);
            }
        }

        log?.Invoke($"[Terrain/Vegetation] Terrain decoration classification: {terrainLayers.Count} layers to terrain vegetation, {regularLayers.Count} layers kept as regular decorations. Height tolerance: {tolerance:F2}.");
        return (terrainLayers.ToArray(), regularLayers.ToArray());
    }

    public static (SceneStaticMeshInstance[] TerrainInstances, SceneStaticMeshInstance[] RegularInstances) SplitTreeInstancesByTerrainSurface(
        IReadOnlyList<SceneStaticMeshInstance> treeInstances,
        IReadOnlyDictionary<string, Mesh> meshCache,
        GameObject staticMeshRoot,
        TerrainImportData terrainImport,
        Action<string> log)
    {
        if (treeInstances == null || treeInstances.Count == 0)
        {
            return (Array.Empty<SceneStaticMeshInstance>(), Array.Empty<SceneStaticMeshInstance>());
        }

        var terrain = FindTerrain(staticMeshRoot);
        if (terrain == null || terrain.terrainData == null)
        {
            if (!TryBuildTerrainSurface(terrainImport, out var terrainSurface))
            {
                log?.Invoke("[Terrain/Vegetation] Terrain surface not found while classifying trees. All tree instances will be kept as regular static meshes.");
                return (Array.Empty<SceneStaticMeshInstance>(), treeInstances.Where(instance => instance != null).ToArray());
            }

            return SplitTreeInstancesByRawTerrainSurface(treeInstances, meshCache, terrainSurface, log);
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

            if (IsTreeInstanceOnTerrainSurface(instance, terrain, meshCache, tolerance))
            {
                terrainAccepted.Add(instance);
            }
            else
            {
                regularFallback.Add(instance);
            }
        }

        log?.Invoke($"[Terrain/Vegetation] Tree terrain classification: {terrainAccepted.Count} snapped to terrain, {regularFallback.Count} kept as regular meshes. Height tolerance: {tolerance:F2}. Classification uses mesh base point against terrain surface.");
        return (terrainAccepted.ToArray(), regularFallback.ToArray());
    }

    private static (SceneTerrainDecorationLayer[] TerrainLayers, SceneTerrainDecorationLayer[] RegularLayers) SplitTerrainDecorationLayersByRawTerrainSurface(
        IReadOnlyList<SceneTerrainDecorationLayer> terrainDecorationLayers,
        TerrainSurfaceDescriptor terrainSurface,
        string clientPath,
        Action<string> log)
    {
        var textureManager = new BspTextureManager(clientPath);
        var heightTextureRequests = terrainDecorationLayers
            .Where(layer => layer?.HeightMapResource != null)
            .Select(layer => new SceneTextureRequest(layer.HeightMapResource.PackageName, layer.HeightMapResource.ObjectName))
            .Distinct()
            .ToArray();
        var resolvedHeightTextures = ResolveTerrainDecorationHeightTextures(textureManager, heightTextureRequests);
        var terrainLayers = new List<SceneTerrainDecorationLayer>(terrainDecorationLayers.Count);
        var regularLayers = new List<SceneTerrainDecorationLayer>();

        foreach (var layer in terrainDecorationLayers)
        {
            if (layer == null)
            {
                continue;
            }

            if (IsTerrainDecorationLayerOnTerrainSurface(layer, terrainSurface, resolvedHeightTextures, terrainSurface.HeightTolerance))
            {
                terrainLayers.Add(layer);
            }
            else
            {
                regularLayers.Add(layer);
            }
        }

        log?.Invoke($"[Terrain/Vegetation] Terrain decoration classification (raw terrain): {terrainLayers.Count} layers to terrain vegetation, {regularLayers.Count} layers kept as regular decorations. Height tolerance: {terrainSurface.HeightTolerance:F2}.");
        return (terrainLayers.ToArray(), regularLayers.ToArray());
    }

    private static (SceneStaticMeshInstance[] TerrainInstances, SceneStaticMeshInstance[] RegularInstances) SplitTreeInstancesByRawTerrainSurface(
        IReadOnlyList<SceneStaticMeshInstance> treeInstances,
        IReadOnlyDictionary<string, Mesh> meshCache,
        TerrainSurfaceDescriptor terrainSurface,
        Action<string> log)
    {
        var terrainAccepted = new List<SceneStaticMeshInstance>(treeInstances.Count);
        var regularFallback = new List<SceneStaticMeshInstance>();

        foreach (var instance in treeInstances)
        {
            if (instance == null)
            {
                continue;
            }

            if (IsTreeInstanceOnTerrainSurface(instance, terrainSurface, meshCache, terrainSurface.HeightTolerance))
            {
                terrainAccepted.Add(instance);
            }
            else
            {
                regularFallback.Add(instance);
            }
        }

        log?.Invoke($"[Terrain/Vegetation] Tree terrain classification (raw terrain): {terrainAccepted.Count} snapped to terrain, {regularFallback.Count} kept as regular meshes. Height tolerance: {terrainSurface.HeightTolerance:F2}. Classification uses mesh base point against terrain surface.");
        return (terrainAccepted.ToArray(), regularFallback.ToArray());
    }

    public static void PopulateTerrainVegetation(
        IReadOnlyList<SceneStaticMeshInstance> grassInstances,
        IReadOnlyList<SceneStaticMeshInstance> treeInstances,
        IReadOnlyList<SceneTerrainDecorationLayer> terrainDecorationLayers,
        GameObject staticMeshRoot,
        IReadOnlyDictionary<string, Mesh> meshCache,
        StaticMeshMaterialCatalog materialCatalog,
        string clientPath,
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

        var detailPreviewDir = $"{outputDir}/Terrain/Details/Previews";
        var detailPrefabDir = L2AssetManager.ManagedTerrainDetailPrefabsRoot;
        var treePrefabDir = L2AssetManager.ManagedTerrainTreePrefabsRoot;
        var treeMaterialDir = L2AssetManager.ManagedTerrainTreeMaterialsRoot;
        L2AssetManager.EnsureFolderExists(detailPreviewDir);
        L2AssetManager.EnsureFolderExists(detailPrefabDir);
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
        var terrainDecorationTreeGroups = (terrainDecorationLayers ?? Array.Empty<SceneTerrainDecorationLayer>())
            .Where(layer => layer != null &&
                            !string.IsNullOrWhiteSpace(layer.MeshReference) &&
                            StaticMeshImportUtility.IsTreeMeshReference(layer.MeshReference) &&
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
            var densityMap = BuildDensityMap(group.ToArray(), terrain, clientPath, detailWidth, detailHeight);
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
            AddTreeInstances(treeInstanceList, prototypeIndex, group.ToArray(), terrain, meshCache);
            createdTreeGroups++;
        }

        foreach (var group in terrainDecorationTreeGroups)
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
            AddTerrainDecorationTreeInstances(treeInstanceList, prototypeIndex, group.ToArray(), terrain);
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

        log?.Invoke($"[Terrain/Vegetation] Grass layers: {createdGrassLayers}/{grassGroups.Length + terrainDecorationGrassGroups.Length} groups from staticGrass={grassCount}, terrainDecoGrass={terrainDecorationCount}. Tree prototypes: {createdTreeGroups}/{treeGroups.Length + terrainDecorationTreeGroups.Length} groups from staticTrees={treeCount}, terrainDecoLayers={terrainDecorationCount}. Skipped grass groups: {skippedGrassGroups}. Skipped tree groups: {skippedTreeGroups}. Tree instances written: {treeInstanceList.Count}.");
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
        return L2AssetManager.BuildClientPackageAssetPath(
            detailPrefabDir,
            meshReference,
            "PF",
            "prefab",
            "TerrainDetailPrefabs",
            TerrainDetailPrefabSuffix);
    }

    private static string BuildTreePrefabPath(string treePrefabDir, string meshReference)
    {
        return L2AssetManager.BuildClientPackageAssetPath(
            treePrefabDir,
            meshReference,
            "PF",
            "prefab",
            "TerrainTreePrefabs",
            TerrainTreePrefabSuffix);
    }

    private static string BuildTreeMaterialPath(string treeMaterialDir, string meshReference, int index, string sourceName)
    {
        return L2AssetManager.BuildClientPackageAssetPath(
            treeMaterialDir,
            meshReference,
            $"MAT{index:D2}",
            "mat",
            "TerrainTreeMaterials",
            sourceName);
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

            L2AssetManager.EnsureParentFolderExists(prefabPath);
            var prefab = PrefabUtility.SaveAsPrefabAsset(prefabRoot, prefabPath);
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
        var assetPath = BuildTreeMaterialPath(treeMaterialDir, meshReference, index, sourceName);
        var assetName = Path.GetFileNameWithoutExtension(assetPath);
        var material = source != null
            ? new Material(source)
            : new Material(ResolveCompatibleTreeShader(source));

        ConfigureTreeMaterial(material, source, index, assetName);
        material = UnityAssetDatabaseUtility.CreateOrReplaceAsset(material, assetPath);
        EditorUtility.SetDirty(material);
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
            minWidth = 1.15f * GrassSizeBaseMultiplier,
            maxWidth = 1.15f * GrassSizeBaseMultiplier * GrassSizeRandomRangeMultiplier,
            minHeight = 0.95f * GrassSizeBaseMultiplier,
            maxHeight = 0.95f * GrassSizeBaseMultiplier * GrassSizeRandomRangeMultiplier,
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
        Terrain terrain,
        IReadOnlyDictionary<string, Mesh> meshCache)
    {
        var terrainData = terrain.terrainData;
        var terrainPosition = terrain.transform.position;
        var terrainSize = terrainData.size;

        foreach (var instance in instances)
        {
            if (!TryGetTreeBaseWorldPosition(instance, meshCache, out var baseWorldPosition))
            {
                continue;
            }

            var localPosition = baseWorldPosition - terrainPosition;
            if (terrainSize.x <= 0f || terrainSize.y <= 0f || terrainSize.z <= 0f)
            {
                continue;
            }

            var normalizedX = localPosition.x / terrainSize.x;
            var normalizedZ = localPosition.z / terrainSize.z;
            if (normalizedX < 0f || normalizedX > 1f || normalizedZ < 0f || normalizedZ > 1f)
            {
                continue;
            }

            var terrainSurfaceY = terrainPosition.y + terrainData.GetInterpolatedHeight(normalizedX, normalizedZ);
            var normalizedY = (terrainSurfaceY - terrainPosition.y) / terrainSize.y;

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

    private static void AddTerrainDecorationTreeInstances(
        List<TreeInstance> treeInstances,
        int prototypeIndex,
        IReadOnlyList<SceneTerrainDecorationLayer> layers,
        Terrain terrain)
    {
        var terrainData = terrain.terrainData;
        var rngSeed = 0;

        foreach (var layer in layers)
        {
            var densityTexture = layer?.DensityMapTexture;
            if (densityTexture == null || densityTexture.Width <= 0 || densityTexture.Height <= 0)
            {
                continue;
            }

            var stepX = Math.Max(1, (int)Math.Ceiling(densityTexture.Width / (double)MaxTerrainDecorationSampleResolution));
            var stepY = Math.Max(1, (int)Math.Ceiling(densityTexture.Height / (double)MaxTerrainDecorationSampleResolution));
            var rng = new System.Random(layer.Seed ^ (layer.TerrainExportIndex * 397) ^ (layer.LayerIndex * 7919) ^ rngSeed);
            var densityScale = layer.DensityMultiplier?.Max ?? 100f;
            var maxPerQuad = Math.Max(1, layer.MaxPerQuad);

            for (var y = 0; y < densityTexture.Height; y += stepY)
            {
                for (var x = 0; x < densityTexture.Width; x += stepX)
                {
                    var density = SampleGray01(densityTexture, x, y);
                    if (density <= 0.05f)
                    {
                        continue;
                    }

                    var desired = density * maxPerQuad * (densityScale / 100f);
                    var count = Math.Clamp((int)MathF.Round(desired), 0, 3);
                    if (count == 0 && density > 0.35f)
                    {
                        count = 1;
                    }

                    for (var i = 0; i < count; i++)
                    {
                        var jitterU = (x + ((float)rng.NextDouble() * stepX)) / Math.Max(1, densityTexture.Width - 1);
                        var jitterV = (y + ((float)rng.NextDouble() * stepY)) / Math.Max(1, densityTexture.Height - 1);
                        jitterU = Math.Clamp(jitterU, 0f, 1f);
                        jitterV = Math.Clamp(jitterV, 0f, 1f);

                        var position = new Vector3(jitterU, 0f, jitterV);
                        position.y = terrainData.GetInterpolatedHeight(position.x, position.z) / Mathf.Max(0.0001f, terrainData.size.y);

                        var scale = ResolveTerrainDecorationScale(layer, jitterU, jitterV, rng);
                        var yawDegrees = layer.RandomYaw ? (float)(rng.NextDouble() * 360.0) : 0f;

                        var treeInstance = new TreeInstance
                        {
                            prototypeIndex = prototypeIndex,
                            position = new Vector3(
                                Mathf.Clamp01(position.x),
                                Mathf.Clamp01(position.y),
                                Mathf.Clamp01(position.z)),
                            widthScale = Mathf.Max(0.01f, scale.X),
                            heightScale = Mathf.Max(0.01f, scale.Z),
                            rotation = -yawDegrees * Mathf.Deg2Rad,
                            color = Color.white,
                            lightmapColor = Color.white
                        };
                        treeInstances.Add(treeInstance);
                    }
                }
            }

            rngSeed++;
        }
    }

    private static bool IsTreeInstanceOnTerrainSurface(
        SceneStaticMeshInstance instance,
        Terrain terrain,
        IReadOnlyDictionary<string, Mesh> meshCache,
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

        if (!TryGetTreeBaseWorldPosition(instance, meshCache, out var baseWorldPosition))
        {
            return false;
        }

        var localPosition = baseWorldPosition - terrainPosition;
        var normalizedX = localPosition.x / terrainSize.x;
        var normalizedZ = localPosition.z / terrainSize.z;
        if (normalizedX < 0f || normalizedX > 1f || normalizedZ < 0f || normalizedZ > 1f)
        {
            return false;
        }

        var terrainSurfaceY = terrainPosition.y + terrainData.GetInterpolatedHeight(normalizedX, normalizedZ);
        return Mathf.Abs(baseWorldPosition.y - terrainSurfaceY) <= heightTolerance;
    }

    private static bool IsTreeInstanceOnTerrainSurface(
        SceneStaticMeshInstance instance,
        TerrainSurfaceDescriptor terrainSurface,
        IReadOnlyDictionary<string, Mesh> meshCache,
        float heightTolerance)
    {
        if (!TryGetTreeBaseWorldPosition(instance, meshCache, out var baseWorldPosition))
        {
            return false;
        }

        return TrySampleTerrainSurfaceY(baseWorldPosition, terrainSurface, out var terrainSurfaceY) &&
               Mathf.Abs(baseWorldPosition.y - terrainSurfaceY) <= heightTolerance;
    }

    private static bool IsTerrainDecorationLayerOnTerrainSurface(
        SceneTerrainDecorationLayer layer,
        Terrain terrain,
        IReadOnlyDictionary<string, TextureData> resolvedHeightTextures,
        float heightTolerance)
    {
        if (layer == null || terrain == null || terrain.terrainData == null)
        {
            return false;
        }

        if (!TryGetTerrainDecorationHeightTexture(layer, resolvedHeightTextures, out var heightTexture))
        {
            return false;
        }

        var densityTexture = layer.DensityMapTexture;
        if (densityTexture == null || densityTexture.Width <= 0 || densityTexture.Height <= 0)
        {
            return false;
        }

        var heightField = BuildHeightField(heightTexture);
        var stepX = Math.Max(1, (int)Math.Ceiling(densityTexture.Width / (double)MaxTerrainDecorationSampleResolution));
        var stepY = Math.Max(1, (int)Math.Ceiling(densityTexture.Height / (double)MaxTerrainDecorationSampleResolution));
        var matched = 0;
        var total = 0;

        for (var y = 0; y < densityTexture.Height; y += stepY)
        {
            for (var x = 0; x < densityTexture.Width; x += stepX)
            {
                var density = SampleGray01(densityTexture, x, y);
                if (density <= 0.05f)
                {
                    continue;
                }

                var u = x / (float)Math.Max(1, densityTexture.Width - 1);
                var v = y / (float)Math.Max(1, densityTexture.Height - 1);
                var worldPosition = SampleTerrainDecorationWorldPosition(heightTexture, heightField, layer.TerrainScale, layer.TerrainLocation, u, v)
                    .TransformFromUnrealToUnityWithScale();
                if (TrySampleTerrainSurfaceY(worldPosition, terrain, out var terrainSurfaceY) &&
                    Mathf.Abs(worldPosition.y - terrainSurfaceY) <= heightTolerance)
                {
                    matched++;
                }

                total++;
            }
        }

        return total > 0 && ((matched / (float)total) >= TerrainDecorationTerrainAcceptanceThreshold);
    }

    private static bool IsTerrainDecorationLayerOnTerrainSurface(
        SceneTerrainDecorationLayer layer,
        TerrainSurfaceDescriptor terrainSurface,
        IReadOnlyDictionary<string, TextureData> resolvedHeightTextures,
        float heightTolerance)
    {
        if (layer == null)
        {
            return false;
        }

        if (!TryGetTerrainDecorationHeightTexture(layer, resolvedHeightTextures, out var heightTexture))
        {
            return false;
        }

        var densityTexture = layer.DensityMapTexture;
        if (densityTexture == null || densityTexture.Width <= 0 || densityTexture.Height <= 0)
        {
            return false;
        }

        var heightField = BuildHeightField(heightTexture);
        var stepX = Math.Max(1, (int)Math.Ceiling(densityTexture.Width / (double)MaxTerrainDecorationSampleResolution));
        var stepY = Math.Max(1, (int)Math.Ceiling(densityTexture.Height / (double)MaxTerrainDecorationSampleResolution));
        var matched = 0;
        var total = 0;

        for (var y = 0; y < densityTexture.Height; y += stepY)
        {
            for (var x = 0; x < densityTexture.Width; x += stepX)
            {
                var density = SampleGray01(densityTexture, x, y);
                if (density <= 0.05f)
                {
                    continue;
                }

                var u = x / (float)Math.Max(1, densityTexture.Width - 1);
                var v = y / (float)Math.Max(1, densityTexture.Height - 1);
                var worldPosition = SampleTerrainDecorationWorldPosition(heightTexture, heightField, layer.TerrainScale, layer.TerrainLocation, u, v)
                    .TransformFromUnrealToUnityWithScale();
                if (TrySampleTerrainSurfaceY(worldPosition, terrainSurface, out var terrainSurfaceY) &&
                    Mathf.Abs(worldPosition.y - terrainSurfaceY) <= heightTolerance)
                {
                    matched++;
                }

                total++;
            }
        }

        return total > 0 && ((matched / (float)total) >= TerrainDecorationTerrainAcceptanceThreshold);
    }

    private static bool TryGetTreeBaseWorldPosition(
        SceneStaticMeshInstance instance,
        IReadOnlyDictionary<string, Mesh> meshCache,
        out Vector3 baseWorldPosition)
    {
        baseWorldPosition = default;

        if (instance == null ||
            meshCache == null ||
            string.IsNullOrWhiteSpace(instance.MeshReference) ||
            !meshCache.TryGetValue(instance.MeshReference, out var mesh) ||
            mesh == null)
        {
            return false;
        }

        var pivotWorldPosition = instance.WorldLocation.TransformFromUnrealToUnityWithScale();
        var verticalScale = instance.Scale.Z;
        var baseOffsetY = mesh.bounds.min.y * verticalScale;
        baseWorldPosition = pivotWorldPosition + new Vector3(0f, baseOffsetY, 0f);
        return true;
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

    private static float ComputeTreeTerrainHeightTolerance(TerrainImportData terrainImport)
    {
        if (terrainImport == null)
        {
            return MinimumTreeTerrainHeightTolerance;
        }

        var scaleX = terrainImport.SampleSpacingX * UnrealToUnityScale;
        var scaleZ = terrainImport.SampleSpacingY * UnrealToUnityScale;
        var adaptiveTolerance = Mathf.Max(scaleX, scaleZ) * 0.5f;
        return Mathf.Max(MinimumTreeTerrainHeightTolerance, adaptiveTolerance);
    }

    private static bool TryBuildTerrainSurface(TerrainImportData terrainImport, out TerrainSurfaceDescriptor terrainSurface)
    {
        terrainSurface = default;
        if (terrainImport == null ||
            terrainImport.HeightSamples == null ||
            terrainImport.HeightWidth <= 1 ||
            terrainImport.HeightHeight <= 1 ||
            terrainImport.HeightSamples.Length < (terrainImport.HeightWidth * terrainImport.HeightHeight))
        {
            return false;
        }

        var raw = terrainImport.WorldMinCorner ?? System.Numerics.Vector3.Zero;
        var position = new Vector3(
            raw.X * UnrealToUnityScale,
            (raw.Z - (terrainImport.HeightValueScale * 0.5f)) * UnrealToUnityScale,
            raw.Y * UnrealToUnityScale);
        var size = new Vector3(
            terrainImport.SampleSpacingX * (terrainImport.HeightWidth - 1) * UnrealToUnityScale,
            terrainImport.HeightValueScale * UnrealToUnityScale,
            terrainImport.SampleSpacingY * (terrainImport.HeightHeight - 1) * UnrealToUnityScale);
        if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
        {
            return false;
        }

        terrainSurface = new TerrainSurfaceDescriptor(
            position,
            size,
            terrainImport.HeightWidth,
            terrainImport.HeightHeight,
            terrainImport.HeightSamples,
            ComputeTreeTerrainHeightTolerance(terrainImport));
        return true;
    }

    private static bool TryGetTerrainDecorationHeightTexture(
        SceneTerrainDecorationLayer layer,
        IReadOnlyDictionary<string, TextureData> resolvedHeightTextures,
        out TextureData heightTexture)
    {
        heightTexture = null;
        if (layer?.HeightMapResource == null || resolvedHeightTextures == null)
        {
            return false;
        }

        var lookup = $"{layer.HeightMapResource.PackageName}.{layer.HeightMapResource.ObjectName}";
        if (!resolvedHeightTextures.TryGetValue(lookup, out var resolved) || resolved == null)
        {
            return false;
        }

        heightTexture = resolved;
        return true;
    }

    private static Dictionary<string, TextureData> ResolveTerrainDecorationHeightTextures(
        BspTextureManager textureManager,
        IReadOnlyList<SceneTextureRequest> requests)
    {
        var resolvedTextures = new Dictionary<string, TextureData>(StringComparer.OrdinalIgnoreCase);
        if (textureManager == null || requests == null || requests.Count == 0)
        {
            return resolvedTextures;
        }

        var resolved = textureManager.ResolveMany(requests.ToArray());
        foreach (var pair in resolved)
        {
            if (pair.Value?.Texture != null)
            {
                resolvedTextures[pair.Key] = pair.Value.Texture;
            }
        }

        return resolvedTextures;
    }

    private static System.Numerics.Vector3 ResolveTerrainDecorationScale(SceneTerrainDecorationLayer layer, float u, float v, System.Random rng)
    {
        var amount = layer.ScaleMapTexture == null ? (float)rng.NextDouble() : SampleGray01(layer.ScaleMapTexture, u, v);
        if (layer.ScaleMultiplier == null)
        {
            return System.Numerics.Vector3.One;
        }

        return new System.Numerics.Vector3(
            Lerp(layer.ScaleMultiplier.X.Min, layer.ScaleMultiplier.X.Max, amount),
            Lerp(layer.ScaleMultiplier.Y.Min, layer.ScaleMultiplier.Y.Max, amount),
            Lerp(layer.ScaleMultiplier.Z.Min, layer.ScaleMultiplier.Z.Max, amount));
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

    private static void PaintSoftPatch(float[,] weights, int centerX, int centerY, int radiusX, int radiusY, int seed, float densityMultiplier = 1f)
    {
        var height = weights.GetLength(0);
        var width = weights.GetLength(1);
        var patchScale = BasePatchOpacity * GrassDensityMultiplier * densityMultiplier * JitterAroundOne(Hash01(seed ^ 17));

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
        var radiusX = Mathf.Clamp(Mathf.CeilToInt((footprintX / Mathf.Max(0.0001f, cellSizeX)) * 2.6f * GrassPatchRadiusMultiplier), 4, 40);
        var radiusY = Mathf.Clamp(Mathf.CeilToInt((footprintZ / Mathf.Max(0.0001f, cellSizeZ)) * 2.6f * GrassPatchRadiusMultiplier), 4, 40);
        radiusX = Mathf.RoundToInt(radiusX * JitterAroundOne(Hash01(StableHash(instance.MeshReference) ^ (int)instance.WorldLocation.X)));
        radiusY = Mathf.RoundToInt(radiusY * JitterAroundOne(Hash01(StableHash(instance.MeshReference) ^ (int)instance.WorldLocation.Y)));
        return new Vector2Int(Mathf.Clamp(radiusX, 4, 40), Mathf.Clamp(radiusY, 4, 40));
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
        Terrain terrain,
        string clientPath,
        int detailWidth,
        int detailHeight)
    {
        var weights = new float[detailHeight, detailWidth];
        var textureManager = new BspTextureManager(clientPath);
        var heightTextureRequests = layers
            .Where(layer => layer?.HeightMapResource != null)
            .Select(layer => new SceneTextureRequest(layer.HeightMapResource.PackageName, layer.HeightMapResource.ObjectName))
            .Distinct()
            .ToArray();
        var resolvedHeightTextures = ResolveTerrainDecorationHeightTextures(textureManager, heightTextureRequests);

        foreach (var layer in layers)
        {
            var densityTexture = layer.DensityMapTexture;
            if (densityTexture == null || densityTexture.Width <= 0 || densityTexture.Height <= 0)
            {
                continue;
            }

            if (!TryGetTerrainDecorationHeightTexture(layer, resolvedHeightTextures, out var heightTexture))
            {
                continue;
            }

            var heightField = BuildHeightField(heightTexture);
            PaintTerrainDecorationGrassWeights(weights, layer, densityTexture, heightTexture, heightField, terrain);
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

    private static void PaintTerrainDecorationGrassWeights(
        float[,] weights,
        SceneTerrainDecorationLayer layer,
        TextureData densityTexture,
        TextureData heightTexture,
        IReadOnlyList<float> heightField,
        Terrain terrain)
    {
        var width = weights.GetLength(1);
        var height = weights.GetLength(0);
        var stepX = Math.Max(1, (int)Math.Ceiling(densityTexture.Width / (double)MaxTerrainDecorationSampleResolution));
        var stepY = Math.Max(1, (int)Math.Ceiling(densityTexture.Height / (double)MaxTerrainDecorationSampleResolution));
        var seed = StableHash(layer.MeshReference) ^ layer.Seed ^ (layer.LayerIndex * 7919);

        for (var y = 0; y < densityTexture.Height; y += stepY)
        {
            for (var x = 0; x < densityTexture.Width; x += stepX)
            {
                var density = SampleGray01(densityTexture, x, y);
                if (density <= 0.05f)
                {
                    continue;
                }

                var u = x / (float)Math.Max(1, densityTexture.Width - 1);
                var v = y / (float)Math.Max(1, densityTexture.Height - 1);
                var worldPosition = SampleTerrainDecorationWorldPosition(heightTexture, heightField, layer.TerrainScale, layer.TerrainLocation, u, v)
                    .TransformFromUnrealToUnityWithScale();
                if (!TryProjectWorldPositionToTerrain(worldPosition, terrain, out var normalizedX, out var normalizedZ))
                {
                    continue;
                }

                var centerX = Mathf.Clamp(Mathf.RoundToInt(normalizedX * (width - 1)), 0, width - 1);
                var centerY = Mathf.Clamp(Mathf.RoundToInt(normalizedZ * (height - 1)), 0, height - 1);
                var radiusX = Mathf.Clamp(Mathf.RoundToInt(stepX * 0.5f * GrassPatchRadiusMultiplier), 4, 40);
                var radiusY = Mathf.Clamp(Mathf.RoundToInt(stepY * 0.5f * GrassPatchRadiusMultiplier), 4, 40);
                PaintSoftPatch(weights, centerX, centerY, radiusX, radiusY, seed ^ x ^ (y << 8), density);
            }
        }
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

    private static float SampleGray01(TextureData texture, int x, int y)
    {
        var clampedX = Math.Clamp(x, 0, texture.Width - 1);
        var clampedY = Math.Clamp(y, 0, texture.Height - 1);
        var src = (clampedY * texture.Width + clampedX) * 4;
        return ((0.299f * texture.RgbaBytes[src + 0]) +
                (0.587f * texture.RgbaBytes[src + 1]) +
                (0.114f * texture.RgbaBytes[src + 2])) / 255f;
    }

    private static float Lerp(float a, float b, float t)
    {
        return a + ((b - a) * t);
    }

    private static float JitterAroundOne(float t)
    {
        return Mathf.Lerp(1f - GrassRandomJitter, 1f + GrassRandomJitter, t);
    }

    private static List<float> BuildHeightField(TextureData texture)
    {
        var pixels = new List<(byte R, byte G, byte B, byte A)>(texture.Width * texture.Height);
        for (var i = 0; i < texture.RgbaBytes.Length; i += 4)
        {
            pixels.Add((texture.RgbaBytes[i], texture.RgbaBytes[i + 1], texture.RgbaBytes[i + 2], texture.RgbaBytes[i + 3]));
        }

        var channels = new[] { "a", "r", "g", "b", "luma" };
        var channelName = "luma";
        var bestScore = float.MaxValue;
        foreach (var channel in channels)
        {
            var (rough, lo, hi) = AnalyzeHeightChannel(pixels, texture.Width, texture.Height, channel);
            var range = hi - lo;
            if (range < 0.03f)
            {
                continue;
            }

            var score = rough - (0.04f * range);
            if (score < bestScore)
            {
                bestScore = score;
                channelName = channel;
            }
        }

        return pixels.Select(pixel => ChannelValue(pixel, channelName)).ToList();
    }

    private static (float Rough, float Lo, float Hi) AnalyzeHeightChannel(
        List<(byte R, byte G, byte B, byte A)> pixels,
        int width,
        int height,
        string mode)
    {
        var lo = 1f;
        var hi = 0f;
        var rough = 0f;
        var count = 0;
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            for (var x = 0; x < width; x++)
            {
                var value = ChannelValue(pixels[row + x], mode);
                lo = MathF.Min(lo, value);
                hi = MathF.Max(hi, value);
                if (x + 1 < width)
                {
                    rough += MathF.Abs(ChannelValue(pixels[row + x + 1], mode) - value);
                    count++;
                }

                if (y + 1 < height)
                {
                    rough += MathF.Abs(ChannelValue(pixels[row + x + width], mode) - value);
                    count++;
                }
            }
        }

        return (rough / Math.Max(1, count), lo, hi);
    }

    private static float ChannelValue((byte R, byte G, byte B, byte A) pixel, string mode)
    {
        return mode switch
        {
            "r" => pixel.R / 255f,
            "g" => pixel.G / 255f,
            "b" => pixel.B / 255f,
            "a" => pixel.A / 255f,
            _ => ((0.299f * pixel.R) + (0.587f * pixel.G) + (0.114f * pixel.B)) / 255f
        };
    }

    private static System.Numerics.Vector3 SampleTerrainDecorationWorldPosition(
        TextureData heightTexture,
        IReadOnlyList<float> heightField,
        System.Numerics.Vector3? terrainScale,
        System.Numerics.Vector3 terrainLocation,
        float u,
        float v)
    {
        var sampleX = u * Math.Max(1, heightTexture.Width - 1);
        var sampleY = v * Math.Max(1, heightTexture.Height - 1);
        var scaleX = terrainScale?.X ?? 4f;
        var scaleHeight = terrainScale is null ? 240f : terrainScale.Value.Y * 256f;
        var scaleY = terrainScale?.Z ?? 4f;
        var cx = 0.5f * (heightTexture.Width - 1);
        var cy = 0.5f * (heightTexture.Height - 1);
        var heightValue = SampleHeight(heightField, heightTexture.Width, heightTexture.Height, sampleX, sampleY);

        return new System.Numerics.Vector3(
            ((sampleX - cx) * scaleX) + terrainLocation.X,
            ((sampleY - cy) * scaleY) + terrainLocation.Y,
            ((heightValue - 0.5f) * scaleHeight) + terrainLocation.Z);
    }

    private static float SampleHeight(IReadOnlyList<float> heightField, int width, int height, float sampleX, float sampleY)
    {
        var x0 = Math.Clamp((int)MathF.Floor(sampleX), 0, width - 1);
        var y0 = Math.Clamp((int)MathF.Floor(sampleY), 0, height - 1);
        var x1 = Math.Clamp(x0 + 1, 0, width - 1);
        var y1 = Math.Clamp(y0 + 1, 0, height - 1);
        var tx = sampleX - x0;
        var ty = sampleY - y0;

        var h00 = heightField[(y0 * width) + x0];
        var h10 = heightField[(y0 * width) + x1];
        var h01 = heightField[(y1 * width) + x0];
        var h11 = heightField[(y1 * width) + x1];
        var hx0 = Lerp(h00, h10, tx);
        var hx1 = Lerp(h01, h11, tx);
        return Lerp(hx0, hx1, ty);
    }

    private static bool TrySampleTerrainSurfaceY(Vector3 worldPosition, Terrain terrain, out float terrainSurfaceY)
    {
        terrainSurfaceY = 0f;
        if (!TryProjectWorldPositionToTerrain(worldPosition, terrain, out var normalizedX, out var normalizedZ))
        {
            return false;
        }

        terrainSurfaceY = terrain.transform.position.y + terrain.terrainData.GetInterpolatedHeight(normalizedX, normalizedZ);
        return true;
    }

    private static bool TrySampleTerrainSurfaceY(Vector3 worldPosition, TerrainSurfaceDescriptor terrainSurface, out float terrainSurfaceY)
    {
        terrainSurfaceY = 0f;
        if (!TryProjectWorldPositionToTerrain(worldPosition, terrainSurface.Position, terrainSurface.Size, out var normalizedX, out var normalizedZ))
        {
            return false;
        }

        var sampleX = normalizedX * (terrainSurface.Width - 1);
        var sampleY = normalizedZ * (terrainSurface.Height - 1);
        var sample = SampleHeight(terrainSurface.HeightSamples, terrainSurface.Width, terrainSurface.Height, sampleX, sampleY);
        terrainSurfaceY = terrainSurface.Position.y + (sample * terrainSurface.Size.y);
        return true;
    }

    private static bool TryProjectWorldPositionToTerrain(Vector3 worldPosition, Terrain terrain, out float normalizedX, out float normalizedZ)
    {
        normalizedX = 0f;
        normalizedZ = 0f;
        if (terrain == null || terrain.terrainData == null)
        {
            return false;
        }

        var terrainPosition = terrain.transform.position;
        var terrainSize = terrain.terrainData.size;
        if (terrainSize.x <= 0f || terrainSize.z <= 0f)
        {
            return false;
        }

        var localPosition = worldPosition - terrainPosition;
        normalizedX = localPosition.x / terrainSize.x;
        normalizedZ = localPosition.z / terrainSize.z;
        return normalizedX >= 0f && normalizedX <= 1f && normalizedZ >= 0f && normalizedZ <= 1f;
    }

    private static bool TryProjectWorldPositionToTerrain(Vector3 worldPosition, Vector3 terrainPosition, Vector3 terrainSize, out float normalizedX, out float normalizedZ)
    {
        normalizedX = 0f;
        normalizedZ = 0f;
        if (terrainSize.x <= 0f || terrainSize.z <= 0f)
        {
            return false;
        }

        var localPosition = worldPosition - terrainPosition;
        normalizedX = localPosition.x / terrainSize.x;
        normalizedZ = localPosition.z / terrainSize.z;
        return normalizedX >= 0f && normalizedX <= 1f && normalizedZ >= 0f && normalizedZ <= 1f;
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
