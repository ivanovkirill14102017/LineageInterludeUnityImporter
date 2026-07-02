using System;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services.CharacterServices;
using UnityEditor;
using UnityEngine;

internal static class PlayerCharacterImportBuilder
{
    public static string PrefabOutputRoot => L2AssetManager.ManagedPlayerCharacterPrefabsRoot;
    public static string AssetOutputRoot => L2AssetManager.SharedSkeletalCharactersRoot;

    internal readonly struct ImportResult
    {
        public ImportResult(string prefabPath, string characterAssetPath)
        {
            PrefabPath = prefabPath;
            CharacterAssetPath = characterAssetPath;
        }

        public string PrefabPath { get; }
        public string CharacterAssetPath { get; }
    }

    public static ImportResult Import(
        string clientRoot,
        SceneCharacterBaseClass baseClass,
        SceneCharacterGender gender,
        Action<string> log)
    {
        var appearanceBuilder = new SceneCharacterAppearanceBuilder();
        var request = new SceneCharacterAppearanceRequest
        {
            BaseClass = baseClass,
            Gender = gender
        };

        log?.Invoke($"Resolving player skeleton through SceneDomain: BaseClass={baseClass}, Gender={gender}.");
        var appearance = appearanceBuilder.Build(clientRoot, request);
        log?.Invoke(
            $"SceneDomain resolved VisualFamily={appearance.VisualFamily}, " +
            $"Skeleton={appearance.SkeletonMeshLocation.Reference}, Bones={appearance.SkeletonBoneCount}.");

        var resolver = new SceneSkeletalMeshResolver();
        var sharedAsset = resolver.ResolveAsset(appearance.SkeletonMeshLocation);
        log?.Invoke(
            $"SceneDomain skeletal asset loaded. Mesh={sharedAsset.MeshObjectName}, " +
            $"Bones={sharedAsset.Skeleton.Bones.Count}, Sequences={sharedAsset.AnimationSet.Sequences.Count}.");

        var characterName = BuildCharacterName(baseClass, gender, appearance.VisualFamily);
        var referenceText = $"{appearance.SkeletonMeshLocation.Reference}.{appearance.VisualFamily}";

        L2AssetManager.EnsureFolderExists(AssetOutputRoot);
        L2AssetManager.EnsureFolderExists(PrefabOutputRoot);

        var characterAsset = L2SkeletalCharacterAssetFactory.Build(characterName, sharedAsset);
        var characterAssetPath = L2AssetManager.BuildClientPackageAssetPath(
            AssetOutputRoot,
            referenceText,
            "PC",
            "asset",
            "PlayerCharacters",
            "skeleton");
        characterAsset = UnityAssetDatabaseUtility.CreateOrReplaceAsset(characterAsset, characterAssetPath);
        log?.Invoke($"[PlayerCharacter] Character asset updated: {characterAssetPath}");

        var prefabPath = L2AssetManager.BuildClientPackageAssetPath(
            PrefabOutputRoot,
            referenceText,
            "PF",
            "prefab",
            "PlayerCharacterPrefabs",
            "debug");
        CreateDebugPrefab(characterAsset, characterName, prefabPath, appearance, log);

        AssetDatabase.SaveAssets();
        AssetDatabase.ImportAsset(characterAssetPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        AssetDatabase.ImportAsset(prefabPath, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab != null)
        {
            var instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (instance != null)
            {
                instance.name = prefab.name;
                Selection.activeObject = instance;
                log?.Invoke("[PlayerCharacter] Debug prefab instantiated into the current scene and selected.");
            }
        }

        return new ImportResult(prefabPath, characterAssetPath);
    }

    private static void CreateDebugPrefab(
        L2SkeletalCharacterAsset characterAsset,
        string characterName,
        string prefabPath,
        SceneCharacterAppearanceData appearance,
        Action<string> log)
    {
        var root = new GameObject($"PC_{characterName}");
        try
        {
            var meshFilter = root.AddComponent<MeshFilter>();
            var meshRenderer = root.AddComponent<MeshRenderer>();
            meshRenderer.enabled = false;
            meshRenderer.sharedMaterials = Array.Empty<Material>();

            var player = root.AddComponent<L2SkeletalAnimationPlayer>();
            player.Asset = characterAsset;
            player.SourceMesh = null;
            player.ShowMesh = false;
            player.ShowSkeleton = true;
            player.DrawBoneLabels = false;
            player.PreviewInEditMode = true;
            player.Loop = true;
            player.PlayOnEnable = true;
            player.PlaybackSpeed = 1f;
            player.SetVisibility(false, true);
            player.SetSelectedAnimation(FindPreferredAnimationIndex(characterAsset), true);
            player.SetPoseMode(L2SkeletalAnimationPlayer.DiagnosticPoseMode.Animated, false);

            var label = root.AddComponent<L2PlayerCharacterDebugMetadata>();
            label.BaseClass = appearance.BaseClass.ToString();
            label.Gender = appearance.Gender.ToString();
            label.VisualFamily = appearance.VisualFamily.ToString();
            label.SkeletonReference = appearance.SkeletonMeshLocation.Reference;
            label.SkeletonUri = appearance.SkeletonMeshLocation.Uri;

            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            log?.Invoke($"[PlayerCharacter] Debug prefab updated: {prefabPath}");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static int FindPreferredAnimationIndex(L2SkeletalCharacterAsset asset)
    {
        var sequences = asset.AnimationSequences ?? Array.Empty<L2SkeletalAnimationSequenceData>();
        if (sequences.Length == 0)
        {
            return 0;
        }

        var preferredTokens = new[]
        {
            "wait",
            "idle",
            "run",
            "walk",
            "attack"
        };

        for (var tokenIndex = 0; tokenIndex < preferredTokens.Length; tokenIndex++)
        {
            var token = preferredTokens[tokenIndex];
            for (var i = 0; i < sequences.Length; i++)
            {
                var sequence = sequences[i];
                if (sequence == null || string.IsNullOrWhiteSpace(sequence.Name) || sequence.NumRawFrames <= 0)
                {
                    continue;
                }

                if (sequence.Name.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return i;
                }
            }
        }

        for (var i = 0; i < sequences.Length; i++)
        {
            if (sequences[i] != null && sequences[i].NumRawFrames > 0)
            {
                return i;
            }
        }

        return 0;
    }

    private static string BuildCharacterName(
        SceneCharacterBaseClass baseClass,
        SceneCharacterGender gender,
        SceneCharacterVisualFamily visualFamily)
    {
        return $"{gender}_{baseClass}_{visualFamily}";
    }
}
