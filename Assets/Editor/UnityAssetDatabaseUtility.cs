using System.IO;
using UnityEditor;
using UnityEngine;

internal static class UnityAssetDatabaseUtility
{
    public static void RunAssetEditingBatch(System.Action action)
    {
        AssetDatabase.StartAssetEditing();
        try
        {
            action();
        }
        finally
        {
            AssetDatabase.StopAssetEditing();
        }
    }

    public static T CreateOrReplaceAsset<T>(T asset, string assetPath) where T : Object
    {
        var expectedName = Path.GetFileNameWithoutExtension(assetPath);
        if (!string.IsNullOrWhiteSpace(expectedName) && asset != null)
        {
            asset.name = expectedName;
        }

        var existing = AssetDatabase.LoadAssetAtPath<T>(assetPath);
        if (existing != null)
        {
            if (existing.GetType() == asset.GetType())
            {
                EditorUtility.CopySerialized(asset, existing);
                EditorUtility.SetDirty(existing);

                if (!ReferenceEquals(existing, asset))
                {
                    Object.DestroyImmediate(asset);
                }

                return existing;
            }

            AssetDatabase.DeleteAsset(assetPath);
        }

        AssetDatabase.CreateAsset(asset, assetPath);
        return asset;
    }

    public static void DeleteFolderIfExists(string folderPath)
    {
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        AssetDatabase.DeleteAsset(folderPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    public static void EnsureChildFolderExists(string parentFolder, string childFolderName)
    {
        var childFolderPath = $"{parentFolder}/{childFolderName}";
        if (!AssetDatabase.IsValidFolder(childFolderPath))
        {
            AssetDatabase.CreateFolder(parentFolder, childFolderName);
        }
    }

    public static void EnsureOutputFolderExists(string outputRoot, string outputDir)
    {
        if (!AssetDatabase.IsValidFolder(outputRoot))
        {
            EnsureChildFolderExists("Assets", Path.GetFileName(outputRoot));
        }

        if (!AssetDatabase.IsValidFolder(outputDir))
        {
            EnsureChildFolderExists(outputRoot, Path.GetFileName(outputDir));
        }
    }
}
