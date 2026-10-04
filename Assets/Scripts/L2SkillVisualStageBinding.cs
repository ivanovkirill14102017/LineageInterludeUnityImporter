using System;
using UnityEngine;

public enum L2SkillVisualStagePlaybackRole
{
    Auto = 0,
    Caster = 1,
    Projectile = 2,
    Target = 3,
    Impact = 4,
    Persistent = 5
}

[Serializable]
public sealed class L2SkillVisualStageBinding
{
    public int StageOrder;
    public string StageName;
    public L2SkillVisualStagePlaybackRole Role = L2SkillVisualStagePlaybackRole.Auto;
    public L2SkillVisualPlacementData Placement;
    public GameObject StageRoot;

    public string GetDisplayName(int fallbackIndex)
    {
        var name = string.IsNullOrWhiteSpace(StageName)
            ? StageRoot != null ? StageRoot.name : $"Stage {fallbackIndex}"
            : StageName;
        return $"{StageOrder:D2} {Role} - {name}";
    }

    public static L2SkillVisualStagePlaybackRole ResolveRole(bool isProjectile, L2SkillVisualPlacementData placement, string stageName)
    {
        if (placement == null)
        {
            throw new NotSupportedException($"Skill stage '{stageName}' has no explicit SkillAction placement.");
        }

        return isProjectile
            ? L2SkillVisualStagePlaybackRole.Projectile
            : placement.SpawnOnTarget
                ? L2SkillVisualStagePlaybackRole.Target
                : L2SkillVisualStagePlaybackRole.Caster;
    }
}
