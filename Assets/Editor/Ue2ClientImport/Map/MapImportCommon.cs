using System.IO;

internal readonly struct MapImportRequest
{
    public MapImportRequest(
        string mapRelativePath,
        string mapKey,
        string objectName,
        string outputDir,
        bool reuseExistingMaterialTextureAssets = true)
    {
        MapRelativePath = mapRelativePath;
        MapKey = mapKey;
        ObjectName = objectName;
        OutputDir = outputDir;
        ReuseExistingMaterialTextureAssets = reuseExistingMaterialTextureAssets;
    }

    public string MapRelativePath { get; }
    public string MapKey { get; }
    public string ObjectName { get; }
    public string OutputDir { get; }
    public bool ReuseExistingMaterialTextureAssets { get; }

    public static MapImportRequest FromMapRelativePath(
        string mapRelativePath,
        bool reuseExistingMaterialTextureAssets = true)
    {
        var fileName = Path.GetFileNameWithoutExtension(mapRelativePath);
        var mapKey = fileName;
        return new MapImportRequest(
            mapRelativePath,
            mapKey,
            $"L2Terrain_{mapKey}",
            $"{MapImportPaths.OutputRoot}/{mapKey}",
            reuseExistingMaterialTextureAssets);
    }
}

internal static class MapImportPaths
{
    public const string OutputRoot = "Assets/L2Imported";
}
