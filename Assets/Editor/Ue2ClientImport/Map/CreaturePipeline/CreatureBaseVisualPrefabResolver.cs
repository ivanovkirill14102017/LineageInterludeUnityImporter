using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal static class CreatureBaseVisualPrefabResolver
{
    private sealed class BuildState
    {
        public BuildState(
            CreatureVisualSource visual,
            L2SkeletalAnimatorPrefabBuilder.PreparedBuildData prepared)
        {
            Visual = visual;
            Prepared = prepared;
        }

        public CreatureVisualSource Visual { get; }
        public L2SkeletalAnimatorPrefabBuilder.PreparedBuildData Prepared { get; }
        public Mesh Mesh { get; set; }
        public RuntimeAnimatorController AnimatorController { get; set; }
    }

    public static void Resolve(
        CreatureVisualSourceSet sources,
        string clientRoot,
        CreatureImportProgress progress)
    {
        var builds = PrepareAll(sources, clientRoot, progress);
        BuildMeshes(builds, progress);
        BuildAnimations(builds, progress);
        BuildPrefabs(builds, progress);
        AssetDatabase.SaveAssets();
    }

    public static string BuildPrefabPath(CreatureVisualImportPlan visual)
    {
        return L2AssetManager.BuildClientPackageAssetPath(
            L2AssetManager.ManagedCreaturePrefabsRoot,
            visual.Source.ActorClassResource.Reference,
            "PF",
            "prefab",
            "CreaturePrefabs",
            BasePrefabSuffix(visual));
    }

    private static BuildState[] PrepareAll(
        CreatureVisualSourceSet sources,
        string clientRoot,
        CreatureImportProgress progress)
    {
        var builds = new BuildState[sources.Visuals.Length];
        for (var index = 0; index < sources.Visuals.Length; index++)
        {
            var visual = sources.Visuals[index];
            progress.ReportItem("Skeleton assets", visual.Plan.MeshReference, index, sources.Visuals.Length, 0.48f, 0.54f);
            var prepared = L2SkeletalAnimatorPrefabBuilder.PrepareFromResolvedAsset(
                clientRoot,
                visual.SkeletalAsset,
                L2AssetManager.ManagedCreaturePrefabsRoot,
                L2AssetManager.SharedSkeletalCharactersRoot,
                visual.Plan.MeshReference.ToString(),
                null,
                includeMaterials: false);
            ApplyTextureReferences(prepared.CharacterAsset, visual);
            builds[index] = new BuildState(visual, WithMaterials(prepared, visual));
        }

        return builds;
    }

    private static void BuildMeshes(BuildState[] builds, CreatureImportProgress progress)
    {
        for (var index = 0; index < builds.Length; index++)
        {
            var build = builds[index];
            progress.ReportItem("Skeletal meshes", build.Visual.Plan.MeshReference, index, builds.Length, 0.54f, 0.62f);
            var prepared = build.Prepared;
            var meshPath = L2AssetManager.BuildClientPackageAssetPath(
                L2AssetManager.SharedSkeletalMeshesRoot,
                prepared.ReferenceText,
                "SM",
                "asset",
                "SkeletalMeshes");
            build.Mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (build.Mesh == null)
            {
                var mesh = CreatureSkinnedMeshBuilder.Build(
                    prepared.CharacterAsset,
                    prepared.Materials,
                    null,
                    out _);
                build.Mesh = UnityAssetDatabaseUtility.CreateAssetIfMissing(mesh, meshPath);
            }
        }
    }

    private static void BuildAnimations(BuildState[] builds, CreatureImportProgress progress)
    {
        for (var index = 0; index < builds.Length; index++)
        {
            var build = builds[index];
            progress.ReportItem("Animations", build.Visual.Plan.MeshReference, index, builds.Length, 0.62f, 0.72f);
            var prepared = build.Prepared;
            var sequenceNames = CreatureSkeletalImportUtility.GetAllSequenceNames(prepared.CharacterAsset);
            var clips = CreatureAnimationClipBuilder.Build(
                prepared.CharacterAsset,
                sequenceNames,
                null,
                out _);
            build.AnimatorController = CreatureAnimatorControllerBuilder.Build(
                prepared.CharacterAsset,
                prepared.ReferenceText,
                prepared.PrefabRoot,
                clips,
                null,
                out _);
        }
    }

    private static void BuildPrefabs(BuildState[] builds, CreatureImportProgress progress)
    {
        for (var index = 0; index < builds.Length; index++)
        {
            var build = builds[index];
            progress.ReportItem("Base prefabs", build.Visual.Plan.MeshReference, index, builds.Length, 0.72f, 0.78f);
            var prepared = build.Prepared;
            var archetype = ScriptableObject.CreateInstance<L2CreatureCharacterArchetypeAsset>();
            archetype.ArchetypeName = prepared.CharacterName;
            archetype.DisplayName = build.Visual.Plan.Source.DisplayName;
            archetype.SkeletonReference = prepared.CharacterAsset.MeshObjectName;
            archetype.SkeletonUri = prepared.CharacterAsset.SourcePackagePath;
            archetype.BaseAsset = prepared.CharacterAsset;
            archetype.AnimatorController = build.AnimatorController;
            archetype.Slots = new[]
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
                                    Mesh = build.Mesh,
                                    Materials = prepared.Materials,
                                    BoneNames = prepared.CharacterAsset.Bones.Select(x => x.Name).ToArray(),
                                    BoneParentIndices = prepared.CharacterAsset.Bones.Select(x => x.ParentIndex).ToArray()
                                }
                            }
                        }
                    }
                }
            };
            var archetypePath = L2AssetManager.BuildClientPackageAssetPath(
                $"{prepared.PrefabRoot}/Archetypes",
                build.Visual.Plan.Source.ActorClassResource.Reference,
                "NCA",
                "asset",
                "CreatureArchetypes",
                "base");
            archetype = UnityAssetDatabaseUtility.CreateOrReplaceAsset(archetype, archetypePath);
            CreatureSkeletalPrefabFactory.Create(
                archetype,
                BuildPrefabPath(build.Visual.Plan),
                build.Visual.Plan.Source.DisplayName);
        }
    }

    private static L2SkeletalAnimatorPrefabBuilder.PreparedBuildData WithMaterials(
        L2SkeletalAnimatorPrefabBuilder.PreparedBuildData prepared,
        CreatureVisualSource visual)
    {
        var materialIds = CreatureSkeletalImportUtility.GetMaterialIds(prepared.CharacterAsset);
        var materials = materialIds
            .Select((_, index) => AssetDatabase.LoadAssetAtPath<Material>(
                SharedMaterialResourceResolver.BuildAssetPath(visual.MaterialRequests[index].Id))!)
            .ToArray();
        return new L2SkeletalAnimatorPrefabBuilder.PreparedBuildData(
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

    private static void ApplyTextureReferences(
        L2SkeletalCharacterAsset characterAsset,
        CreatureVisualSource visual)
    {
        characterAsset.UsedTextures = visual.TextureReferences
            .Select(x => new L2SkeletalTextureRefData
            {
                Reference = x.ToString(),
                ResolvedPackagePath = string.Empty
            })
            .ToArray();
        if (visual.TextureReferences.Length == 0)
        {
            return;
        }

        characterAsset.PrimaryTextureReference = visual.TextureReferences[0].ToString();
        var bindings = characterAsset.MaterialBindings ?? Array.Empty<L2SkeletalMaterialBindingData>();
        for (var index = 0; index < bindings.Length; index++)
        {
            var reference = visual.TextureReferences[index];
            bindings[index].PackageName = reference.Id.PackageName;
            bindings[index].ObjectName = reference.Id.ObjectPath;
            bindings[index].TextureReference = reference.ToString();
        }

        EditorUtility.SetDirty(characterAsset);
    }

    private static string BasePrefabSuffix(CreatureVisualImportPlan visual)
    {
        return "base";
    }
}
