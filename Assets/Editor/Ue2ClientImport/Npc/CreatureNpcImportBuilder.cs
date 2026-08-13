using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Services.CharacterServices;
using L2Viewer.SceneDomain.Services.MaterialServices;
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
        log($"Prefab ready: {result.PrefabPath}");

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
        log?.Invoke($"Prefab ready: {result.PrefabPath}");

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

public static class CreatureNpcBatchImportCommand
{
    public static void ImportByIdentifier()
    {
        var creatureId = GetArgumentValue("-creatureId");
        if (string.IsNullOrWhiteSpace(creatureId))
        {
            throw new InvalidOperationException("Missing required -creatureId argument for batch creature import.");
        }

        var clientRoot = ConstInfo.L2GameClientPath;
        var resolved = CreatureMeshLocator.ResolveByIdentifier(clientRoot, creatureId, Debug.Log);
        var characterName = string.IsNullOrWhiteSpace(creatureId)
            ? resolved.SharedAsset.MeshObjectName
            : CreatureIdentifierUtility.NormalizeCreatureIdentifier(creatureId);
        characterName = string.IsNullOrWhiteSpace(characterName)
            ? resolved.SharedAsset.MeshObjectName
            : characterName;

        var referenceText = L2AssetManager.BuildReferenceText(
            Path.GetFileNameWithoutExtension(resolved.PackagePath),
            resolved.SharedAsset.MeshObjectName ?? characterName,
            characterName);

        L2SkeletalAnimatorPrefabBuilder.BuildFromResolvedAsset(
            clientRoot,
            resolved.SharedAsset,
            CreatureNpcImportBuilder.PrefabOutputRoot,
            CreatureNpcImportBuilder.AssetOutputRoot,
            referenceText,
            prefabNameSuffix: null,
            displayLabel: characterName,
            Debug.Log);

        AssetDatabase.SaveAssets();
        Debug.Log($"[CreatureNpcBatchImportCommand] Imported '{characterName}'.");
    }

    private static string GetArgumentValue(string argumentName)
    {
        var arguments = Environment.GetCommandLineArgs();
        for (var i = 0; i < arguments.Length - 1; i++)
        {
            if (string.Equals(arguments[i], argumentName, StringComparison.OrdinalIgnoreCase))
            {
                return arguments[i + 1];
            }
        }

        return null;
    }
}

internal static class L2SkeletalAnimatorPrefabBuilder
{
    internal sealed class PreparedBuildData
    {
        public PreparedBuildData(
            string referenceText,
            string prefabRoot,
            string assetRoot,
            string characterName,
            string characterAssetPath,
            SceneSkeletalAsset sharedAsset,
            L2SkeletalCharacterAsset characterAsset,
            Material[] materials,
            BuildContext context)
        {
            ReferenceText = referenceText;
            PrefabRoot = prefabRoot;
            AssetRoot = assetRoot;
            CharacterName = characterName;
            CharacterAssetPath = characterAssetPath;
            SharedAsset = sharedAsset;
            CharacterAsset = characterAsset;
            Materials = materials ?? Array.Empty<Material>();
            Context = context;
        }

        public string ReferenceText { get; }
        public string PrefabRoot { get; }
        public string AssetRoot { get; }
        public string CharacterName { get; }
        public string CharacterAssetPath { get; }
        public SceneSkeletalAsset SharedAsset { get; }
        public L2SkeletalCharacterAsset CharacterAsset { get; }
        public Material[] Materials { get; }
        public BuildContext Context { get; }
    }

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
        public Dictionary<string, Texture2D> ImportedTextureCache { get; } = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, CreatureSkeletalMaterialImporter.TextureImportPlan> TexturePlanCache { get; } =
            new Dictionary<string, CreatureSkeletalMaterialImporter.TextureImportPlan>(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, object> ExactBindingCache { get; } = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
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

        var activeContext = context ?? new BuildContext(clientRoot);
        var prepared = PrepareFromResolvedAsset(
            clientRoot,
            sharedAsset,
            prefabRoot,
            assetRoot,
            referenceText,
            log,
            activeContext,
            includeMaterials: true);

        return CompletePreparedBuild(
            prepared,
            prefabNameSuffix,
            displayLabel,
            log,
            finalizeAssets);
    }

