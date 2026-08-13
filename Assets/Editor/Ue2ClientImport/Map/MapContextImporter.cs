using System;
using System.Threading.Tasks;
using L2Viewer.SceneDomain.Services;
using UnityEngine;

internal static class MapContextImporter
{
    public static void ImportAsync(MapImportRequest request, Ue2MapSource source, Action<string> log, bool finalizeScene = true)
    {
        var mapContextBuilder = new SceneMapContextBuilder();
        var mapContext = mapContextBuilder.Build(source.UnrFile, source.ClientPath);

        if (mapContext == null)
        {
            log("Map context was not built.");
            return;
        }

        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        MapImportAssetPreparation.EnsureMapOutputFolderExists(request.OutputDir);

        var contextRootName = $"{request.ObjectName}_Context";
        if (UnitySceneObjectUtility.ObjectExists(contextRootName))
        {
            log($"[Context] Skipping import because '{contextRootName}' already exists.");
            return;
        }

        var contextAsset = L2MapContextAssetBuilder.BuildContextAsset(mapContext, request.OutputDir);
        var contextRoot = new GameObject(contextRootName);
        contextRoot.transform.SetParent(mapRoot.transform, false);
        AlignContextRootToTerrainQuadrant(contextRoot.transform, mapRoot.transform, contextAsset, log);

        var contextVolume = contextRoot.AddComponent<L2MapContextVolume>();
        contextVolume.Context = contextAsset;
        contextVolume.RefreshContext();

        if (finalizeScene)
        {
            MapImportFinalizer.Complete(mapRoot, log);
        }
    }

    private static void AlignContextRootToTerrainQuadrant(
        Transform contextRoot,
        Transform mapRoot,
        L2MapAtmosphereContextAsset contextAsset,
        Action<string> log)
    {
        if (contextRoot == null || mapRoot == null || contextAsset == null)
        {
            return;
        }

        var terrain = FindMapTerrain(mapRoot);
        if (terrain == null || terrain.terrainData == null)
        {
            return;
        }

        var contextCenter = (contextAsset.WorldBoundsMinUnity + contextAsset.WorldBoundsMaxUnity) * 0.5f;
        var terrainSize = terrain.terrainData.size;
        var terrainWorldCenter = terrain.transform.position + new Vector3(terrainSize.x * 0.5f, 0f, terrainSize.z * 0.5f);
        var worldPosition = contextRoot.position;
        worldPosition.x = terrainWorldCenter.x - contextCenter.x;
        worldPosition.z = terrainWorldCenter.z - contextCenter.z;
        contextRoot.position = worldPosition;

        log?.Invoke($"[Context] Aligned context volume X/Z to terrain quadrant center: {contextRoot.position}.");
    }

    private static Terrain FindMapTerrain(Transform mapRoot)
    {
        var expectedTerrainName = $"{mapRoot.name}_Terrain";
        var terrains = mapRoot.GetComponentsInChildren<Terrain>(true);
        for (var i = 0; i < terrains.Length; i++)
        {
            var terrain = terrains[i];
            if (terrain != null && terrain.name == expectedTerrainName)
            {
                return terrain;
            }
        }

        return terrains.Length > 0 ? terrains[0] : null;
    }
}
