using System.IO;

internal readonly struct MapImportRequest
{
    public MapImportRequest(
        string mapRelativePath,
        string mapKey,
        string objectName,
        string outputDir,
        bool importTrees = true,
        bool importNonTrees = true,
        bool reuseExistingMaterialTextureAssets = true,
        int terrainHeightSmooth = 0)
    {
        MapRelativePath = mapRelativePath;
        MapKey = mapKey;
        ObjectName = objectName;
        OutputDir = outputDir;
        ImportTrees = importTrees;
        ImportNonTrees = importNonTrees;
        ReuseExistingMaterialTextureAssets = reuseExistingMaterialTextureAssets;
        TerrainHeightSmooth = 0;
    }

    public string MapRelativePath { get; }
    public string MapKey { get; }
    public string ObjectName { get; }
    public string OutputDir { get; }
    public bool ImportTrees { get; }
    public bool ImportNonTrees { get; }
    public bool ReuseExistingMaterialTextureAssets { get; }
    public int TerrainHeightSmooth { get; }

    public static MapImportRequest FromMapRelativePath(
        string mapRelativePath,
        bool importTrees = true,
        bool importNonTrees = true,
        bool reuseExistingMaterialTextureAssets = true,
        int terrainHeightSmooth = 0)
    {
        var fileName = Path.GetFileNameWithoutExtension(mapRelativePath);
        var mapKey = AssetNameUtility.SanitizeName(fileName);
        return new MapImportRequest(
            mapRelativePath,
            mapKey,
            $"L2Terrain_{mapKey}",
            $"{MapImportPaths.OutputRoot}/{mapKey}",
            importTrees,
            importNonTrees,
            reuseExistingMaterialTextureAssets,
            terrainHeightSmooth);
    }
}

internal static class MapImportPaths
{
    public const string OutputRoot = "Assets/L2Imported";
}
