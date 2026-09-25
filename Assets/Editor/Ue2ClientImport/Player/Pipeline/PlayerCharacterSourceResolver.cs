using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services.CharacterServices;
using L2Viewer.SceneDomain.Services.Utility;

internal sealed class PlayerCharacterSourceSet
{
    public PlayerCharacterSourceSet(
        SceneSkeletalAsset baseAsset,
        PlayerCharacterPartSource[] parts)
    {
        BaseAsset = baseAsset;
        Parts = parts;
    }

    public SceneSkeletalAsset BaseAsset { get; }
    public PlayerCharacterPartSource[] Parts { get; }
}

internal sealed class PlayerCharacterPartSource
{
    public PlayerCharacterPartSource(
        PlayerCharacterVariantPlan variant,
        int partIndex,
        SceneResourceLocation location,
        SceneSkeletalAsset asset,
        SceneSurfaceResourceReference[] surfaces)
    {
        Variant = variant;
        PartIndex = partIndex;
        Location = location;
        Asset = asset;
        Surfaces = surfaces;
    }

    public PlayerCharacterVariantPlan Variant { get; }
    public int PartIndex { get; }
    public SceneResourceLocation Location { get; }
    public SceneSkeletalAsset Asset { get; }
    public SceneSurfaceResourceReference[] Surfaces { get; }
}

internal static class PlayerCharacterSourceResolver
{
    public static PlayerCharacterSourceSet Resolve(
        PlayerCharacterImportPlan plan,
        string clientRoot,
        PlayerCharacterImportProgress progress)
    {
        var packageIndex = ScenePackageIndexer.BuildResourcePackageIndex(clientRoot);
        var skeletalResolver = new SceneSkeletalMeshResolver();
        progress.Report("Source", plan.Appearance.SkeletonMeshLocation.Reference, 0.02f);
        var originalBase = skeletalResolver.ResolveAsset(plan.Appearance.SkeletonMeshLocation);

        var canonicalMeshReferences = plan.Variants
            .Where(x => !PlayerCharacterImportPlanBuilder.IsWeaponSlotName(x.SlotName))
            .SelectMany(x => x.MeshReferences)
            .GroupBy(x => x.Reference, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
        var additionalSkeletons = new List<SceneSkeletalSkeleton>(canonicalMeshReferences.Length);
        for (var index = 0; index < canonicalMeshReferences.Length; index++)
        {
            var reference = canonicalMeshReferences[index];
            progress.ReportItem("Skeleton sources", reference.Reference, index, canonicalMeshReferences.Length, 0.03f, 0.08f);
            additionalSkeletons.Add(PlayerCharacterPartAssetBuilder.LoadMeshSkeleton(
                ResolveLocation(clientRoot, packageIndex, reference)));
        }

        var canonicalBase = PlayerCharacterSkeletonMergeUtility.BuildCanonicalAsset(originalBase, additionalSkeletons);
        var uniqueMeshReferences = plan.Variants
            .SelectMany(x => x.MeshReferences)
            .GroupBy(x => x.Reference, StringComparer.OrdinalIgnoreCase)
            .Select(x => x.First())
            .ToArray();
        var sourcesByReference = new Dictionary<string, (SceneResourceLocation Location, SceneSkeletalAsset Asset)>(
            StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < uniqueMeshReferences.Length; index++)
        {
            var reference = uniqueMeshReferences[index];
            progress.ReportItem("Mesh sources", reference.Reference, index, uniqueMeshReferences.Length, 0.08f, 0.15f);
            var location = ResolveLocation(clientRoot, packageIndex, reference);
            var asset = PlayerCharacterPartAssetBuilder.BuildMeshOnlyAsset(location, canonicalBase);
            sourcesByReference.Add(reference.Reference, (location, asset));
        }

        var partCount = plan.Variants.Sum(x => x.MeshReferences.Length);
        var parts = new List<PlayerCharacterPartSource>(partCount);
        foreach (var variant in plan.Variants)
        {
            for (var partIndex = 0; partIndex < variant.MeshReferences.Length; partIndex++)
            {
                var meshReference = variant.MeshReferences[partIndex];
                var source = sourcesByReference[meshReference.Reference];
                var surfaces = ResolvePartSurfaces(variant.SurfaceReferences, source.Asset, partIndex);
                parts.Add(new PlayerCharacterPartSource(
                    variant,
                    partIndex,
                    source.Location,
                    source.Asset,
                    surfaces));
            }
        }

        return new PlayerCharacterSourceSet(canonicalBase, parts.ToArray());
    }

    private static SceneSurfaceResourceReference[] ResolvePartSurfaces(
        SceneResourceReference[] overrides,
        SceneSkeletalAsset asset,
        int partIndex)
    {
        if (overrides.Length == 0)
        {
            return asset.SurfaceResourceReferences.Distinct().ToArray();
        }

        var selected = overrides[Math.Min(partIndex, overrides.Length - 1)];
        return new[] { new SceneSurfaceResourceReference(selected.ResourceId) };
    }

    private static SceneResourceLocation ResolveLocation(
        string clientRoot,
        IReadOnlyDictionary<string, string> packageIndex,
        SceneResourceReference reference)
    {
        var packagePath = packageIndex[reference.PackageName];
        return SceneReferenceUtilities.BuildResourceLocation(
            clientRoot,
            packagePath,
            reference.PackageName,
            reference.ObjectName,
            reference.ClassName);
    }
}
