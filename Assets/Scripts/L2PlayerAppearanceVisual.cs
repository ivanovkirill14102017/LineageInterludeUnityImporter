using System;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2PlayerAppearanceVisual : MonoBehaviour
{
    public L2PlayerCharacterArchetypeAsset Archetype;
    [HideInInspector] public Transform SkeletonRoot;
    [HideInInspector] public Transform RootBone;
    [HideInInspector] public Transform[] Bones = Array.Empty<Transform>();
    [HideInInspector] public L2CharacterSlotBinding[] SlotBindings = Array.Empty<L2CharacterSlotBinding>();

    public int SelectedFaceIndex;
    public int SelectedHairIndex;
    public int SelectedChestIndex;
    public int SelectedLegsIndex;
    public int SelectedGlovesIndex;
    public int SelectedFeetIndex;

    private L2SkinnedPartVisualController _visual;

    public string[] GetVariantDisplayNames(string slotName)
    {
        return Archetype?.Slots?
            .FirstOrDefault(x => string.Equals(x?.SlotName, slotName, StringComparison.OrdinalIgnoreCase))?
            .Variants?.Select(x => x?.DisplayName ?? "<null>").ToArray() ?? Array.Empty<string>();
    }

    public void ApplyAppearance()
    {
        ApplySlot("Face", SelectedFaceIndex);
        ApplySlot("Hair", SelectedHairIndex);
        ApplySlot("Chest", SelectedChestIndex);
        ApplySlot("Legs", SelectedLegsIndex);
        ApplySlot("Gloves", SelectedGlovesIndex);
        ApplySlot("Feet", SelectedFeetIndex);
    }

    private void ApplySlot(string slotName, int selectedIndex)
    {
        (_visual ??= new L2SkinnedPartVisualController(this)).Apply(
            Archetype, SlotBindings, SkeletonRoot, RootBone, Bones, slotName, selectedIndex);
    }

    private void OnEnable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && EditorUtility.IsPersistent(this))
        {
            return;
        }
#endif
        ApplyAppearance();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            ApplyAppearance();
        }
    }
}
