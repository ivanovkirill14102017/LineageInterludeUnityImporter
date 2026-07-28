using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;

internal static class CreatureAnimatorControllerBuilder
{
    private const string CombatModeParameter = "CombatMode";
    private const string SkillModeParameter = "SkillMode";

    public static AnimatorController Build(L2SkeletalCharacterAsset asset, string referenceText, string prefabRoot, CreatureAnimationClipBuilder.ClipBuildInfo[] clips, Action<string> log, out string notes)
    {
        if (clips == null || clips.Length == 0)
        {
            notes = "AnimatorController was not created because no clips were baked.";
            return null;
        }

        var controllerPath = L2AssetManager.BuildClientPackageAssetPath(
            $"{prefabRoot}/Controllers",
            referenceText,
            "AC",
            "controller",
            "AnimatorControllers");

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            L2AssetManager.EnsureParentFolderExists(controllerPath);
            controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
        }

        var layer = controller.layers.Length > 0 ? controller.layers[0] : new AnimatorControllerLayer
        {
            name = "Base Layer",
            stateMachine = new AnimatorStateMachine()
        };

        if (layer.stateMachine == null)
        {
            layer.stateMachine = new AnimatorStateMachine();
        }

        ResetControllerParameters(controller);
        EnsureCoreParameters(controller);

        foreach (var childState in layer.stateMachine.states)
        {
            if (childState.state != null)
            {
                layer.stateMachine.RemoveState(childState.state);
            }
        }

        var statesBySequenceName = new Dictionary<string, AnimatorState>(StringComparer.OrdinalIgnoreCase);
        AnimatorState defaultState = null;
        foreach (var clipInfo in clips.Where(x => x.Clip != null))
        {
            var state = layer.stateMachine.AddState(clipInfo.Clip.name);
            state.motion = clipInfo.Clip;
            state.writeDefaultValues = true;

            var sequence = FindSequenceForClip(asset, clipInfo.Clip);
            if (sequence != null)
            {
                if (!string.IsNullOrWhiteSpace(sequence.Name))
                {
                    statesBySequenceName[sequence.Name] = state;
                }
            }

            if (defaultState == null)
            {
                defaultState = state;
            }

            if (CreatureSkeletalImportUtility.IsPreferredDefaultStateName(clipInfo.Clip.name))
            {
                defaultState = state;
            }
        }

        CreateSemanticTransitions(controller, asset, layer.stateMachine, statesBySequenceName);
        defaultState = ResolveDefaultState(asset, statesBySequenceName) ?? defaultState;
        layer.stateMachine.defaultState = defaultState;
        controller.layers = new[] { layer };
        EditorUtility.SetDirty(layer.stateMachine);
        EditorUtility.SetDirty(controller);

