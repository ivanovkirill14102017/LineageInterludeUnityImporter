using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    public static void ImportAsync(
        MapImportRequest request,
        Ue2MapSource source,
        Action<string> log,
        MapImportExecutionContext context = null,
        bool finalizeScene = true)
    {
        context?.Report("Particles", "Build emitter data", 0.18f);
        var buildStopwatch = Stopwatch.StartNew();
        var particleBuilder = new SceneParticleBuilder();
        var emitters = particleBuilder.BuildEmitters(source.UnrFile);
        buildStopwatch.Stop();
        log($"[Particles] Build emitter data took {buildStopwatch.Elapsed.TotalSeconds:F2}s");

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

        context?.ThrowIfCancellationRequested();
        context?.Report("Particles", "Resolve mesh dependencies", 0.42f);
        var dependencyStopwatch = Stopwatch.StartNew();
        L2ParticleDependencyImporter.EnsureMeshDependencies(
            emitters,
            source.ClientPath,
            source.UnrFile.FilePath,
            request.MapKey,
            request.ReuseExistingMaterialTextureAssets,
            log,
            context);
        dependencyStopwatch.Stop();
        log($"[Particles] Resolve mesh dependencies took {dependencyStopwatch.Elapsed.TotalSeconds:F2}s");

        context?.Report("Particles", "Scene root preparation", 0.58f);
        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        var particlesRootName = $"{request.ObjectName}_Particles";
        if (UnitySceneObjectUtility.ObjectExists(particlesRootName))
        {
            log($"[Particles] Skipping import because '{particlesRootName}' already exists.");
            return;
        }

        MapImportAssetPreparation.EnsureMapOutputFolderExists(request.OutputDir);

        var particlesRoot = new GameObject(particlesRootName);
        particlesRoot.transform.SetParent(mapRoot.transform, false);
        context?.ThrowIfCancellationRequested();
        context?.Report("Particles", "Build particle objects", 0.78f);
        var objectStopwatch = Stopwatch.StartNew();
        L2ParticleAssetBuilder.BuildParticles(emitters, source.ClientPath, request.OutputDir, particlesRoot, log);
        objectStopwatch.Stop();
        log($"[Particles] Build particle objects took {objectStopwatch.Elapsed.TotalSeconds:F2}s");

        if (finalizeScene)
        {
            context?.ThrowIfCancellationRequested();
            context?.Report("Particles", "Finalize", 0.96f);
            var finalizeStopwatch = Stopwatch.StartNew();
            MapImportFinalizer.Complete(mapRoot, log);
            finalizeStopwatch.Stop();
            log($"[Particles] Finalize took {finalizeStopwatch.Elapsed.TotalSeconds:F2}s");
        }
    }

}
