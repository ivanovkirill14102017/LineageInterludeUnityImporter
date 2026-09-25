using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using UnityEngine;

internal static class CreatureAttachedEffectPrefabBuilder
{
    public static void Build(
        GameObject prefabRoot,
        IReadOnlyList<Transform> bones,
        IReadOnlyList<SceneCreatureAttachedEffectData> effects,
        string clientRoot,
        string assetRoot,
        string outputKey,
        Action<string> log,
        MapImportExecutionContext context = null)
    {
        if (effects == null || effects.Count == 0)
        {
            return;
        }

        foreach (var packageGroup in effects.GroupBy(x => x.EffectResource.PackagePath, StringComparer.OrdinalIgnoreCase))
        {
            L2ParticleDependencyImporter.EnsureMeshDependencies(
                packageGroup.Select(x => x.Emitter).ToArray(),
                clientRoot,
                packageGroup.Key,
                outputKey,
                reuseExistingAssets: true,
                log,
                context);
        }

        foreach (var effect in effects)
        {
            context?.ThrowIfCancellationRequested();
            var bone = ResolveBone(bones, effect);
            if (bone == null)
            {
                throw new InvalidOperationException(
                    $"Attached effect '{effect.EffectReference}' cannot resolve bone name '{effect.BoneName ?? "<null>"}' index '{effect.BoneIndex?.ToString() ?? "<null>"}'.");
            }

            var attachment = new GameObject(effect.StableName);
            attachment.transform.SetParent(bone, false);
            attachment.transform.localPosition = effect.RelativeLocationUnreal.TransformFromUnrealToUnityWithScale();
            attachment.transform.localRotation = effect.RelativeRotationEulerDegrees.ToEulerAngles();
            L2ParticleAssetBuilder.BuildParticles(
                new[] { effect.Emitter },
                clientRoot,
                assetRoot,
                attachment,
                log);
        }

        log?.Invoke($"[Creatures/Effects] Attached {effects.Count} emitter instances to skeletal bones.");
    }

    private static Transform ResolveBone(IReadOnlyList<Transform> bones, SceneCreatureAttachedEffectData effect)
    {
        if (!string.IsNullOrWhiteSpace(effect.BoneName))
        {
            var byName = bones.FirstOrDefault(x =>
                x != null && string.Equals(x.name, effect.BoneName, StringComparison.OrdinalIgnoreCase));
            if (byName != null)
            {
                return byName;
            }
        }

        return effect.BoneIndex.HasValue && effect.BoneIndex.Value >= 0 && effect.BoneIndex.Value < bones.Count
            ? bones[effect.BoneIndex.Value]
            : null;
    }
}