        notes = defaultState != null
            ? $"AnimatorController created with {clips.Length} state(s); default state is '{defaultState.name}'."
            : "AnimatorController created without a default state.";
        log?.Invoke($"[SkinnedPOC] AnimatorController updated: {controllerPath}");
        return controller;
    }

    private static void CreateSemanticTransitions(
        AnimatorController controller,
        L2SkeletalCharacterAsset asset,
        AnimatorStateMachine stateMachine,
        IReadOnlyDictionary<string, AnimatorState> statesBySequenceName)
    {
        if (asset?.AnimationSequences == null)
        {
            return;
        }

        CreateModeTransitions(asset, statesBySequenceName);

        foreach (var sequence in asset.AnimationSequences)
        {
            if (sequence == null ||
                string.IsNullOrWhiteSpace(sequence.Name) ||
                !statesBySequenceName.TryGetValue(sequence.Name, out var sourceState))
            {
                continue;
            }

            if (sequence.IsOneShot)
            {
                AddAnyStateTriggerTransition(controller, stateMachine, sourceState, sequence.Name);
            }

            if (!sequence.IsOneShot)
            {
                continue;
            }

            foreach (var nextName in sequence.SuggestedNextSequenceNames ?? Array.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(nextName) || !statesBySequenceName.TryGetValue(nextName, out var targetState))
                {
                    continue;
                }

                if (sourceState.transitions.Any(x => x.destinationState == targetState))
                {
                    continue;
                }

                var transition = sourceState.AddTransition(targetState);
                transition.hasExitTime = true;
                transition.exitTime = 0.98f;
                transition.duration = 0.05f;
                transition.hasFixedDuration = true;
                transition.canTransitionToSelf = false;
            }
        }

        CreateSkillTriggerTransitions(controller, stateMachine, asset, statesBySequenceName);
    }

    private static AnimatorState ResolveDefaultState(
        L2SkeletalCharacterAsset asset,
        IReadOnlyDictionary<string, AnimatorState> statesBySequenceName)
    {
        foreach (var name in EnumeratePreferredDefaultSequenceNames(asset))
        {
            if (!string.IsNullOrWhiteSpace(name) && statesBySequenceName.TryGetValue(name, out var state))
            {
                return state;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumeratePreferredDefaultSequenceNames(L2SkeletalCharacterAsset asset)
    {
        foreach (var profile in asset?.RoutingProfiles ?? Array.Empty<L2SkeletalAnimationRoutingProfileData>())
        {
            foreach (var candidate in profile.SuggestedDefaultSequenceNames ?? Array.Empty<string>())
            {
                yield return candidate;
            }

            foreach (var candidate in profile.SuggestedCombatIdleSequenceNames ?? Array.Empty<string>())
            {
                yield return candidate;
            }

            foreach (var candidate in profile.SuggestedSkillIdleSequenceNames ?? Array.Empty<string>())
            {
                yield return candidate;
            }
        }

        foreach (var sequence in asset?.AnimationSequences ?? Array.Empty<L2SkeletalAnimationSequenceData>())
        {
            if (sequence != null && sequence.SuggestedLoop && !sequence.IsOneShot)
            {
                yield return sequence.Name;
            }
        }
    }

    private static L2SkeletalAnimationSequenceData FindSequenceForClip(L2SkeletalCharacterAsset asset, UnityEngine.AnimationClip clip)
    {
        if (asset?.AnimationSequences == null || clip == null)
        {
            return null;
        }

        return asset.AnimationSequences.FirstOrDefault(sequence =>
            sequence != null &&
            clip.name.EndsWith(CreatureSkeletalImportUtility.SanitizeName(sequence.Name), StringComparison.OrdinalIgnoreCase));
    }

    private static void ResetControllerParameters(AnimatorController controller)
    {
        foreach (var parameter in controller.parameters.ToArray())
        {
            controller.RemoveParameter(parameter);
        }
    }

    private static void EnsureCoreParameters(AnimatorController controller)
    {
        controller.AddParameter(CombatModeParameter, AnimatorControllerParameterType.Bool);
        controller.AddParameter(SkillModeParameter, AnimatorControllerParameterType.Bool);
    }

    private static void CreateModeTransitions(
        L2SkeletalCharacterAsset asset,
        IReadOnlyDictionary<string, AnimatorState> statesBySequenceName)
    {
        var idleStates = ResolveStates(statesBySequenceName, EnumerateIdleCandidates(asset)).ToArray();
        var combatStates = ResolveStates(statesBySequenceName, EnumerateCombatIdleCandidates(asset)).ToArray();
        var skillStates = ResolveStates(statesBySequenceName, EnumerateSkillIdleCandidates(asset)).ToArray();

        foreach (var idleState in idleStates)
        {
            foreach (var combatState in combatStates)
            {
                AddBoolTransition(idleState, combatState, CombatModeParameter, true, SkillModeParameter, false);
            }

            foreach (var skillState in skillStates)
            {
                AddBoolTransition(idleState, skillState, SkillModeParameter, true);
            }
        }

        foreach (var combatState in combatStates)
        {
            foreach (var idleState in idleStates)
            {
                AddBoolTransition(combatState, idleState, CombatModeParameter, false, SkillModeParameter, false);
            }

            foreach (var skillState in skillStates)
            {
                AddBoolTransition(combatState, skillState, SkillModeParameter, true);
            }
        }

        foreach (var skillState in skillStates)
        {
            foreach (var combatState in combatStates)
            {
                AddBoolTransition(skillState, combatState, SkillModeParameter, false, CombatModeParameter, true);
            }

            foreach (var idleState in idleStates)
            {
                AddBoolTransition(skillState, idleState, SkillModeParameter, false, CombatModeParameter, false);
            }
        }
    }

    private static IEnumerable<string> EnumerateIdleCandidates(L2SkeletalCharacterAsset asset)
    {
        foreach (var profile in asset?.RoutingProfiles ?? Array.Empty<L2SkeletalAnimationRoutingProfileData>())
        {
            foreach (var candidate in profile.SuggestedDefaultSequenceNames ?? Array.Empty<string>())
            {
                yield return candidate;
            }
        }

        foreach (var sequence in asset?.AnimationSequences ?? Array.Empty<L2SkeletalAnimationSequenceData>())
        {
            if (sequence != null && string.Equals(sequence.Category, "idle", StringComparison.OrdinalIgnoreCase))
            {
                yield return sequence.Name;
            }
        }
    }

    private static IEnumerable<string> EnumerateCombatIdleCandidates(L2SkeletalCharacterAsset asset)
    {
        foreach (var profile in asset?.RoutingProfiles ?? Array.Empty<L2SkeletalAnimationRoutingProfileData>())
        {
            foreach (var candidate in profile.SuggestedCombatIdleSequenceNames ?? Array.Empty<string>())
            {
                yield return candidate;
            }
        }

        foreach (var sequence in asset?.AnimationSequences ?? Array.Empty<L2SkeletalAnimationSequenceData>())
        {
            if (sequence != null && string.Equals(sequence.Category, "combat_idle", StringComparison.OrdinalIgnoreCase))
            {
                yield return sequence.Name;
            }
        }
    }

    private static IEnumerable<string> EnumerateSkillIdleCandidates(L2SkeletalCharacterAsset asset)
    {
        foreach (var profile in asset?.RoutingProfiles ?? Array.Empty<L2SkeletalAnimationRoutingProfileData>())
        {
            foreach (var candidate in profile.SuggestedSkillIdleSequenceNames ?? Array.Empty<string>())
            {
                yield return candidate;
            }
        }

        foreach (var sequence in asset?.AnimationSequences ?? Array.Empty<L2SkeletalAnimationSequenceData>())
        {
            if (sequence != null && string.Equals(sequence.Category, "combat_skill_idle", StringComparison.OrdinalIgnoreCase))
            {
                yield return sequence.Name;
            }
        }
    }

    private static IEnumerable<AnimatorState> ResolveStates(
        IReadOnlyDictionary<string, AnimatorState> statesBySequenceName,
        IEnumerable<string> names)
    {
        var yielded = new HashSet<AnimatorState>();
        foreach (var name in names ?? Array.Empty<string>())
        {
            if (!string.IsNullOrWhiteSpace(name) &&
                statesBySequenceName.TryGetValue(name, out var state) &&
                yielded.Add(state))
            {
                yield return state;
            }
        }
    }

    private static void AddAnyStateTriggerTransition(
        AnimatorController controller,
        AnimatorStateMachine stateMachine,
        AnimatorState targetState,
        string sequenceName)
    {
        var parameterName = BuildSequenceTriggerParameterName(sequenceName);
        EnsureParameter(controller, parameterName, AnimatorControllerParameterType.Trigger);
        if (stateMachine.anyStateTransitions.Any(x => x.destinationState == targetState &&
                                                      x.conditions.Any(c => c.parameter == parameterName)))
        {
            return;
        }

        var transition = stateMachine.AddAnyStateTransition(targetState);
        transition.hasExitTime = false;
        transition.duration = 0.02f;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.If, 0f, parameterName);
    }

    private static void CreateSkillTriggerTransitions(
        AnimatorController controller,
        AnimatorStateMachine stateMachine,
        L2SkeletalCharacterAsset asset,
        IReadOnlyDictionary<string, AnimatorState> statesBySequenceName)
    {
        foreach (var profile in asset?.RoutingProfiles ?? Array.Empty<L2SkeletalAnimationRoutingProfileData>())
        {
            foreach (var trigger in profile.SkillTriggers ?? Array.Empty<L2SkeletalSkillAnimationTriggerData>())
            {
                if (trigger == null ||
                    trigger.SkillId <= 0 ||
                    string.IsNullOrWhiteSpace(trigger.SequenceName) ||
                    !statesBySequenceName.TryGetValue(trigger.SequenceName, out var targetState))
                {
                    continue;
                }

                var parameterName = BuildSkillTriggerParameterName(trigger.SkillId, trigger.SequenceName);
                EnsureParameter(controller, parameterName, AnimatorControllerParameterType.Trigger);
                if (stateMachine.anyStateTransitions.Any(x => x.destinationState == targetState &&
                                                              x.conditions.Any(c => c.parameter == parameterName)))
                {
                    continue;
                }

                var transition = stateMachine.AddAnyStateTransition(targetState);
                transition.hasExitTime = false;
                transition.duration = 0.02f;
                transition.hasFixedDuration = true;
                transition.canTransitionToSelf = false;
                transition.AddCondition(AnimatorConditionMode.If, 0f, parameterName);
            }
        }
    }

    private static void AddBoolTransition(
        AnimatorState fromState,
        AnimatorState toState,
        string firstParameter,
        bool firstValue,
        string secondParameter = null,
        bool secondValue = false)
    {
        if (fromState == null || toState == null || fromState == toState)
        {
            return;
        }

        if (fromState.transitions.Any(x => x.destinationState == toState &&
                                           HasBoolCondition(x, firstParameter, firstValue) &&
                                           (secondParameter == null || HasBoolCondition(x, secondParameter, secondValue))))
        {
            return;
        }

        var transition = fromState.AddTransition(toState);
        transition.hasExitTime = false;
        transition.duration = 0.1f;
        transition.hasFixedDuration = true;
        transition.canTransitionToSelf = false;
        transition.AddCondition(firstValue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, firstParameter);
        if (!string.IsNullOrWhiteSpace(secondParameter))
        {
            transition.AddCondition(secondValue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, secondParameter);
        }
    }

    private static bool HasBoolCondition(AnimatorStateTransition transition, string parameterName, bool expectedValue)
    {
        var expectedMode = expectedValue ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot;
        return transition.conditions.Any(x => x.parameter == parameterName && x.mode == expectedMode);
    }

    private static void EnsureParameter(AnimatorController controller, string name, AnimatorControllerParameterType type)
    {
        if (controller.parameters.All(x => !string.Equals(x.name, name, StringComparison.Ordinal)))
        {
            controller.AddParameter(name, type);
        }
    }

    private static string BuildSequenceTriggerParameterName(string sequenceName)
    {
        return $"Play_{CreatureSkeletalImportUtility.SanitizeName(sequenceName)}";
    }

    private static string BuildSkillTriggerParameterName(int skillId, string sequenceName)
    {
        return $"Skill_{skillId}_{CreatureSkeletalImportUtility.SanitizeName(sequenceName)}";
    }
}
