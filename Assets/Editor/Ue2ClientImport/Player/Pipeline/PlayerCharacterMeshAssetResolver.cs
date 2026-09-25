using System;
using System.Collections.Generic;

using System.Linq;
using L2Viewer.SceneDomain.Models;
using UnityEditor;
using UnityEngine;

internal sealed class PlayerCharacterMeshAssetSet
{
    public PlayerCharacterMeshAssetSet(
        L2SkeletalCharacterAsset baseAsset,
        PlayerCharacterBuiltPart[] parts)
    {
        BaseAsset = baseAsset;
        Parts = parts;
    }

    public L2SkeletalCharacterAsset BaseAsset { get; }
    public PlayerCharacterBuiltPart[] Parts { get; }
}

internal sealed class PlayerCharacterBuiltPart
{
    public PlayerCharacterBuiltPart(PlayerCharacterPartSource source, Mesh mesh, Material[] materials, string[] boneNames, int[] boneParentIndices)
    {
        Source = source;
        Mesh = mesh;
        Materials = materials;
        BoneNames = boneNames;
        BoneParentIndices = boneParentIndices;
    }

    public PlayerCharacterPartSource Source { get; }
    public Mesh Mesh { get; }
    public Material[] Materials { get; }
    public string[] BoneNames { get; }
    public int[] BoneParentIndices { get; }
}

internal static class PlayerCharacterMeshAssetResolver
{
    public static PlayerCharacterMeshAssetSet Resolve(
        PlayerCharacterSourceSet sources,
        string characterName,
        string characterAssetPath,
        PlayerCharacterImportProgress progress)
    {
        var baseAsset = L2SkeletalCharacterAssetFactory.Build(characterName, sources.BaseAsset);
        baseAsset = UnityAssetDatabaseUtility.CreateOrReplaceAsset(baseAsset, characterAssetPath);
        var missingTextureMaterial = SharedMaterialResourceResolver.ResolveMissingTextureMaterial();
        var uniqueSources = sources.Parts
            .GroupBy(BuildMeshKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
        var preparedMeshes = new Dictionary<string, (Mesh Mesh, int MaterialCount, string[] BoneNames, int[] BoneParentIndices)>(
            StringComparer.OrdinalIgnoreCase);

        UnityAssetDatabaseUtility.RunAssetEditingBatch(() =>
        {
            for (var index = 0; index < uniqueSources.Length; index++)
            {
                var source = uniqueSources[index];
                progress.ReportItem("Meshes", source.Location.Reference, index, uniqueSources.Length, 0.48f, 0.72f);
                var renderAsset = L2SkeletalCharacterAssetFactory.Build(
                    source.Location.ObjectName,
                    source.Asset);
                var meshPath = BuildMeshPath(source);
                if (source.Variant.Binding == SceneCharacterPartBinding.RigidHead)
                {
                    var adaptedSource = PlayerCharacterPartAssetBuilder.BuildMeshOnlyAsset(
                        source.Location,
                        sources.BaseAsset,
                        SceneCharacterPartBinding.RigidHead);
                    renderAsset = L2SkeletalCharacterAssetFactory.Build(source.Location.ObjectName, adaptedSource);
                    meshPath = BuildAdaptedMeshPath(source);
                }

                var materialCount = CreatureSkeletalImportUtility.GetMaterialIds(renderAsset).Length;
                var mesh = BuildOrLoadMesh(renderAsset, new Material[materialCount], meshPath);
                preparedMeshes.Add(
                    BuildMeshKey(source),
                    (
                        mesh,
                        materialCount,
                        renderAsset.Bones.Select(x => x.Name).ToArray(),
                        renderAsset.Bones.Select(x => x.ParentIndex).ToArray()));
            }
        });

        var builtParts = new PlayerCharacterBuiltPart[sources.Parts.Length];
        for (var index = 0; index < sources.Parts.Length; index++)
        {
            var source = sources.Parts[index];
            var prepared = preparedMeshes[BuildMeshKey(source)];
            var materials = source.Surfaces.Length == 0
                ? Enumerable.Repeat(missingTextureMaterial, prepared.MaterialCount).ToArray()
                : Enumerable.Range(0, prepared.MaterialCount)
                    .Select(materialIndex => AssetDatabase.LoadAssetAtPath<Material>(
                        SharedMaterialResourceResolver.BuildAssetPath(
                            MaterialResourceRequest.Opaque(
                                source.Surfaces[Math.Min(materialIndex, source.Surfaces.Length - 1)]).Id)))
                    .ToArray();
            builtParts[index] = new PlayerCharacterBuiltPart(
                source,
                prepared.Mesh,
                materials,
                prepared.BoneNames,
                prepared.BoneParentIndices);
        }

        return new PlayerCharacterMeshAssetSet(baseAsset, builtParts);
    }

    private static string BuildMeshKey(PlayerCharacterPartSource source)
    {
        return $"{source.Variant.Binding}:{source.Location.Reference}";
    }

    private static Mesh BuildOrLoadMesh(
        L2SkeletalCharacterAsset asset,
        Material[] materials,
        string assetPath)
    {
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(assetPath);
        if (mesh != null)
        {
            return mesh;
        }

        mesh = CreatureSkinnedMeshBuilder.Build(asset, materials, null, out _);
        return UnityAssetDatabaseUtility.CreateAssetIfMissing(mesh, assetPath);
    }

    private static string BuildMeshPath(PlayerCharacterPartSource source)
    {
        return L2AssetManager.BuildClientPackageAssetPath(
            L2AssetManager.SharedSkeletalMeshesRoot,
            source.Location.Reference,
            "SM",
            "asset",
            "SkeletalMeshes");
    }

    private static string BuildAdaptedMeshPath(PlayerCharacterPartSource source)
    {
        return L2AssetManager.BuildClientPackageAssetPath(
            L2AssetManager.ManagedSkeletalMeshAdaptationsRoot,
            source.Location.Reference,
            "SM",
            "asset",
            "RigidHead",
            "rigid_head");
    }
}
