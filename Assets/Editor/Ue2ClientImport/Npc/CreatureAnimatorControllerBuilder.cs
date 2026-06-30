using System;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;

internal static class CreatureAnimatorControllerBuilder
{
    public static AnimatorController Build(string referenceText, string prefabRoot, CreatureAnimationClipBuilder.ClipBuildInfo[] clips, Action<string> log, out string notes)
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

        foreach (var childState in layer.stateMachine.states)
        {
            if (childState.state != null)
            {
                layer.stateMachine.RemoveState(childState.state);
            }
        }

        AnimatorState defaultState = null;
        foreach (var clipInfo in clips.Where(x => x.Clip != null))
        {
            var state = layer.stateMachine.AddState(clipInfo.Clip.name);
            state.motion = clipInfo.Clip;

            if (defaultState == null)
            {
                defaultState = state;
            }

            if (CreatureSkeletalImportUtility.IsPreferredDefaultStateName(clipInfo.Clip.name))
            {
                defaultState = state;
            }
        }

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
}
