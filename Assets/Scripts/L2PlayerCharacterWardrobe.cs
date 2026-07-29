using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2PlayerCharacterWardrobe : L2ModularSkeletalCharacterBehaviour
{
    public L2PlayerCharacterArchetypeAsset Archetype;
    public int SelectedAnimationIndex;
    public int SelectedFaceIndex;
    public int SelectedHairIndex;
    public int SelectedChestIndex;
    public int SelectedLegsIndex;
    public int SelectedGlovesIndex;
    public int SelectedFeetIndex;

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
            "Face" => SelectedFaceIndex,
            "Hair" => SelectedHairIndex,
            "Chest" => SelectedChestIndex,
            "Legs" => SelectedLegsIndex,
            "Gloves" => SelectedGlovesIndex,
            "Feet" => SelectedFeetIndex,
            _ => 0
        };
    }

    protected override void SetSelectedSlotIndexValue(string slotName, int index)
    {
        switch (slotName)
        {
            case "Face":
                SelectedFaceIndex = index;
                break;
            case "Hair":
                SelectedHairIndex = index;
                break;
            case "Chest":
                SelectedChestIndex = index;
                break;
            case "Legs":
                SelectedLegsIndex = index;
                break;
            case "Gloves":
                SelectedGlovesIndex = index;
                break;
            case "Feet":
                SelectedFeetIndex = index;
                break;
        }
    }
}
