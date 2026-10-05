using System;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

public enum L2PlayerWeaponMode
{
    Unarmed,
    OneHanded,
    TwoHanded
}

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2PlayerEquipmentVisual : MonoBehaviour
{
    public L2PlayerCharacterArchetypeAsset Archetype;
    [HideInInspector] public Transform SkeletonRoot;
    [HideInInspector] public Transform RootBone;
    [HideInInspector] public Transform[] Bones = Array.Empty<Transform>();
    [HideInInspector] public L2CharacterSlotBinding[] SlotBindings = Array.Empty<L2CharacterSlotBinding>();

    public L2PlayerWeaponMode WeaponMode = L2PlayerWeaponMode.Unarmed;
    public bool ShowShield;
    public int SelectedRightHandIndex;
    public int SelectedLeftHandIndex;
    public int SelectedLeftRightHandIndex;

    private L2SkinnedPartVisualController _visual;

    public L2WeaponAnimationClass AnimationClass
    {
        get
        {
            var slotName = WeaponMode switch
            {
                L2PlayerWeaponMode.OneHanded => "RightHand",
                L2PlayerWeaponMode.TwoHanded => "LeftRightHand",
                _ => null
            };
            if (slotName == null)
            {
                return L2WeaponAnimationClass.Hand;
            }

            var variants = GetVariants(slotName);
            var selected = slotName == "RightHand" ? SelectedRightHandIndex : SelectedLeftRightHandIndex;
            if (variants.Length == 0)
            {
                return L2WeaponAnimationClass.Hand;
            }

            var selectedVariant = variants[Mathf.Clamp(selected, 0, variants.Length - 1)];
            var animationClass = selectedVariant?.WeaponAnimationClass
                ?? L2WeaponAnimationClass.None;
            if (animationClass == L2WeaponAnimationClass.None)
            {
                throw new InvalidOperationException(
                    $"Weapon slot '{slotName}' has no animation class for item {selectedVariant?.VariantId}.");
            }

            return animationClass;
        }
    }

    public string[] GetVariantDisplayNames(string slotName)
    {
        return GetVariants(slotName).Select(x => x?.DisplayName ?? "<null>").ToArray();
    }

    public void ApplyEquipment()
    {
        _visual ??= new L2SkinnedPartVisualController(this);
        var oneHanded = WeaponMode == L2PlayerWeaponMode.OneHanded;
        var twoHanded = WeaponMode == L2PlayerWeaponMode.TwoHanded;
        _visual.Apply(Archetype, SlotBindings, SkeletonRoot, RootBone, Bones,
            "RightHand", SelectedRightHandIndex, oneHanded);
        _visual.Apply(Archetype, SlotBindings, SkeletonRoot, RootBone, Bones,
            "LeftHand", SelectedLeftHandIndex, oneHanded && ShowShield);
        _visual.Apply(Archetype, SlotBindings, SkeletonRoot, RootBone, Bones,
            "LeftRightHand", SelectedLeftRightHandIndex, twoHanded);
    }

    private L2CharacterVariantData[] GetVariants(string slotName)
    {
        return Archetype?.Slots?
            .FirstOrDefault(x => string.Equals(x?.SlotName, slotName, StringComparison.OrdinalIgnoreCase))?
            .Variants ?? Array.Empty<L2CharacterVariantData>();
    }

    private void OnEnable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && EditorUtility.IsPersistent(this))
        {
            return;
        }
#endif
        ApplyEquipment();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
        {
            ApplyEquipment();
        }
    }
}
