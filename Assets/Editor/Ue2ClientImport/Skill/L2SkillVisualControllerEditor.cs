using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(L2SkillVisualController))]
public sealed class L2SkillVisualControllerEditor : Editor
{
    private bool _showAdvanced;

    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var controller = (L2SkillVisualController)target;

        DrawSkillAssetDropdown(controller);
        serializedObject.Update();

        DrawAnchors();
        DrawPlaybackSettings(controller);
        DrawTimeline(controller);
        DrawAdvanced(controller);

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawAnchors()
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Preview Rig", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.CastPoint)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.TargetPoint)));
    }

    private void DrawPlaybackSettings(L2SkillVisualController controller)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.SelectedStageReplayIntervalSeconds)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.ProjectileSpeed)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.ProjectileArcHeight)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.RuntimeInstanceLifetime)));

        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(!HasStages(controller)))
            {
                if (GUILayout.Button("Play Full Visual"))
                {
                    ApplyAndRun(controller, c => c.PlayAllStages());
                }

                if (GUILayout.Button("Stop"))
                {
                    ApplyAndRun(controller, c => c.Stop());
                }
            }
        }

        using (new EditorGUI.DisabledScope(controller.Skill == null))
        {
            if (GUILayout.Button("Reload Visual From Asset"))
            {
                ApplyAndRun(controller, c => c.ApplySkillTemplate(c.Skill));
            }
        }
    }

    private void DrawTimeline(L2SkillVisualController controller)
    {
        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Visual Steps", EditorStyles.boldLabel);

        var bindings = controller.StageBindings ?? Array.Empty<L2SkillVisualStageBinding>();
        if (bindings.Length == 0)
        {
            EditorGUILayout.HelpBox("No visual steps are configured on this controller.", MessageType.Info);
            return;
        }

        var selected = Mathf.Clamp(controller.SelectedStageIndex, 0, bindings.Length - 1);
        var nextSelected = EditorGUILayout.Popup("Selected Step", selected, controller.GetStageDisplayNames());
        if (nextSelected != selected)
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(controller, "Change selected skill visual step");
            controller.SetSelectedStage(nextSelected, preview: false);
            EditorUtility.SetDirty(controller);
            serializedObject.Update();
        }

        for (var i = 0; i < bindings.Length; i++)
        {
            var binding = bindings[i];
            if (binding == null)
            {
                continue;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                var wasSelected = i == controller.SelectedStageIndex;
                var labelStyle = wasSelected ? EditorStyles.boldLabel : EditorStyles.label;
                EditorGUILayout.LabelField(BuildStepLabel(i, binding), labelStyle, GUILayout.MinWidth(220f));

                if (GUILayout.Button("View", GUILayout.Width(54f)))
                {
                    ApplyAndRun(controller, c => c.PreviewStageAt(i));
                }

                if (GUILayout.Button("Play", GUILayout.Width(54f)))
                {
                    ApplyAndRun(controller, c => c.PlayStageAt(i));
                }

                if (GUILayout.Button("From", GUILayout.Width(54f)))
                {
                    ApplyAndRun(controller, c => c.PlayAllStagesFrom(i));
                }
            }
        }
    }

    private void DrawAdvanced(L2SkillVisualController controller)
    {
        EditorGUILayout.Space();
        _showAdvanced = EditorGUILayout.Foldout(_showAdvanced, "Advanced", true);
        if (!_showAdvanced)
        {
            return;
        }

        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.PlayOnEnable)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.LoopSelectedStage)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.IsPlaying)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.StageContainer)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.RuntimeContainer)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.StageBindings)), true);

        if (GUILayout.Button("Rebuild Steps From Children"))
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RegisterFullObjectHierarchyUndo(controller.gameObject, "Rebuild skill visual steps");
            controller.RebuildStageBindingsFromChildren();
            EditorUtility.SetDirty(controller);
            serializedObject.Update();
        }
    }

    private void DrawSkillAssetDropdown(L2SkillVisualController controller)
    {
        var skillAssets = AssetDatabase.FindAssets("t:L2SkillVisualAsset")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(path => AssetDatabase.LoadAssetAtPath<L2SkillVisualAsset>(path))
            .Where(asset => asset != null)
            .OrderBy(asset => asset.SkillId)
            .ThenBy(asset => asset.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (skillAssets.Length == 0)
        {
            var nextSkill = (L2SkillVisualAsset)EditorGUILayout.ObjectField(
                "Skill",
                controller.Skill,
                typeof(L2SkillVisualAsset),
                false);
            if (nextSkill != controller.Skill)
            {
                ApplySkillTemplateWithUndo(controller, nextSkill);
            }

            EditorGUILayout.HelpBox("No imported L2SkillVisualAsset assets were found. Import one via L2/Import Skill Visual first.", MessageType.Info);
            return;
        }

        var labels = new[] { "None" }
            .Concat(skillAssets
            .Select(asset => $"{asset.SkillId:D5} - {(string.IsNullOrWhiteSpace(asset.DisplayName) ? asset.name : asset.DisplayName)}")
            )
            .ToArray();
        var selected = controller.Skill == null ? 0 : Array.IndexOf(skillAssets, controller.Skill) + 1;
        if (selected < 0)
        {
            selected = 0;
        }

        var nextSelected = EditorGUILayout.Popup("Skill", selected, labels);
        if (nextSelected != selected)
        {
            var nextSkill = nextSelected <= 0
                ? null
                : skillAssets[Mathf.Clamp(nextSelected - 1, 0, skillAssets.Length - 1)];
            ApplySkillTemplateWithUndo(controller, nextSkill);
        }
    }

    private static bool HasStages(L2SkillVisualController controller)
    {
        return controller.StageBindings != null && controller.StageBindings.Length > 0;
    }

    private static string BuildStepLabel(int index, L2SkillVisualStageBinding binding)
    {
        var name = string.IsNullOrWhiteSpace(binding.StageName)
            ? binding.StageRoot != null ? binding.StageRoot.name : $"Step {index}"
            : binding.StageName;
        return $"{index + 1:00}. {binding.Role} - {name}";
    }

    private void ApplyAndRun(L2SkillVisualController controller, Action<L2SkillVisualController> action)
    {
        serializedObject.ApplyModifiedProperties();
        Undo.RegisterFullObjectHierarchyUndo(controller.gameObject, "Preview skill visual");
        action(controller);
        EditorUtility.SetDirty(controller);
        serializedObject.Update();
    }

    private void ApplySkillTemplateWithUndo(L2SkillVisualController controller, L2SkillVisualAsset nextSkill)
    {
        serializedObject.ApplyModifiedProperties();
        Undo.RegisterFullObjectHierarchyUndo(controller.gameObject, "Change skill visual asset");
        controller.ApplySkillTemplate(nextSkill);
        EditorUtility.SetDirty(controller);
        serializedObject.Update();
    }
}
