using System;
using System.IO;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

internal static class CreatureNpcImportBuilder
{
    public static string PrefabOutputRoot => L2AssetManager.ManagedCreaturePrefabsRoot;
    public static string AssetOutputRoot => L2AssetManager.SharedSkeletalCharactersRoot;

    internal readonly struct ImportResult
    {
        public ImportResult(string prefabPath)
        {
            PrefabPath = prefabPath;
        }

        public string PrefabPath { get; }
    }

    public static ImportResult Import(string clientRoot, string packageRelativePath, string meshName, Action<string> log)
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
        var result = L2SkeletalAnimatorPrefabBuilder.BuildFromResolvedAsset(
            clientRoot,
            sharedAsset,
            PrefabOutputRoot,
            AssetOutputRoot,
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

    public static ImportResult ImportByIdentifier(string clientRoot, string creatureIdentifier, Action<string> log)
    {
        var resolved = CreatureMeshLocator.ResolveByIdentifier(clientRoot, creatureIdentifier, log);
        var characterName = string.IsNullOrWhiteSpace(creatureIdentifier)
            ? resolved.SharedAsset.MeshObjectName
            : CreatureIdentifierUtility.NormalizeCreatureIdentifier(creatureIdentifier);
        characterName = string.IsNullOrWhiteSpace(characterName)
            ? resolved.SharedAsset.MeshObjectName
            : characterName;

        log?.Invoke($"Resolved '{characterName}' in package: {resolved.PackagePath}");

        var referenceText = L2AssetManager.BuildReferenceText(
            Path.GetFileNameWithoutExtension(resolved.PackagePath),
            resolved.SharedAsset.MeshObjectName ?? characterName,
            characterName);
        var result = L2SkeletalAnimatorPrefabBuilder.BuildFromResolvedAsset(
            clientRoot,
            resolved.SharedAsset,
            PrefabOutputRoot,
            AssetOutputRoot,
            referenceText,
            prefabNameSuffix: null,
            displayLabel: characterName,
            log);
        log?.Invoke($"Prefab updated: {result.PrefabPath}");

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath);
        if (prefab != null)
        {
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance != null)
            {
                instance.name = prefab.name;
                Selection.activeObject = instance;
                log?.Invoke("Prefab instantiated into the current scene and selected.");
            }
        }

        return new ImportResult(result.PrefabPath);
    }
}

internal static class L2SkeletalAnimatorPrefabBuilder
{
    internal sealed class BuildContext
    {
        public BuildContext(string clientRoot)
        {
            ClientRoot = clientRoot ?? string.Empty;
            TextureManager = new BspTextureManager(ClientRoot);
            MaterialResolver = new SceneMaterialResolver(ClientRoot, TextureManager);
        }

        public string ClientRoot { get; }
        public BspTextureManager TextureManager { get; }
        public SceneMaterialResolver MaterialResolver { get; }
    }

    private static readonly Quaternion UnityBasisRotation = Quaternion.AngleAxis(-90f, Vector3.right);
    private static readonly Quaternion UnityBasisRotationInverse = Quaternion.Inverse(UnityBasisRotation);

    internal readonly struct BuildResult
    {
        public BuildResult(string prefabPath, GameObject prefab, L2SkeletalCharacterAsset characterAsset, Mesh sourceMesh, RuntimeAnimatorController controller)
        {
            PrefabPath = prefabPath;
            Prefab = prefab;
            CharacterAsset = characterAsset;
            SourceMesh = sourceMesh;
            Controller = controller;
        }

        public string PrefabPath { get; }
        public GameObject Prefab { get; }
        public L2SkeletalCharacterAsset CharacterAsset { get; }
        public Mesh SourceMesh { get; }
        public RuntimeAnimatorController Controller { get; }
    }

    public static BuildContext CreateBuildContext(string clientRoot)
    {
        return new BuildContext(clientRoot);
    }

    public static BuildResult Import(string clientRoot, string packageRelativePath, string meshName, Action<string> log)
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

        log?.Invoke($"[SkinnedPOC] Reading shared skeletal asset: {packagePath}");
        var resolver = new SceneSkeletalMeshResolver();
        var sharedAsset = resolver.ResolveAssetNamed(packagePath, meshName);
        if (sharedAsset == null)
        {
            throw new InvalidOperationException($"Mesh '{meshName}' was not resolved from '{packagePath}'.");
        }

        log?.Invoke($"[SkinnedPOC] Loaded asset. Bones={sharedAsset.Skeleton.Bones.Count}, Points={sharedAsset.Mesh.Points.Count}, Faces={sharedAsset.Mesh.Faces.Count}, Sequences={sharedAsset.AnimationSet.Sequences.Count}.");