    public static PreparedBuildData PrepareFromResolvedAsset(
        string clientRoot,
        SceneSkeletalAsset sharedAsset,
        string prefabRoot,
        string assetRoot,
        string referenceText,
        Action<string> log,
        BuildContext context = null,
        bool includeMaterials = true,
        string assetObjectRootSuffix = null)
    {
        if (sharedAsset == null)
        {
            throw new ArgumentNullException(nameof(sharedAsset));
        }

        var characterName = string.IsNullOrWhiteSpace(sharedAsset.MeshObjectName)
            ? "L2SkeletalCharacter"
            : sharedAsset.MeshObjectName;
        var activeContext = context ?? new BuildContext(clientRoot);
        var assetObjectRoot = ResolveSkeletalAssetObjectRoot(assetRoot, sharedAsset, characterName);
        if (!string.IsNullOrWhiteSpace(assetObjectRootSuffix))
        {
            assetObjectRoot = $"{assetObjectRoot}/{SanitizePathSegment(assetObjectRootSuffix)}";
        }

        L2AssetManager.EnsureFolderExists(assetObjectRoot);
        L2AssetManager.EnsureFolderExists(prefabRoot);

        var characterAsset = L2SkeletalCharacterAssetFactory.Build(characterName, sharedAsset);
        var characterAssetPath = L2AssetManager.BuildAssetPathInFolder(
            assetObjectRoot,
            "NPC",
            sharedAsset.MeshObjectName ?? characterName,
            "asset",
            "skeleton");
        characterAsset = UnityAssetDatabaseUtility.CreateAssetIfMissing(characterAsset, characterAssetPath);
        log?.Invoke($"[CreatureAnimator] Character asset ready: {characterAssetPath}");

        var materials = includeMaterials
            ? CreatureSkeletalMaterialImporter.CreateMaterials(
                characterAsset,
                referenceText,
                $"{assetObjectRoot}/Materials",
                log,
                activeContext)
            : Array.Empty<Material>();
        return new PreparedBuildData(
            referenceText,
            prefabRoot,
            assetObjectRoot,
            characterName,
            characterAssetPath,
            sharedAsset,
            characterAsset,
            materials,
            activeContext);
    }

    public static PreparedBuildData MaterializePreparedBuildMaterials(
        PreparedBuildData prepared,
        Action<string> log)
    {
        if (prepared == null)
        {
            throw new ArgumentNullException(nameof(prepared));
        }

        var materials = CreatureSkeletalMaterialImporter.CreateMaterials(
            prepared.CharacterAsset,
            prepared.ReferenceText,
            $"{prepared.AssetRoot}/Materials",
            log,
            prepared.Context);
        return new PreparedBuildData(
            prepared.ReferenceText,
            prepared.PrefabRoot,
            prepared.AssetRoot,
            prepared.CharacterName,
            prepared.CharacterAssetPath,
            prepared.SharedAsset,
            prepared.CharacterAsset,
            materials,
            prepared.Context);
    }

