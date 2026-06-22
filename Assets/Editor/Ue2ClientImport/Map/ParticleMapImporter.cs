using System;
using System.Linq;
using System.Threading.Tasks;
using L2Viewer.SceneDomain.Services;
using UnityEngine;

internal static class ParticleMapImporter
{
    public static Task ImportAsync(MapImportRequest request, Ue2MapSource source, Action<string> log)
    {
        log("[Particles] START Build particle data");
        var particleBuilder = new SceneParticleBuilder();
        var emitters = particleBuilder.BuildEmitters(source.UnrFile);
        log("[Particles] DONE Build particle data");
        log($"[Particles] Builder produced emitter roots={emitters.Length}.");

        var spriteLayerCount = emitters.Sum(x => x.Layers.Length);
        var meshLayerCount = emitters.Sum(x => x.MeshLayers.Length);
        var beamLayerCount = emitters.Sum(x => x.BeamLayers.Length);
        var vertMeshLayerCount = emitters.Sum(x => x.VertMeshLayers.Length);
        var totalReferencedLayers = emitters.Sum(x => x.EmitterReferences?.Length ?? 0);
        var resolvedLayers = spriteLayerCount + meshLayerCount + beamLayerCount + vertMeshLayerCount;
        if (emitters.Length == 0 || (spriteLayerCount + meshLayerCount + beamLayerCount + vertMeshLayerCount) == 0)
        {
            log($"No particle emitters with supported domain layers were found. referencedLayers={totalReferencedLayers}, resolvedLayers={resolvedLayers}.");
            return Task.CompletedTask;
        }

        log($"Found particle roots={emitters.Length}, referencedLayers={totalReferencedLayers}, resolvedLayers={resolvedLayers}, spriteLayers={spriteLayerCount}, meshLayers={meshLayerCount}, beamLayers={beamLayerCount}, vertMeshLayers={vertMeshLayerCount}.");

        log("[Particles] START Scene root preparation");
        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        var particlesRootName = $"{request.ObjectName}_Particles";
        UnitySceneObjectUtility.RemoveExistingObject(particlesRootName);
        MapImportAssetPreparation.EnsureMapOutputFolderExists(request.OutputDir);

        var particlesRoot = new GameObject(particlesRootName);
        particlesRoot.transform.SetParent(mapRoot.transform, false);
        log("[Particles] DONE Scene root preparation");

        log("[Particles] START Build particle objects");
        L2ParticleAssetBuilder.BuildParticles(emitters, source.ClientPath, request.OutputDir, particlesRoot, log);
        log("[Particles] DONE Build particle objects");

        log("[Particles] START Finalize");
        MapImportFinalizer.Complete(mapRoot, log);
        log("[Particles] DONE Finalize");
        log("Particle import finished.");
        return Task.CompletedTask;
    }
}
