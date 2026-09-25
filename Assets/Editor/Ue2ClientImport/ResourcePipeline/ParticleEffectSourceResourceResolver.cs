using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.SceneDomain.Models;

internal sealed class ParticleEffectSourceResourceSet
{
    public ParticleEffectSourceResourceSet(
        ParticleEffectSourceResource[] effects,
        SceneSurfaceResourceReference[] textureReferences,
        SceneStaticMeshResourceReference[] staticMeshReferences,
        SceneVertexMeshResourceReference[] vertexMeshReferences,
        SceneParticleResourceReference[] nestedEmitterReferences)
    {
        Effects = effects;
        TextureReferences = textureReferences;
        StaticMeshReferences = staticMeshReferences;
        VertexMeshReferences = vertexMeshReferences;
        NestedEmitterReferences = nestedEmitterReferences;
    }

    public ParticleEffectSourceResource[] Effects { get; }
    public SceneSurfaceResourceReference[] TextureReferences { get; }
    public SceneStaticMeshResourceReference[] StaticMeshReferences { get; }
    public SceneVertexMeshResourceReference[] VertexMeshReferences { get; }
    public SceneParticleResourceReference[] NestedEmitterReferences { get; }

}

internal sealed class ParticleEffectSourceResource
{
    public ParticleEffectSourceResource(
        SceneParticleResourceReference reference,
        SceneCreatureAttachedEffectData source)
    {
        Reference = reference;
        Source = source;
    }

    public SceneParticleResourceReference Reference { get; }
    public SceneCreatureAttachedEffectData Source { get; }
}

internal static class ParticleEffectSourceResourceResolver
{
    public static ParticleEffectSourceResourceSet Resolve(
        IEnumerable<SceneCreatureAttachedEffectData> attachedEffects)
    {
        var effects = attachedEffects
            .Select(x => new ParticleEffectSourceResource(
                x.ParticleReference,
                x))
            .GroupBy(x => x.Reference)
            .Select(x => x.First())
            .ToArray();
        var emitters = effects.Select(x => x.Source.Emitter).ToArray();
        var textureReferences = CollectSurfaceReferences(emitters).Distinct().ToArray();

        return new ParticleEffectSourceResourceSet(
            effects,
            textureReferences,
            CollectStaticMeshReferences(emitters).Distinct().ToArray(),
            CollectVertexMeshReferences(emitters).Distinct().ToArray(),
            CollectNestedEmitterReferences(emitters).Distinct().ToArray());
    }

    private static IEnumerable<SceneSurfaceResourceReference> CollectSurfaceReferences(
        IEnumerable<SceneParticleEmitterData> emitters)
    {
        foreach (var emitter in emitters)
        {
            foreach (var layer in emitter.Layers ?? Array.Empty<SceneSpriteEmitterLayerData>())
            {
                if (layer.SurfaceResourceReference != null)
                {
                    yield return layer.SurfaceResourceReference;
                }
            }

            foreach (var layer in emitter.BeamLayers ?? Array.Empty<SceneBeamEmitterLayerData>())
            {
                if (layer.SurfaceResourceReference != null)
                {
                    yield return layer.SurfaceResourceReference;
                }
            }
        }
    }

    private static IEnumerable<SceneStaticMeshResourceReference> CollectStaticMeshReferences(
        IEnumerable<SceneParticleEmitterData> emitters)
    {
        foreach (var reference in emitters
                     .SelectMany(x => x.MeshLayers ?? Array.Empty<SceneMeshEmitterLayerData>())
                     .Select(x => x.MeshResourceReference))
        {
            if (reference != null)
            {
                yield return reference;
            }
        }
    }

    private static IEnumerable<SceneVertexMeshResourceReference> CollectVertexMeshReferences(
        IEnumerable<SceneParticleEmitterData> emitters)
    {
        foreach (var reference in emitters
                     .SelectMany(x => x.VertMeshLayers ?? Array.Empty<SceneVertMeshEmitterLayerData>())
                     .Select(x => x.MeshResourceReference))
        {
            if (reference != null)
            {
                yield return reference;
            }
        }
    }

    private static IEnumerable<SceneParticleResourceReference> CollectNestedEmitterReferences(
        IEnumerable<SceneParticleEmitterData> emitters)
    {
        foreach (var reference in emitters.SelectMany(x => x.ParticleResourceReferences))
        {
            yield return reference;
        }
    }
}
