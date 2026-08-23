using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal static class CreatureSkeletalPrefabFactory
{
    public static void Create(L2CreatureCharacterArchetypeAsset archetype, string prefabPath, string displayLabel)
    {
        if (archetype == null)
        {
            throw new ArgumentNullException(nameof(archetype));
        }

        L2ModularCharacterPrefabFactory.Create(
            archetype,
            prefabPath,
            $"NPC_{archetype.BaseAsset.CharacterName}",
            build =>
            {
                var wardrobe = build.Root.AddComponent<L2CreatureWardrobe>();
                wardrobe.Archetype = archetype;
                wardrobe.SkeletonRoot = build.SkeletonRoot;
                wardrobe.RootBone = build.RootBone;
                wardrobe.Bones = build.Bones;
                wardrobe.SlotBindings = build.SlotBindings;
                wardrobe.Animator = build.Animator;
                wardrobe.ApplyAppearance();
                wardrobe.ApplyAnimation();

                EditorUtility.SetDirty(wardrobe);
                EditorUtility.SetDirty(build.Animator);
                EditorUtility.SetDirty(build.Root);
            },
            root =>
            {
                var labelMesh = archetype.Slots?
                    .FirstOrDefault(x => x?.SlotName == "Body")?
                    .Variants?.FirstOrDefault()?
                    .Parts?.FirstOrDefault()?
                    .Mesh;
                CreatureSkeletalImportUtility.CreateLabel(root.transform, labelMesh, displayLabel);
            },
            replaceExisting: true);
    }
}