    public static BuildResult CompletePreparedBuild(
        PreparedBuildData prepared,
        string prefabNameSuffix,
        string displayLabel,
        Action<string> log,
        bool finalizeAssets = true,
        string archetypeNameSuffix = null,
        IReadOnlyList<L2CharacterSlotCatalogData> extraSlots = null)
    {
        if (prepared == null)
        {
            throw new ArgumentNullException(nameof(prepared));
        }

        var characterAsset = prepared.CharacterAsset;
        var materials = prepared.Materials ?? Array.Empty<Material>();
        var skinnedMesh = CreatureSkinnedMeshBuilder.Build(characterAsset, materials, log, out _);
        var meshPath = L2AssetManager.BuildAssetPathInFolder(
            $"{prepared.AssetRoot}/Meshes",
            "SM",
            characterAsset.MeshObjectName ?? prepared.CharacterName,
            "asset",
            "skinned");
        skinnedMesh = UnityAssetDatabaseUtility.CreateAssetIfMissing(skinnedMesh, meshPath);
        log?.Invoke($"[CreatureAnimator] Skinned mesh ready: {meshPath}");

        var sequenceNames = CreatureSkeletalImportUtility.GetAllSequenceNames(characterAsset);
        var clips = CreatureAnimationClipBuilder.Build(characterAsset, prepared.ReferenceText, prepared.AssetRoot, sequenceNames, log, out _);
        var controller = CreatureAnimatorControllerBuilder.Build(characterAsset, prepared.ReferenceText, prepared.PrefabRoot, clips, log, out _);
        var archetype = ScriptableObject.CreateInstance<L2CreatureCharacterArchetypeAsset>();
        archetype.ArchetypeName = prepared.CharacterName;
        archetype.DisplayName = string.IsNullOrWhiteSpace(displayLabel) ? prepared.CharacterName : displayLabel;
        archetype.SkeletonReference = characterAsset.MeshObjectName;
        archetype.SkeletonUri = characterAsset.SourcePackagePath;
        archetype.BaseAsset = characterAsset;
        archetype.AnimatorController = controller;
        var slots = new List<L2CharacterSlotCatalogData>
        {
            new L2CharacterSlotCatalogData
            {
                SlotName = "Body",
                DefaultVariantIndex = 0,
                Variants = new[]
                {
                    new L2CharacterVariantData
                    {
                        VariantKey = "body_base",
                        DisplayName = "<Base>",
                        Parts = new[]
                        {
                            new L2CharacterVariantPartData
                            {
                                Name = "Body_00",
                                Mesh = skinnedMesh,
                                Materials = materials
                            }
                        }
                    }
                }
            }
        };
        if (extraSlots != null)
        {
            slots.AddRange(extraSlots.Where(x => x != null));
        }

        archetype.Slots = slots.ToArray();
        var archetypeAssetPath = L2AssetManager.BuildAssetPathInFolder(
            $"{prepared.PrefabRoot}/Archetypes",
            "NCA",
            characterAsset.MeshObjectName ?? prepared.CharacterName,
            "asset",
            string.IsNullOrWhiteSpace(archetypeNameSuffix)
                ? "archetype"
                : $"archetype_{archetypeNameSuffix}");
        archetype = UnityAssetDatabaseUtility.CreateAssetIfMissing(archetype, archetypeAssetPath);
        var prefabPath = L2AssetManager.BuildClientPackageAssetPath(
            prepared.PrefabRoot,
            prepared.ReferenceText,
            "PF",
            "prefab",
            "CreaturePrefabs",
            prefabNameSuffix);
        CreatureSkeletalPrefabFactory.Create(
            archetype,
            prefabPath,
            string.IsNullOrWhiteSpace(displayLabel) ? prepared.CharacterName : displayLabel);

        if (finalizeAssets)
        {
            AssetDatabase.SaveAssets();
        }

        var prefab = finalizeAssets ? AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) : null;
        return new BuildResult(prefabPath, prefab, characterAsset, skinnedMesh, controller);
    }

    private static string ResolveSkeletalAssetObjectRoot(string assetRoot, SceneSkeletalAsset sharedAsset, string fallbackObjectName)
    {
        var packageName = Path.GetFileNameWithoutExtension(sharedAsset?.PackagePath);
        var objectName = sharedAsset?.MeshObjectName ?? fallbackObjectName;
        return L2AssetManager.BuildClientPackageObjectRoot(
            assetRoot,
            packageName,
            objectName,
            "SkeletalCharacters");
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var sanitized = value.Trim().Replace('\\', '_').Replace('/', '_').Replace(':', '_');
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalidChar, '_');
        }

        return sanitized;
    }
}
