using System;
using System.Linq;
using UnityEngine;

internal static class L2SkillVisualTimeline
{
    public static float ResolveHitTime(L2SkillVisualAsset skill, string visualReference)
    {
        var level = skill?.Levels?
            .Where(x => x != null && string.Equals(x.DescriptionToken, visualReference, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.SkillLevel)
            .FirstOrDefault();
        if (level == null || level.HitTime < 0f || float.IsNaN(level.HitTime) || float.IsInfinity(level.HitTime))
        {
            throw new InvalidOperationException($"Skill visual '{visualReference}' has no valid HitTime in skillgrp.dat.");
        }

        return level.HitTime;
    }

    public static void ValidateStandalonePhase(L2SkillVisualPhase phase)
    {
        if (phase == L2SkillVisualPhase.Channeling || phase == L2SkillVisualPhase.Preshot)
        {
            throw new NotSupportedException($"Standalone timing for {phase} requires an explicit event time; skill.usk does not provide one.");
        }
    }

    public static void ValidateStandaloneTimeline(L2SkillVisualAsset skill, L2SkillVisualStageBinding[] stageBindings, int startIndex)
    {
        var bindings = stageBindings ?? Array.Empty<L2SkillVisualStageBinding>();
        string visualReference = null;
        var sawCasting = false;
        for (var i = Mathf.Clamp(startIndex, 0, bindings.Length); i < bindings.Length; i++)
        {
            var placement = bindings[i]?.Placement;
            if (placement == null)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(placement.VisualReference))
            {
                throw new InvalidOperationException($"Stage '{bindings[i].StageName}' has no visual reference. Reimport the skill visual.");
            }

            if (!string.Equals(visualReference, placement.VisualReference, StringComparison.OrdinalIgnoreCase))
            {
                visualReference = placement.VisualReference;
                sawCasting = false;
            }

            ValidateStandalonePhase(placement.Phase);
            if (placement.Phase == L2SkillVisualPhase.Casting)
            {
                sawCasting = true;
            }
            else if (placement.Phase == L2SkillVisualPhase.Shot && sawCasting)
            {
                ResolveHitTime(skill, visualReference);
            }
        }
    }

    public static int FindPhaseEnd(L2SkillVisualStageBinding[] bindings, int start)
    {
        var placement = bindings[start].Placement ?? throw new InvalidOperationException($"Stage '{bindings[start].StageName}' has no placement.");
        if (string.IsNullOrWhiteSpace(placement.VisualReference) || !Enum.IsDefined(typeof(L2SkillVisualPhase), placement.Phase))
        {
            throw new InvalidOperationException($"Stage '{bindings[start].StageName}' has no visual reference or action phase. Reimport the skill visual.");
        }

        var end = start + 1;
        while (end < bindings.Length && bindings[end]?.Placement != null &&
               string.Equals(bindings[end].Placement.VisualReference, placement.VisualReference, StringComparison.OrdinalIgnoreCase) &&
               bindings[end].Placement.Phase == placement.Phase)
        {
            end++;
        }

        return end;
    }

    public static int[] OrderPhaseActions(L2SkillVisualStageBinding[] bindings, int start, int end)
    {
        return Enumerable.Range(start, end - start)
            .OrderBy(index => Mathf.Max(0f, bindings[index].Placement.SpawnDelay))
            .ThenBy(index => index)
            .ToArray();
    }
}
