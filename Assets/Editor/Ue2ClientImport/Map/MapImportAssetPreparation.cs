internal static class MapImportAssetPreparation
{
    public static void PrepareTerrainOutputFolder(string outputDir)
    {
        EnsureMapOutputFolderExists(outputDir);
        UnityAssetDatabaseUtility.DeleteFolderIfExists($"{outputDir}/Terrain");
    }

    public static void PrepareBspVariantFolder(string outputDir, bool reuseExistingMaterialTextureAssets)
    {
        EnsureMapOutputFolderExists(System.IO.Path.GetDirectoryName(outputDir).Replace('\\', '/'));
        if (!reuseExistingMaterialTextureAssets)
        {
            UnityAssetDatabaseUtility.DeleteFolderIfExists(outputDir);
            return;
        }

        L2AssetManager.EnsureFolderExists(outputDir);
        L2AssetManager.EnsureFolderExists($"{outputDir}/Meshes");
    }

    public static void EnsureMapOutputFolderExists(string outputDir)
    {
        UnityAssetDatabaseUtility.EnsureOutputFolderExists(MapImportPaths.OutputRoot, outputDir);
    }
}
