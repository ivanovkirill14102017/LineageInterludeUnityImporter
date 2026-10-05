using System;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2CreatureWardrobe : MonoBehaviour
{
    public L2CreatureCharacterArchetypeAsset Archetype;
    public int SelectedAnimationIndex;
    public int SelectedBodyIndex;
    public int SelectedWeaponIndex;
    [HideInInspector] public Transform SkeletonRoot;
    [HideInInspector] public Transform RootBone;
    [HideInInspector] public Transform[] Bones = Array.Empty<Transform>();
    [HideInInspector] public L2CharacterSlotBinding[] SlotBindings = Array.Empty<L2CharacterSlotBinding>();
    [HideInInspector] public Animator Animator;

    private L2SkinnedPartVisualController _visual;

    public string[] GetAnimationNames()
    {
        var sequences = Archetype?.BaseAsset?.AnimationSequences ?? Array.Empty<L2SkeletalAnimationSequenceData>();
        return sequences
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => x.Name)
            .ToArray();
    }

    public string[] GetVariantDisplayNames(string slotName)
    {
        var slot = GetSlot(slotName);
        return slot?.Variants?.Select(x => x?.DisplayName ?? "<null>").ToArray() ?? Array.Empty<string>();
    }

    public void SetSelectedAnimation(int index)
    {
        SelectedAnimationIndex = Mathf.Max(0, index);
        ApplyAnimation();
    }

    public void SetSelectedVariant(string slotName, int index)
    {
        SetSelectedSlotIndexValue(slotName, Mathf.Max(0, index));
        ApplySlot(slotName);
    }

    public void ApplyAppearance()
    {
        foreach (var slot in Archetype?.Slots ?? Array.Empty<L2CharacterSlotCatalogData>())
        {
            if (slot != null && !string.IsNullOrWhiteSpace(slot.SlotName))
            {
                ApplySlot(slot.SlotName);
            }
        }
    }

    public void ApplyAnimation()
    {
        var sequences = Archetype?.BaseAsset?.AnimationSequences ?? Array.Empty<L2SkeletalAnimationSequenceData>();
        if (sequences.Length == 0)
        {
            return;
        }

        SelectedAnimationIndex = Mathf.Clamp(SelectedAnimationIndex, 0, sequences.Length - 1);
        var sequence = sequences[SelectedAnimationIndex];
        if (sequence != null)
        {
            L2CharacterAnimationPlayback.Play(Archetype, Animator, sequence.Name);
        }
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
        if (Application.isPlaying)
        {
            ApplyAnimation();
        }
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            ApplyAppearance();
            ApplyAnimation();
        }
    }

    private void ApplySlot(string slotName)
    {
        var slot = GetSlot(slotName);
        var variants = slot?.Variants ?? Array.Empty<L2CharacterVariantData>();
        if (variants.Length == 0)
        {
            _visual?.Clear(slotName);
            return;
        }

        var selectedIndex = Mathf.Clamp(GetSelectedSlotIndexValue(slotName), 0, variants.Length - 1);
        SetSelectedSlotIndexValue(slotName, selectedIndex);
        (_visual ??= new L2SkinnedPartVisualController(this)).Apply(
            Archetype, SlotBindings, SkeletonRoot, RootBone, Bones, slotName, selectedIndex);
    }

    private L2CharacterSlotCatalogData GetSlot(string slotName)
    {
        return Archetype?.Slots?.FirstOrDefault(x => string.Equals(x?.SlotName, slotName, StringComparison.OrdinalIgnoreCase));
    }

    private int GetSelectedSlotIndexValue(string slotName)
    {
        return slotName switch
        {
            "Body" => SelectedBodyIndex,
            "Weapon" => SelectedWeaponIndex,
            _ => 0
        };
    }

    private void SetSelectedSlotIndexValue(string slotName, int index)
    {
        switch (slotName)
        {
            case "Body":
                SelectedBodyIndex = index;
                break;
            case "Weapon":
                SelectedWeaponIndex = index;
                break;
        }
    }
}
