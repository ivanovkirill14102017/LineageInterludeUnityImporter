using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using UnityEngine;

internal static class L2StaticMeshAssetBuilder
{
    public static void BuildStaticMeshes(
        SceneInstancedMeshResult instancedResult,
        GameObject parent,
        string clientPath,
        string mapKey,
        string outputDir,
        Action<string> log,
        bool importTrees = true,
        bool importNonTrees = true,
        bool reuseExistingMaterialTextureAssets = true,
        bool placeRegularInstances = true,
        bool placeTerrainDecorations = true,
        bool convertTerrainDecorationsToTerrainVegetation = false,
        bool convertTreeInstancesToTerrainVegetation = true,
        bool placeTreeInstancesAsRegularInstances = false)
    {
        var meshDir = L2AssetManager.SharedStaticMeshesRoot;
        var materialDir = L2AssetManager.SharedMaterialsRoot;
        var textureDir = L2AssetManager.SharedTexturesRoot;
        L2AssetManager.EnsureFolderExists(meshDir);
        L2AssetManager.EnsureFolderExists(materialDir);
        L2AssetManager.EnsureFolderExists(textureDir);

        var meshDefinitions = StaticMeshImportUtility.FilterMeshDefinitions(instancedResult.UniqueMeshes, importTrees, importNonTrees);
        var shader = StaticMeshImportUtility.FindDefaultShader();

        log($"Building {meshDefinitions.Count} unique mesh assets...");

        log("[StaticMesh/Pipeline] START Texture import");
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
        var geometryStopwatch = Stopwatch.StartNew();
        var meshCache = BuildMeshAssets(meshDefinitions, meshDir, mapKey);
        geometryStopwatch.Stop();
        log($"[StaticMesh/Pipeline] DONE Geometry asset build ({geometryStopwatch.Elapsed.TotalSeconds:F2}s)");

        log("[StaticMesh/Pipeline] START Instance placement");
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
        var treeToTerrainCount = convertTreeInstancesToTerrainVegetation ? treeInstances.Length : 0;
        var treeAsRegularCount = convertTreeInstancesToTerrainVegetation ? 0 : treeInstances.Length;
        log($"[StaticMesh/Pipeline] Regular instances: {regularInstances.Length}, grass-to-terrain instances: {grassInstances.Length}, tree-to-terrain instances: {treeToTerrainCount}, tree-as-regular instances: {treeAsRegularCount}.");

        if (placeRegularInstances)
        {
            StaticMeshInstancePlacer.PlaceInstances(regularInstances, parent, meshCache, materialCatalog, log);
        }

        if (placeTreeInstancesAsRegularInstances && treeInstances.Length > 0)
        {
            StaticMeshInstancePlacer.PlaceInstances(treeInstances, parent, meshCache, materialCatalog, log);
        }

        TerrainGrassDetailBuilder.PopulateTerrainVegetation(
            grassInstances,
            convertTreeInstancesToTerrainVegetation ? treeInstances : Array.Empty<SceneStaticMeshInstance>(),
            convertTerrainDecorationsToTerrainVegetation ? instancedResult.TerrainDecorations : null,
            parent,
            meshCache,
            materialCatalog,
            outputDir,
            mapKey,
            log);

        if (placeTerrainDecorations)
        {
            TerrainDecorationInstancePlacer.PlaceDecorations(instancedResult.TerrainDecorations, parent, meshCache, materialCatalog, clientPath, log);
        }
        placementStopwatch.Stop();
        log($"[StaticMesh/Pipeline] DONE Instance placement ({placementStopwatch.Elapsed.TotalSeconds:F2}s)");
    }

    private static Dictionary<string, Mesh> BuildMeshAssets(
        IReadOnlyDictionary<string, SceneStaticMeshDefinition> meshDefinitions,
        string meshDir,
        string mapKey)
    {
        var meshCache = new Dictionary<string, Mesh>(StringComparer.OrdinalIgnoreCase);

        UnityAssetDatabaseUtility.RunAssetEditingBatch(() =>
        {
            foreach (var pair in meshDefinitions)
            {
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

                var mesh = BuildMeshAsset(definition.RenderGeometry, meshAssetPath);
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

    private static Mesh BuildMeshAsset(SceneTriangleMeshData meshData, string assetPath)
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
        return UnityAssetDatabaseUtility.CreateOrReplaceAsset(unityMesh, assetPath);
    }

    private static Vector3 ConvertPosition(System.Numerics.Vector3 raw)
    {
        return new Vector3(raw.X * 0.01f, raw.Z * 0.01f, raw.Y * 0.01f);
    }
}