        var characterName = string.IsNullOrWhiteSpace(meshName) ? sharedAsset.MeshObjectName : meshName;
        var referenceText = L2AssetManager.BuildReferenceText(
            Path.GetFileNameWithoutExtension(packagePath),
            sharedAsset.MeshObjectName ?? characterName,
            characterName);
        var result = BuildFromResolvedAsset(
            clientRoot,
            sharedAsset,
            CreatureNpcImportBuilder.PrefabOutputRoot,
            CreatureNpcImportBuilder.AssetOutputRoot,
            referenceText,
            prefabNameSuffix: null,
            displayLabel: characterName,
            log);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(result.PrefabPath);
        if (prefab != null)
        {
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance != null)
            {
                instance.name = prefab.name;
                Selection.activeObject = instance;
                log?.Invoke("[CreatureAnimator] Prefab instantiated into the current scene and selected.");
            }
        }

        return result;
    }

    public static BuildResult BuildFromResolvedAsset(
        string clientRoot,
        SceneSkeletalAsset sharedAsset,
        string prefabRoot,
        string assetRoot,
        string referenceText,
        string prefabNameSuffix,
        string displayLabel,
        Action<string> log,
        BuildContext context = null,
        bool finalizeAssets = true)
    {
        if (sharedAsset == null)
        {
            throw new ArgumentNullException(nameof(sharedAsset));
        }

        var characterName = string.IsNullOrWhiteSpace(sharedAsset.MeshObjectName)
            ? "L2SkeletalCharacter"
            : sharedAsset.MeshObjectName;
        var activeContext = context ?? new BuildContext(clientRoot);

        L2AssetManager.EnsureFolderExists(assetRoot);
        L2AssetManager.EnsureFolderExists(prefabRoot);

        var characterAsset = L2SkeletalCharacterAssetFactory.Build(characterName, sharedAsset);
        var characterAssetPath = L2AssetManager.BuildClientPackageAssetPath(
            assetRoot,
            referenceText,
            "NPC",
            "asset",
            "SkeletalCharacters");
        characterAsset = UnityAssetDatabaseUtility.CreateOrReplaceAsset(characterAsset, characterAssetPath);
        log?.Invoke($"[CreatureAnimator] Character asset updated: {characterAssetPath}");

        var materials = CreatureSkeletalMaterialImporter.CreateMaterials(characterAsset, referenceText, assetRoot, log, activeContext);
        var skinnedMesh = CreatureSkinnedMeshBuilder.Build(characterAsset, materials, log, out _);
        var meshPath = L2AssetManager.BuildClientPackageAssetPath(
            assetRoot,
            referenceText,
            "SM",
            "asset",
            "SkeletalCharacters",
            "skinned");
        skinnedMesh = UnityAssetDatabaseUtility.CreateOrReplaceAsset(skinnedMesh, meshPath);
        log?.Invoke($"[CreatureAnimator] Skinned mesh updated: {meshPath}");

        var sequenceNames = CreatureSkeletalImportUtility.GetAllSequenceNames(characterAsset);
        var clips = CreatureAnimationClipBuilder.Build(characterAsset, referenceText, assetRoot, sequenceNames, log, out _);
        var controller = CreatureAnimatorControllerBuilder.Build(referenceText, prefabRoot, clips, log, out _);
        var prefabPath = L2AssetManager.BuildClientPackageAssetPath(
            prefabRoot,
            referenceText,
            "PF",
            "prefab",
            "CreaturePrefabs",
            prefabNameSuffix);
        CreatureSkeletalPrefabFactory.Create(characterAsset, skinnedMesh, materials, controller, prefabPath, string.IsNullOrWhiteSpace(displayLabel) ? characterName : displayLabel);

        if (finalizeAssets)
        {
            AssetDatabase.SaveAssets();
            AssetDatabase.ImportAsset(characterAssetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(meshPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            foreach (var clip in clips)
            {
                AssetDatabase.ImportAsset(clip.Path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
            }

            if (controller != null)
            {
                var controllerPath = AssetDatabase.GetAssetPath(controller);
                if (!string.IsNullOrWhiteSpace(controllerPath))
                {
                    AssetDatabase.ImportAsset(controllerPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                }
            }

            AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        }

        var prefab = finalizeAssets ? AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) : null;
        return new BuildResult(prefabPath, prefab, characterAsset, skinnedMesh, controller);
    }
}
