using System;
using System.Linq;
using System.Threading.Tasks;
using L2Viewer.SceneDomain.Services;
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

        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        var particlesRootName = $"{request.ObjectName}_Particles";
        UnitySceneObjectUtility.RemoveExistingObject(particlesRootName);
        MapImportAssetPreparation.EnsureMapOutputFolderExists(request.OutputDir);

        var particlesRoot = new GameObject(particlesRootName);
        particlesRoot.transform.SetParent(mapRoot.transform, false);
        L2ParticleAssetBuilder.BuildParticles(emitters, source.ClientPath, request.OutputDir, particlesRoot, log);

        MapImportFinalizer.Complete(mapRoot, log);
    }
}
