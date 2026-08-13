using UnityEditor;
using UnityEngine;

[CustomEditor(typeof(L2PlayerCharacterWardrobe))]
public sealed class L2PlayerCharacterWardrobeEditor : Editor
{
    public override void OnInspectorGUI()
    {
        serializedObject.Update();

        var wardrobe = (L2PlayerCharacterWardrobe)target;

        EditorGUILayout.PropertyField(serializedObject.FindProperty(nameof(L2PlayerCharacterWardrobe.Archetype)));

        DrawAnimationPopup(wardrobe);
        DrawSlotPopup(wardrobe, "Face", nameof(L2PlayerCharacterWardrobe.SelectedFaceIndex));
        DrawSlotPopup(wardrobe, "Hair", nameof(L2PlayerCharacterWardrobe.SelectedHairIndex));
        DrawSlotPopup(wardrobe, "Chest", nameof(L2PlayerCharacterWardrobe.SelectedChestIndex));
        DrawSlotPopup(wardrobe, "Legs", nameof(L2PlayerCharacterWardrobe.SelectedLegsIndex));
        DrawSlotPopup(wardrobe, "Gloves", nameof(L2PlayerCharacterWardrobe.SelectedGlovesIndex));
        DrawSlotPopup(wardrobe, "Feet", nameof(L2PlayerCharacterWardrobe.SelectedFeetIndex));
        DrawSlotPopup(wardrobe, "RightHand", nameof(L2PlayerCharacterWardrobe.SelectedRightHandIndex), "Right Weapon");
        DrawSlotPopup(wardrobe, "LeftHand", nameof(L2PlayerCharacterWardrobe.SelectedLeftHandIndex), "Left Weapon");
        DrawSlotPopup(wardrobe, "LeftRightHand", nameof(L2PlayerCharacterWardrobe.SelectedLeftRightHandIndex), "Two-Hand Weapon");

        serializedObject.ApplyModifiedProperties();
    }

    private void DrawAnimationPopup(L2PlayerCharacterWardrobe wardrobe)
    {
        var names = wardrobe.GetAnimationNames();
        if (names.Length == 0)
        {
            EditorGUILayout.HelpBox("No animations available on the archetype base asset.", MessageType.Info);
            return;
        }

        var property = serializedObject.FindProperty(nameof(L2PlayerCharacterWardrobe.SelectedAnimationIndex));
        var current = Mathf.Clamp(property.intValue, 0, names.Length - 1);
        var next = EditorGUILayout.Popup("Animation", current, names);
        if (next != current)
        {
            property.intValue = next;
            serializedObject.ApplyModifiedProperties();
            wardrobe.ApplyAnimation();
            EditorUtility.SetDirty(wardrobe);
            serializedObject.Update();
        }
    }

    private void DrawSlotPopup(L2PlayerCharacterWardrobe wardrobe, string slotName, string propertyName, string label = null)
    {
        var labels = wardrobe.GetVariantDisplayNames(slotName);
        if (labels.Length == 0)
        {
            EditorGUILayout.LabelField(label ?? slotName, "<none>");
            return;
        }

        var property = serializedObject.FindProperty(propertyName);
        var current = Mathf.Clamp(property.intValue, 0, labels.Length - 1);
        var next = EditorGUILayout.Popup(label ?? slotName, current, labels);
        if (next != current)
        {
            property.intValue = next;
            serializedObject.ApplyModifiedProperties();
            wardrobe.ApplyAppearance();
            wardrobe.ApplyAnimation();
            EditorUtility.SetDirty(wardrobe);
            serializedObject.Update();
        }
    }
}
