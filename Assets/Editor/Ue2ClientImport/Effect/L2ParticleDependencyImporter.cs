using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Services.MaterialServices;
using L2Viewer.SceneDomain.Services.Utility;
using L2Viewer.UnrFile;

internal static class L2ParticleDependencyImporter
{
    public static void EnsureMeshDependencies(
        IReadOnlyList<SceneParticleEmitterData> emitters,
        string clientRoot,
        string sourcePackagePath,
        string outputKey,
        bool reuseExistingAssets,
        Action<string> log,
        MapImportExecutionContext context = null)
    {
        var references = (emitters ?? Array.Empty<SceneParticleEmitterData>())
            .Where(x => x != null)
            .SelectMany(x => x.MeshLayers ?? Array.Empty<SceneMeshEmitterLayerData>())
            .Select(x => x.StaticMeshReference)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(TryParseStaticMeshReference)
            .Where(x => x != null)
            .ToArray();
        if (references.Length == 0)
        {
            return;
        }

        try
        {
            var resolver = new SceneStaticMeshResolver(clientRoot, new BspTextureManager(clientRoot));
            var definitions = resolver.ResolveMany(sourcePackagePath, references);
            if (definitions.Count == 0)
            {
                return;
            }

            context?.ThrowIfCancellationRequested();
            L2StaticMeshAssetBuilder.EnsureStaticMeshPrefabs(
                definitions,
                clientRoot,
                outputKey,
                log,
                reuseExistingAssets,
                context);
        }
        catch (Exception exception)
        {
            log?.Invoke($"[Particles/MeshEmitter] Failed to resolve dependent mesh assets: {exception.Message}");
        }
    }

    private static UnrFileObjectReference TryParseStaticMeshReference(string meshReference)
    {
        try
        {
            var parsed = SceneReferenceUtilities.ParseFromDbResourceReference(meshReference);
            return new UnrFileObjectReference
            {
                RawReference = 0,
                Kind = UnrFileReferenceKind.Import,
                ClassName = "StaticMesh",
                ObjectName = parsed.ObjectName,
                PackageName = parsed.PackageName
            };
        }
        catch
        {
            return null;
        }
    }
}
