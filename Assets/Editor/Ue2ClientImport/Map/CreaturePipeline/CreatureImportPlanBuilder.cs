using System;
using System.Linq;
using L2Viewer.SceneDomain.Models;

internal static class CreatureImportPlanBuilder
{
    public static CreatureImportPlan Build(SceneCreatureSpawnData[] spawns)
    {
        var visuals = spawns
            .GroupBy(BuildVisualKey, StringComparer.OrdinalIgnoreCase)
            .Select(x => BuildVisualPlan(x.Key, x.First()))
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var visualsByKey = visuals.ToDictionary(x => x.Key, StringComparer.OrdinalIgnoreCase);
        var compositions = spawns
            .GroupBy(BuildCompositionKey, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var groupedSpawns = group.ToArray();
                return new CreatureCompositionImportPlan(
                    group.Key,
                    visualsByKey[BuildVisualKey(groupedSpawns[0])],
                    groupedSpawns[0],
                    groupedSpawns);
            })
            .OrderBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new CreatureImportPlan(spawns, visuals, compositions);
    }

    public static string BuildVisualKey(SceneCreatureSpawnData spawn)
    {
        return spawn.VisualKey;
    }

    public static string BuildCompositionKey(SceneCreatureSpawnData spawn)
    {
        var effects = string.Join(",", (spawn.AttachedEffects ?? Array.Empty<SceneCreatureAttachedEffectData>())
            .Select(x => $"{x.EffectReference}@{x.BoneName}:{x.BoneIndex}"));
        return $"{BuildVisualKey(spawn)}|rhand={spawn.RightHandItemId}|lhand={spawn.LeftHandItemId}|effects={effects}";
    }

    private static CreatureVisualImportPlan BuildVisualPlan(string key, SceneCreatureSpawnData spawn)
    {
        return new CreatureVisualImportPlan(
            key,
            spawn.SkeletalMeshReference,
            spawn.SurfaceResourceReferences.Distinct().ToArray(),
            spawn);
    }
}
