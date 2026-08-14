#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class L2GeneratedAnimatorControllerIntegrity
{
    private const string CreatureControllerRoot = "Assets/L2Imported/Managed/CreaturePrefabs/Controllers";
    private const string CreatureAnimationRoot = "Assets/L2Imported/ClientPackages/Animations";
    private const string ControllerPrefix = "AC_";

    private static bool _checkedThisEditorSession;

    public static int EnsureCreatureControllersValidOnce()
    {
        if (_checkedThisEditorSession)
        {
            return 0;
        }

        _checkedThisEditorSession = true;
        return EnsureCreatureControllersValid();
    }

    [MenuItem("L2/Validate Generated Creature Animator Controllers")]
    public static void ValidateCreatureControllersFromMenu()
    {
        _checkedThisEditorSession = false;
        var repaired = EnsureCreatureControllersValidOnce();
        Debug.Log($"[L2Import] Creature AnimatorController validation finished. repaired={repaired}");
    }

    private static int EnsureCreatureControllersValid()
    {
        if (!AssetDatabase.IsValidFolder(CreatureControllerRoot))
        {
            return 0;
        }

        var repaired = 0;
        foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController", new[] { CreatureControllerRoot }))
        {
            var controllerPath = AssetDatabase.GUIDToAssetPath(guid);
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            if (HasValidBaseLayerStateMachine(controller))
            {
                continue;
            }

            var clips = FindImportedClips(controller).ToArray();
            if (!RepairMissingBaseLayer(controller, clips))
            {
                Debug.LogWarning($"[L2Import] AnimatorController '{controllerPath}' has no Base Layer state machine and no imported clips were found.");
                continue;
            }

            repaired++;
            Debug.Log($"[L2Import] Repaired generated AnimatorController '{controllerPath}' from {clips.Length} imported clip(s).");
        }

        if (repaired > 0)
        {
            AssetDatabase.SaveAssets();
        }

        return repaired;
    }

    private static bool HasValidBaseLayerStateMachine(AnimatorController controller)
    {
        return controller != null &&
               controller.layers != null &&
               controller.layers.Length > 0 &&
               controller.layers[0].stateMachine != null;
    }

    private static bool RepairMissingBaseLayer(AnimatorController controller, IReadOnlyList<AnimationClip> clips)
    {
        if (controller == null)
        {
            return false;
        }

        if (clips == null || clips.Count == 0)
        {
            return false;
        }

        var layer = EnsureBaseLayerStateMachineAsset(controller);
        foreach (var childState in layer.stateMachine.states)
        {
            if (childState.state != null)
            {
                layer.stateMachine.RemoveState(childState.state);
            }
        }

        AnimatorState defaultState = null;
        foreach (var clip in clips.Where(x => x != null).OrderBy(x => x.name, StringComparer.OrdinalIgnoreCase))
        {
            var state = layer.stateMachine.AddState(BuildStateName(controller, clip));
            state.motion = clip;
            state.writeDefaultValues = true;

            if (defaultState == null || IsPreferredDefaultState(state.name, clip.name))
            {
                defaultState = state;
            }
        }

        layer.stateMachine.defaultState = defaultState;
        controller.layers = new[] { layer };
        EditorUtility.SetDirty(layer.stateMachine);
        EditorUtility.SetDirty(controller);
        return true;
    }

    private static AnimatorControllerLayer EnsureBaseLayerStateMachineAsset(AnimatorController controller)
    {
        var layers = controller.layers;
        if (layers == null || layers.Length == 0)
        {
            layers = new[]
            {
                new AnimatorControllerLayer
                {
                    name = "Base Layer",
                    stateMachine = CreateStateMachineAsset(controller, "Base Layer")
                }
            };
            controller.layers = layers;
            EditorUtility.SetDirty(controller);
            return layers[0];
        }

        var layer = layers[0];
        if (layer.stateMachine == null)
        {
            layer.stateMachine = CreateStateMachineAsset(
                controller,
                string.IsNullOrWhiteSpace(layer.name) ? "Base Layer" : layer.name);
            layers[0] = layer;
            controller.layers = layers;
            EditorUtility.SetDirty(controller);
        }

        return layer;
    }

    private static AnimatorStateMachine CreateStateMachineAsset(AnimatorController controller, string layerName)
    {
        var stateMachine = new AnimatorStateMachine
        {
            name = string.IsNullOrWhiteSpace(layerName) ? "Base Layer" : layerName
        };
        AssetDatabase.AddObjectToAsset(stateMachine, controller);
        EditorUtility.SetDirty(stateMachine);
        return stateMachine;
    }

    private static IEnumerable<AnimationClip> FindImportedClips(AnimatorController controller)
    {
        var characterName = GetCharacterName(controller);
        if (string.IsNullOrWhiteSpace(characterName) || !AssetDatabase.IsValidFolder(CreatureAnimationRoot))
        {
            yield break;
        }

        var marker = "/" + characterName + "/Animations/";
        foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", new[] { CreatureAnimationRoot }))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid).Replace('\\', '/');
            if (path.IndexOf(marker, StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip != null)
            {
                yield return clip;
            }
        }
    }

    private static string BuildStateName(AnimatorController controller, AnimationClip clip)
    {
        var characterName = GetCharacterName(controller);
        if (!string.IsNullOrWhiteSpace(characterName) &&
            clip.name.StartsWith(characterName + "_", StringComparison.OrdinalIgnoreCase))
        {
            return clip.name.Substring(characterName.Length + 1);
        }

        var fileName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(clip));
        if (fileName.StartsWith("AN_", StringComparison.OrdinalIgnoreCase))
        {
            return fileName.Substring(3);
        }

        return clip.name;
    }

    private static string GetCharacterName(AnimatorController controller)
    {
        if (controller == null || string.IsNullOrWhiteSpace(controller.name))
        {
            return string.Empty;
        }

        return controller.name.StartsWith(ControllerPrefix, StringComparison.OrdinalIgnoreCase)
            ? controller.name.Substring(ControllerPrefix.Length)
            : controller.name;
    }

    private static bool IsPreferredDefaultState(string stateName, string clipName)
    {
        return IsPreferredDefaultName(stateName) || IsPreferredDefaultName(clipName);
    }

    private static bool IsPreferredDefaultName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = value.ToLowerInvariant();
        return (normalized.Contains("wait") || normalized.Contains("idle") || normalized.Contains("stand")) &&
               !normalized.Contains("death") &&
               !normalized.Contains("dead") &&
               !normalized.Contains("die");
    }
}
#endif
