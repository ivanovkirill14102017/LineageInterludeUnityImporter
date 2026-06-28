using System;
using System.IO;
using L2Viewer.SceneDomain.Services;
using UnityEditor;
using UnityEngine;

internal static class AngelNpcImportBuilder
{
    public const string OutputRoot = "Assets/L2Imported/Npcs";

    internal readonly struct ImportOptions
    {
        public ImportOptions(string actorXPskPath, string actorXPsaPath)
        {
            ActorXPskPath = actorXPskPath ?? string.Empty;
            ActorXPsaPath = actorXPsaPath ?? string.Empty;
        }

        public string ActorXPskPath { get; }
        public string ActorXPsaPath { get; }
    }

    internal readonly struct ImportResult
    {
        public ImportResult(string prefabPath)
        {
            PrefabPath = prefabPath;
        }

        public string PrefabPath { get; }
    }

    public static ImportResult Import(string clientRoot, string packageRelativePath, string meshName, ImportOptions options, Action<string> log)
    {
        var packagePath = Path.Combine(clientRoot ?? string.Empty, packageRelativePath ?? string.Empty);
        if (string.IsNullOrWhiteSpace(packagePath) || !File.Exists(packagePath))
        {
            throw new FileNotFoundException("UKX package was not found.", packagePath);
        }

        if (string.IsNullOrWhiteSpace(meshName))
        {
            throw new InvalidOperationException("Mesh name is required.");
        }

        log($"Reading shared skeletal asset from package: {packagePath}");
        var resolver = new SceneSkeletalMeshResolver();
        var sharedAsset = resolver.ResolveAssetNamed(packagePath, meshName);
        log($"Shared asset loaded. Bones={sharedAsset.Skeleton.Bones.Count}, Points={sharedAsset.Mesh.Points.Count}, Faces={sharedAsset.Mesh.Faces.Count}, Sequences={sharedAsset.AnimationSet.Sequences.Count}.");

        var characterName = string.IsNullOrWhiteSpace(meshName) ? sharedAsset.MeshObjectName : meshName;
        var referenceText = L2AssetManager.BuildReferenceText(
            Path.GetFileNameWithoutExtension(packagePath),
            sharedAsset.MeshObjectName ?? characterName,
            characterName);
        var result = L2SkeletalPrefabAssetBuilder.BuildFromResolvedAsset(
            clientRoot,
            sharedAsset,
            OutputRoot,
            OutputRoot,
            referenceText,
            prefabNameSuffix: null,
            displayLabel: characterName,
            log);
        log($"Prefab updated: {result.PrefabPath}");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath);
        if (prefab != null)
        {
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance != null)
            {
                instance.name = prefab.name;
                Selection.activeObject = instance;
                log("Prefab instantiated into the current scene and selected.");
            }
        }

        return new ImportResult(result.PrefabPath);
    }
}
