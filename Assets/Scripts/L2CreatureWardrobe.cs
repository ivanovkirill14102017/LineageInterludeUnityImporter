using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2CreatureWardrobe : L2ModularSkeletalCharacterBehaviour
{
    public L2CreatureCharacterArchetypeAsset Archetype;
    public int SelectedAnimationIndex;
    public int SelectedBodyIndex;
    public int SelectedWeaponIndex;

    protected override L2ModularCharacterArchetypeAssetBase GetArchetype()
    {
        return Archetype;
    }

    protected override int GetSelectedAnimationIndexValue()
    {
        return SelectedAnimationIndex;
    }

    protected override void SetSelectedAnimationIndexValue(int index)
    {
        SelectedAnimationIndex = index;
    }

    protected override int GetSelectedSlotIndexValue(string slotName)
    {
        return slotName switch
        {
            "Body" => SelectedBodyIndex,
            "Weapon" => SelectedWeaponIndex,
            _ => 0
        };
    }

    protected override void SetSelectedSlotIndexValue(string slotName, int index)
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
