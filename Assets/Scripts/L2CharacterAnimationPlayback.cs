using System;
using System.Linq;
using UnityEngine;

internal static class L2CharacterAnimationPlayback
{
    public static bool Play(L2ModularCharacterArchetypeAssetBase archetype, Animator animator, string sequenceName)
    {
        if (!Application.isPlaying || archetype?.BaseAsset == null || animator == null || string.IsNullOrWhiteSpace(sequenceName))
        {
            return false;
        }

        var sequence = archetype.BaseAsset.AnimationSequences?
            .FirstOrDefault(x => string.Equals(x?.Name, sequenceName, StringComparison.OrdinalIgnoreCase));
        if (sequence == null)
        {
            return false;
        }

        var stateName = ResolveStateName(archetype, animator, sequence);
        if (stateName == null)
        {
            throw new InvalidOperationException($"Animator state for sequence '{sequence.Name}' was not imported.");
        }

        animator.Play(stateName, 0, 0f);
        animator.Update(0f);
        return true;
    }

    public static bool IsFinished(Animator animator)
    {
        return animator != null && animator.GetCurrentAnimatorStateInfo(0).normalizedTime >= 1f;
    }

    private static string ResolveStateName(
        L2ModularCharacterArchetypeAssetBase archetype,
        Animator animator,
        L2SkeletalAnimationSequenceData sequence)
    {
        var sanitizedName = SanitizeName(sequence.Name);
        var candidates = new[]
        {
            sequence.Name,
            $"Base Layer.{sequence.Name}",
            $"{archetype.BaseAsset.CharacterName}_{sanitizedName}",
            $"Base Layer.{archetype.BaseAsset.CharacterName}_{sanitizedName}"
        };
        return candidates.FirstOrDefault(x => animator.HasState(0, Animator.StringToHash(x)));
    }

    private static string SanitizeName(string value)
    {
        return new string(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_').ToArray());
    }

}
