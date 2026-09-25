using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using UnityEditor;
using UnityEngine;

internal static class CreatureSkeletalPrefabFactory
{
    internal sealed class EffectDecoration
    {
        public EffectDecoration(
            IReadOnlyList<SceneCreatureAttachedEffectData> effects,
            string clientRoot,
            string assetRoot,
            string characterName,
            Action<string> log)
        {
            Effects = effects;
            ClientRoot = clientRoot;
            AssetRoot = assetRoot;
            CharacterName = characterName;
            Log = log;
        }

        public IReadOnlyList<SceneCreatureAttachedEffectData> Effects { get; }
        public string ClientRoot { get; }
        public string AssetRoot { get; }
        public string CharacterName { get; }
        public Action<string> Log { get; }
    }

    internal sealed class EquipmentDecoration
    {
        public EquipmentDecoration(string slotName, GameObject prefab)
        {
            SlotName = slotName;
            Prefab = prefab;
        }

        public string SlotName { get; }
        public GameObject Prefab { get; }
    }

    public static void Create(
        L2CreatureCharacterArchetypeAsset archetype,
        string prefabPath,
        string displayLabel,
        EffectDecoration effectDecoration = null,
        IReadOnlyList<EquipmentDecoration> equipmentDecorations = null)
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
                AttachEquipment(build, equipmentDecorations);
                if (effectDecoration != null)
                {
                    CreatureAttachedEffectPrefabBuilder.Build(
                        build.Root,
                        build.Bones,
                        effectDecoration.Effects,
                        effectDecoration.ClientRoot,
                        effectDecoration.AssetRoot,
                        effectDecoration.CharacterName,
                        effectDecoration.Log);
                }

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

    private static void AttachEquipment(
        L2ModularCharacterPrefabFactory.BuildResult build,
        IReadOnlyList<EquipmentDecoration> equipment)
    {
        foreach (var attachment in equipment ?? Array.Empty<EquipmentDecoration>())
        {
            var parent = L2ModularCharacterPrefabFactory.ResolveSlotParent(
                build.Root.transform,
                attachment.SlotName,
                build.Bones);
            var instance = PrefabUtility.InstantiatePrefab(attachment.Prefab, parent) as GameObject;
            instance.name = attachment.Prefab.name;
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
        }
    }
}
