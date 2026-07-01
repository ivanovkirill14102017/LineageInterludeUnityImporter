using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Services.MaterialServices;
using L2Viewer.SceneDomain.Services.Utility;
using L2Viewer.UnrFile;
using UnityEngine;

internal static class ParticleMapImporter
{
    public static void ImportAsync(MapImportRequest request, Ue2MapSource source, Action<string> log)
    {
        var particleBuilder = new SceneParticleBuilder();
        var emitters = particleBuilder.BuildEmitters(source.UnrFile);

        var spriteLayerCount = emitters.Sum(x => x.Layers.Length);
        var meshLayerCount = emitters.Sum(x => x.MeshLayers.Length);
        var beamLayerCount = emitters.Sum(x => x.BeamLayers.Length);
        var vertMeshLayerCount = emitters.Sum(x => x.VertMeshLayers.Length);
        var totalReferencedLayers = emitters.Sum(x => x.EmitterReferences?.Length ?? 0);
        var resolvedLayers = spriteLayerCount + meshLayerCount + beamLayerCount + vertMeshLayerCount;
        if (emitters.Length == 0 || (spriteLayerCount + meshLayerCount + beamLayerCount + vertMeshLayerCount) == 0)
        {
            log($"No particle emitters with supported domain layers were found. referencedLayers={totalReferencedLayers}, resolvedLayers={resolvedLayers}.");
            return;
        }

        EnsureParticleMeshDependencies(request, source, emitters, log);

        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        var particlesRootName = $"{request.ObjectName}_Particles";
        UnitySceneObjectUtility.RemoveExistingObject(particlesRootName);
        MapImportAssetPreparation.EnsureMapOutputFolderExists(request.OutputDir);

        var particlesRoot = new GameObject(particlesRootName);
        particlesRoot.transform.SetParent(mapRoot.transform, false);
        L2ParticleAssetBuilder.BuildParticles(emitters, source.ClientPath, request.OutputDir, particlesRoot, log);

        MapImportFinalizer.Complete(mapRoot, log);
    }

    private static void EnsureParticleMeshDependencies(
        MapImportRequest request,
        Ue2MapSource source,
        SceneParticleEmitterData[] emitters,
        Action<string> log)
    {
        var meshReferences = emitters
            .SelectMany(x => x.MeshLayers)
            .Select(x => x.StaticMeshReference)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (meshReferences.Length == 0)
        {
            return;
        }

        var textureManager = new BspTextureManager(source.ClientPath);
        var meshResolver = new SceneStaticMeshResolver(source.ClientPath, textureManager);
        var unresolvedReferences = meshReferences
            .Select(TryParseStaticMeshReference)
            .Where(x => x is not null)
            .Cast<UnrFileObjectReference>()
            .ToArray();
        if (unresolvedReferences.Length == 0)
        {
            return;
        }

        IReadOnlyDictionary<string, SceneStaticMeshDefinition> resolvedDefinitions;
        try
        {
            resolvedDefinitions = meshResolver.ResolveMany(source.UnrFile.FilePath, unresolvedReferences);
        }
        catch (Exception ex)
        {
            log($"[Particles/MeshEmitter] Failed to resolve dependent mesh assets: {ex.Message}");
            return;
        }

        if (resolvedDefinitions.Count == 0)
        {
            return;
        }

        L2StaticMeshAssetBuilder.EnsureStaticMeshPrefabs(
            resolvedDefinitions,
            source.ClientPath,
            request.MapKey,
            log,
            request.ReuseExistingMaterialTextureAssets);
    }

    private static UnrFileObjectReference? TryParseStaticMeshReference(string? meshReference)
    {
        if (string.IsNullOrWhiteSpace(meshReference))
        {
            return null;
        }

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
