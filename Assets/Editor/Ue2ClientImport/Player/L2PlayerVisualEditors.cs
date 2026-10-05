using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(L2PlayerAppearanceVisual))]
public sealed class L2PlayerAppearanceVisualEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var appearance = (L2PlayerAppearanceVisual)target;
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2PlayerAppearanceVisual.Archetype)));
        DrawSlot(appearance, "Face", nameof(L2PlayerAppearanceVisual.SelectedFaceIndex));
        DrawSlot(appearance, "Hair", nameof(L2PlayerAppearanceVisual.SelectedHairIndex));
        DrawSlot(appearance, "Chest", nameof(L2PlayerAppearanceVisual.SelectedChestIndex));
        DrawSlot(appearance, "Legs", nameof(L2PlayerAppearanceVisual.SelectedLegsIndex));
        DrawSlot(appearance, "Gloves", nameof(L2PlayerAppearanceVisual.SelectedGlovesIndex));
        DrawSlot(appearance, "Feet", nameof(L2PlayerAppearanceVisual.SelectedFeetIndex));
        if (serializedObject.ApplyModifiedProperties())
        {
            appearance.ApplyAppearance();
        }
    }

    private void DrawSlot(L2PlayerAppearanceVisual appearance, string slot, string propertyName)
    {
        var names = appearance.GetVariantDisplayNames(slot);
        if (names.Length == 0)
        {
            EditorGUILayout.LabelField(slot, "<none>");
            return;
        }

        var property = serializedObject.FindProperty(propertyName);
        property.intValue = EditorGUILayout.Popup(slot, Mathf.Clamp(property.intValue, 0, names.Length - 1), names);
    }
}

[CustomEditor(typeof(L2PlayerEquipmentVisual))]
public sealed class L2PlayerEquipmentVisualEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var equipment = (L2PlayerEquipmentVisual)target;
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2PlayerEquipmentVisual.Archetype)));
        var mode = serializedObject.FindProperty(nameof(L2PlayerEquipmentVisual.WeaponMode));
        EditorGUILayout.PropertyField(mode);
        switch ((L2PlayerWeaponMode)mode.enumValueIndex)
        {
            case L2PlayerWeaponMode.OneHanded:
                DrawSlot(equipment, "RightHand", nameof(L2PlayerEquipmentVisual.SelectedRightHandIndex), "Weapon");
                var shield = serializedObject.FindProperty(nameof(L2PlayerEquipmentVisual.ShowShield));
                EditorGUILayout.PropertyField(shield, new GUIContent("Shield"));
                if (shield.boolValue)
                {
                    DrawSlot(equipment, "LeftHand", nameof(L2PlayerEquipmentVisual.SelectedLeftHandIndex), "Shield Type");
                }
                break;
            case L2PlayerWeaponMode.TwoHanded:
                DrawSlot(equipment, "LeftRightHand", nameof(L2PlayerEquipmentVisual.SelectedLeftRightHandIndex), "Two-Hand Weapon");
                break;
        }

        if (serializedObject.ApplyModifiedProperties())
        {
            equipment.ApplyEquipment();
            equipment.GetComponent<L2PlayerAnimationStateController>()?.Refresh();
        }

        if (equipment.Archetype != null)
        {
            EditorGUILayout.LabelField("Animation Class", equipment.AnimationClass.ToString());
        }
    }

    private void DrawSlot(L2PlayerEquipmentVisual equipment, string slot, string propertyName, string label)
    {
        var names = equipment.GetVariantDisplayNames(slot);
        if (names.Length == 0)
        {
            EditorGUILayout.LabelField(label, "<none>");
            return;
        }

        var property = serializedObject.FindProperty(propertyName);
        property.intValue = EditorGUILayout.Popup(label, Mathf.Clamp(property.intValue, 0, names.Length - 1), names);
    }
}

[CustomEditor(typeof(L2PlayerAnimationStateController))]
public sealed class L2PlayerAnimationStateControllerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        var controller = (L2PlayerAnimationStateController)target;
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2PlayerAnimationStateController.Archetype)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2PlayerAnimationStateController.Equipment)));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2PlayerAnimationStateController.Animator)));
        EditorGUILayout.Space();
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2PlayerAnimationStateController.IsSitting)), new GUIContent("Sitting"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2PlayerAnimationStateController.IsInCombat)), new GUIContent("Combat"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2PlayerAnimationStateController.IsRunning)), new GUIContent("Running"));
        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2PlayerAnimationStateController.IsCasting)), new GUIContent("Casting"));
        using (new EditorGUI.DisabledScope(controller.Equipment != null &&
                                           controller.Equipment.AnimationClass == L2WeaponAnimationClass.Fishing))
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2PlayerAnimationStateController.IsAttacking)), new GUIContent("Attacking"));
        }
        var custom = serializedObject.FindProperty(nameof(L2PlayerAnimationStateController.UseCustomAnimation));
        EditorGUILayout.PropertyField(custom, new GUIContent("Custom Animation"));
        if (custom.boolValue)
        {
            var names = controller.GetAnimationNames();
            if (names.Length > 0)
            {
                var index = serializedObject.FindProperty(nameof(L2PlayerAnimationStateController.CustomAnimationIndex));
                index.intValue = EditorGUILayout.Popup("Sequence", Mathf.Clamp(index.intValue, 0, names.Length - 1), names);
            }

            EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2PlayerAnimationStateController.LoopCustomAnimation)), new GUIContent("Loop"));
        }

        if (serializedObject.ApplyModifiedProperties())
        {
            controller.Refresh();
        }
    }
}
