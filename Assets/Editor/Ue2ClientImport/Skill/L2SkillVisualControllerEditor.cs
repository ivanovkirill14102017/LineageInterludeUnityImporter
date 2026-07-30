using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(L2SkillVisualController))]
public sealed class L2SkillVisualControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var controller = (L2SkillVisualController)target;

        DrawSkillAssetDropdown(controller);
        serializedObject.Update();

        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.CastPoint)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.TargetPoint)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.StageContainer)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.RuntimeContainer)));

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Playback", EditorStyles.boldLabel);
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.PlayOnEnable)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.LoopSelectedStage)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.PlayImpactAfterProjectile)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.StageIntervalSeconds)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.ProjectileSpeed)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.ProjectileArcHeight)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.RuntimeInstanceLifetime)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.IsPlaying)));

        DrawStageDropdown(controller);
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2SkillVisualController.StageBindings)), true);

        EditorGUILayout.Space();
        using (new EditorGUILayout.HorizontalScope())
        {
            using (new EditorGUI.DisabledScope(controller.StageBindings == null || controller.StageBindings.Length == 0))
            {
                if (GUILayout.Button(Application.isPlaying ? "Play Selected" : "Preview Selected"))
                {
                    serializedObject.ApplyModifiedProperties();
                    controller.PlaySelectedStage();
                    EditorUtility.SetDirty(controller);
                    serializedObject.Update();
                }

                if (GUILayout.Button("Play Sequence"))
                {
                    serializedObject.ApplyModifiedProperties();
                    controller.PlayAllStages();
                    EditorUtility.SetDirty(controller);
                    serializedObject.Update();
                }

                if (GUILayout.Button("Stop"))
                {
                    serializedObject.ApplyModifiedProperties();
                    controller.Stop();
                    EditorUtility.SetDirty(controller);
                    serializedObject.Update();
                }
            }
        }

        if (GUILayout.Button("Rebuild Stage Bindings From Children"))
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RegisterFullObjectHierarchyUndo(controller.gameObject, "Rebuild skill stage bindings");
            controller.RebuildStageBindingsFromChildren();
            EditorUtility.SetDirty(controller);
            serializedObject.Update();
        }

        serializedObject.ApplyModifiedProperties();
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

    private void DrawStageDropdown(L2SkillVisualController controller)
    {
        var stageNames = controller.GetStageDisplayNames();
        if (stageNames.Length == 0)
        {
            EditorGUILayout.HelpBox("No stage bindings are configured on this controller.", MessageType.Info);
            return;
        }

        var selected = Mathf.Clamp(controller.SelectedStageIndex, 0, stageNames.Length - 1);
        var nextSelected = EditorGUILayout.Popup("Selected Stage", selected, stageNames);
        if (nextSelected != selected)
        {
            serializedObject.ApplyModifiedProperties();
            Undo.RecordObject(controller, "Change selected skill stage");
            controller.SetSelectedStage(nextSelected, preview: false);
            EditorUtility.SetDirty(controller);
            serializedObject.Update();
        }
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
