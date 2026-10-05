using System;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public abstract class L2ModularSkeletalCharacterBehaviour : MonoBehaviour
{
    [HideInInspector] public Transform SkeletonRoot;
    [HideInInspector] public Transform RootBone;
    [HideInInspector] public Transform[] Bones = Array.Empty<Transform>();
    [HideInInspector] public L2CharacterSlotBinding[] SlotBindings = Array.Empty<L2CharacterSlotBinding>();
    [HideInInspector] public Animator Animator;

    private L2SkinnedPartVisualController _visual;
    private L2CharacterAnimationPlayback _playback;

    protected abstract L2ModularCharacterArchetypeAssetBase GetArchetype();
    protected abstract int GetSelectedAnimationIndexValue();
    protected abstract void SetSelectedAnimationIndexValue(int index);
    protected abstract int GetSelectedSlotIndexValue(string slotName);
    protected abstract void SetSelectedSlotIndexValue(string slotName, int index);

    public string[] GetAnimationNames()
    {
        var sequences = GetArchetype()?.BaseAsset?.AnimationSequences ?? Array.Empty<L2SkeletalAnimationSequenceData>();
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
        SetSelectedAnimationIndexValue(Mathf.Max(0, index));
        ApplyAnimation();
    }

    public void SetSelectedVariant(string slotName, int index)
    {
        SetSelectedSlotIndexValue(slotName, Mathf.Max(0, index));
        ApplySlot(slotName);
    }

    public void ApplyAppearance()
    {
        foreach (var slot in GetArchetype()?.Slots ?? Array.Empty<L2CharacterSlotCatalogData>())
        {
            if (slot != null && !string.IsNullOrWhiteSpace(slot.SlotName))
            {
                ApplySlot(slot.SlotName);
            }
        }
    }

    public void ApplyAnimation()
    {
        var sequences = GetArchetype()?.BaseAsset?.AnimationSequences ?? Array.Empty<L2SkeletalAnimationSequenceData>();
        if (sequences.Length == 0)
        {
            return;
        }

        var selectedIndex = Mathf.Clamp(GetSelectedAnimationIndexValue(), 0, sequences.Length - 1);
        SetSelectedAnimationIndexValue(selectedIndex);
        var sequence = sequences[selectedIndex];
        if (sequence != null)
        {
            (_playback ??= new L2CharacterAnimationPlayback(this)).Play(GetArchetype(), Animator, sequence.Name);
        }
    }

    protected virtual void OnEnable()
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

    protected virtual void OnValidate()
    {
        if (Application.isPlaying)
        {
            ApplyAppearance();
            ApplyAnimation();
        }
    }

    protected virtual void OnDisable()
    {
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
            GetArchetype(), SlotBindings, SkeletonRoot, RootBone, Bones, slotName, selectedIndex);
    }

    protected L2CharacterSlotCatalogData GetSlot(string slotName)
    {
        return GetArchetype()?.Slots?.FirstOrDefault(x => string.Equals(x?.SlotName, slotName, StringComparison.OrdinalIgnoreCase));
    }

}
