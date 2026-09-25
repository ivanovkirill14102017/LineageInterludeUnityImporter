using System;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services.CharacterServices;

internal sealed class CreatureVisualSourceSet
{
    public CreatureVisualSourceSet(
        CreatureImportPlan plan,
        CreatureVisualSource[] visuals,
        SceneSurfaceResourceReference[] textureReferences,
        MaterialResourceRequest[] materialRequests)
    {
        Plan = plan;
        Visuals = visuals;
        TextureReferences = textureReferences;
        MaterialRequests = materialRequests;
    }

    public CreatureImportPlan Plan { get; }
    public CreatureVisualSource[] Visuals { get; }
    public SceneSurfaceResourceReference[] TextureReferences { get; }
    public MaterialResourceRequest[] MaterialRequests { get; }
}

internal sealed class CreatureVisualSource
{
    public CreatureVisualSource(
        CreatureVisualImportPlan plan,
        SceneSkeletalAsset skeletalAsset,
        SceneSurfaceResourceReference[] textureReferences,
        MaterialResourceRequest[] materialRequests)
    {
        Plan = plan;
        SkeletalAsset = skeletalAsset;
        TextureReferences = textureReferences;
        MaterialRequests = materialRequests;
    }

    public CreatureVisualImportPlan Plan { get; }
    public SceneSkeletalAsset SkeletalAsset { get; }
    public SceneSurfaceResourceReference[] TextureReferences { get; }
    public MaterialResourceRequest[] MaterialRequests { get; }
}

internal static class CreatureVisualSourceResolver
{
    public static CreatureVisualSourceSet Resolve(
        CreatureImportPlan plan,
        CreatureImportProgress progress)
    {
        var resolver = new SceneSkeletalMeshResolver();
        var visuals = new CreatureVisualSource[plan.Visuals.Length];
        for (var index = 0; index < plan.Visuals.Length; index++)
        {
            var visual = plan.Visuals[index];
            var resource = visual.Source.MeshResource;
            progress.ReportItem("Skeleton sources", visual.MeshReference, index, plan.Visuals.Length, 0.05f, 0.20f);
            var skeletalAsset = resolver.ResolveAssetNamed(resource.PackagePath, resource.ObjectName)!;
            var visualTextureReferences = visual.TextureReferences
                .Concat(skeletalAsset.SurfaceResourceReferences)
                .Distinct()
                .ToArray();
            visuals[index] = new CreatureVisualSource(
                visual,
                skeletalAsset,
                visualTextureReferences,
                visualTextureReferences.Select(MaterialResourceRequest.Opaque).ToArray());
        }
        var textureReferences = visuals
            .SelectMany(x => x.TextureReferences)
            .Distinct()
            .ToArray();
        return new CreatureVisualSourceSet(
            plan,
            visuals,
            textureReferences,
            textureReferences.Select(MaterialResourceRequest.Opaque).ToArray());
    }
}
