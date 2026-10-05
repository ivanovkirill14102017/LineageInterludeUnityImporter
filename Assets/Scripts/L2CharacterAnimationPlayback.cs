using System;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

internal sealed class L2CharacterAnimationPlayback
{
    private readonly MonoBehaviour _owner;

    public L2CharacterAnimationPlayback(MonoBehaviour owner)
    {
        _owner = owner;
    }

    public bool Play(L2ModularCharacterArchetypeAssetBase archetype, Animator animator, string sequenceName)
    {
        if (archetype?.BaseAsset == null || animator == null || string.IsNullOrWhiteSpace(sequenceName))
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

#if UNITY_EDITOR
        if (!Application.isPlaying && TrySampleEditorPreview(archetype, stateName, sequence, 0f))
        {
            return true;
        }
#endif

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

#if UNITY_EDITOR
    public void SampleInEditor(
        L2ModularCharacterArchetypeAssetBase archetype,
        Animator animator,
        L2SkeletalAnimationSequenceData sequence,
        float time)
    {
        if (archetype == null || animator == null || sequence == null)
        {
            return;
        }

        var stateName = ResolveStateName(archetype, animator, sequence);
        if (stateName != null)
        {
            TrySampleEditorPreview(archetype, stateName, sequence, time);
        }
    }

    private bool TrySampleEditorPreview(
        L2ModularCharacterArchetypeAssetBase archetype,
        string stateName,
        L2SkeletalAnimationSequenceData sequence,
        float time)
    {
        var controller = archetype.AnimatorController as AnimatorController;
        if (controller == null)
        {
            return false;
        }

        var shortStateName = stateName.StartsWith("Base Layer.", StringComparison.Ordinal)
            ? stateName.Substring("Base Layer.".Length)
            : stateName;
        AnimationClip clip = null;
        foreach (var layer in controller.layers)
        {
            clip = FindClip(layer.stateMachine, shortStateName);
            if (clip != null)
            {
                break;
            }
        }

        clip ??= controller.animationClips.FirstOrDefault(x =>
            x != null && x.name.EndsWith($"_{SanitizeName(sequence.Name)}", StringComparison.OrdinalIgnoreCase));
        if (clip == null)
        {
            return false;
        }

        if (!AnimationMode.InAnimationMode())
        {
            AnimationMode.StartAnimationMode();
        }

        AnimationMode.BeginSampling();
        try
        {
            AnimationMode.SampleAnimationClip(_owner.gameObject, clip, Mathf.Clamp(time, 0f, clip.length));
        }
        finally
        {
            AnimationMode.EndSampling();
        }

        SceneView.RepaintAll();
        EditorApplication.QueuePlayerLoopUpdate();
        return true;
    }

    private static AnimationClip FindClip(AnimatorStateMachine machine, string stateName)
    {
        if (machine == null)
        {
            return null;
        }

        foreach (var child in machine.states)
        {
            if (string.Equals(child.state?.name, stateName, StringComparison.Ordinal))
            {
                return child.state.motion as AnimationClip;
            }
        }

        foreach (var child in machine.stateMachines)
        {
            var clip = FindClip(child.stateMachine, stateName);
            if (clip != null)
            {
                return clip;
            }
        }

        return null;
    }
#endif
}
