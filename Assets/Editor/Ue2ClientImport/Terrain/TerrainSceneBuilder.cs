using L2Viewer.SceneDomain.Models;
using UnityEditor;
using UnityEngine;

internal static class TerrainSceneBuilder
{
    private const float HeightmapPixelError = 1f;

    public static GameObject CreateTerrainObject(string objectName, TerrainImportData terrainImport, TerrainData terrainData, GameObject mapRoot)
    {
        var terrainObject = Terrain.CreateTerrainGameObject(terrainData);
        terrainObject.name = $"{objectName}_Terrain";
        terrainObject.isStatic = true;
        terrainObject.transform.SetParent(mapRoot.transform, false);
        terrainObject.transform.position = TerrainAssetBuilder.ConvertTerrainPosition(terrainImport, terrainData);

        var terrain = terrainObject.GetComponent<Terrain>();
        terrain.drawInstanced = true;
        terrain.heightmapPixelError = HeightmapPixelError;
        terrain.Flush();

        EditorUtility.SetDirty(terrain);
        EditorUtility.SetDirty(terrainData);
        return terrainObject;
    }
}

