using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.SceneDomain.Models;

internal static class PlayerCharacterImportPlanBuilder
{
    private static readonly SceneCharacterPaperdollSlot[] EquipmentSlots =
    {
        SceneCharacterPaperdollSlot.Chest,
        SceneCharacterPaperdollSlot.Legs,
        SceneCharacterPaperdollSlot.Gloves,
        SceneCharacterPaperdollSlot.Feet,
        SceneCharacterPaperdollSlot.RightHand,
        SceneCharacterPaperdollSlot.LeftHand,
        SceneCharacterPaperdollSlot.LeftRightHand
    };

    public static PlayerCharacterImportPlan Build(
        SceneCharacterAppearanceData appearance,
        SceneCharacterAppearanceOptionsData options,
        SceneCharacterEquipmentCatalogData equipment)
    {
        var variants = new List<PlayerCharacterVariantPlan>();
        foreach (var face in options.FaceOptions)
        {
            variants.Add(new PlayerCharacterVariantPlan(
                "Face",
                $"Face {face.Id}",
                $"face_{face.Id:D2}",
                face.Id,
                -1,
                face.Binding,
                face.MeshResources,
                face.TextureResources));
        }

        foreach (var hairStyle in options.HairStyleOptions)
        {
            foreach (var hairColor in hairStyle.HairColorOptions)
            {
                variants.Add(new PlayerCharacterVariantPlan(
                    "Hair",
                    $"Hair {hairStyle.Id} / Color {hairColor.Id}",
                    $"hair_{hairStyle.Id:D2}_{hairColor.Id:D2}",
                    hairStyle.Id,
                    hairColor.Id,
                    SceneCharacterPartBinding.MeshSkinning,
                    hairStyle.MeshResources,
                    hairColor.TextureResources));
            }
        }

        foreach (var slot in EquipmentSlots)
        {
            var slotName = slot.ToString();
            var basePart = appearance.Parts.FirstOrDefault(x => x.Slot == slot);
            if (basePart != null && !IsWeaponSlot(slot))
            {
                variants.Add(new PlayerCharacterVariantPlan(
                    slotName,
                    "<Base>",
                    $"{slotName.ToLowerInvariant()}_base",
                    -1,
                    -1,
                    basePart.Binding,
                    basePart.MeshResources,
                    basePart.TextureResources));
            }

            var catalogSlot = equipment.Slots.FirstOrDefault(x => x.Slot == slot);
            foreach (var item in catalogSlot?.Items.Where(x => CanBuildEquipmentItem(slot, x))
                         ?? Array.Empty<SceneCharacterEquipmentCatalogItemData>())
            {
                variants.Add(new PlayerCharacterVariantPlan(
                    slotName,
                    $"{item.ItemId} {item.DisplayName}",
                    $"{slotName.ToLowerInvariant()}_{item.ItemId}",
                    item.ItemId,
                    -1,
                    SceneCharacterPartBinding.MeshSkinning,
                    item.MeshResources,
                    item.TextureResources,
                    item));
            }
        }

        return new PlayerCharacterImportPlan(appearance, variants.ToArray());
    }

    public static bool IsWeaponSlot(SceneCharacterPaperdollSlot slot)
    {
        return slot == SceneCharacterPaperdollSlot.RightHand ||
               slot == SceneCharacterPaperdollSlot.LeftHand ||
               slot == SceneCharacterPaperdollSlot.LeftRightHand;
    }

    public static bool IsWeaponSlotName(string slotName)
    {
        return Enum.TryParse<SceneCharacterPaperdollSlot>(slotName, out var slot) && IsWeaponSlot(slot);
    }

    private static bool CanBuildEquipmentItem(
        SceneCharacterPaperdollSlot slot,
        SceneCharacterEquipmentCatalogItemData item)
    {
        if (slot == SceneCharacterPaperdollSlot.LeftHand &&
            item.AnimationClass != SceneWeaponAnimationClass.None)
        {
            return false;
        }

        return item.IsRenderableWithCurrentAppearanceBuilder ||
               (IsWeaponSlot(slot) && item.MeshResources.Length > 0);
    }
}
