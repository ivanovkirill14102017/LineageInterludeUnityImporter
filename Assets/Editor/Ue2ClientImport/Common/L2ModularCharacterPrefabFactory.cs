using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal static class L2ModularCharacterPrefabFactory
{
    internal sealed class BuildResult
    {
        public GameObject Root;
        public Transform SkeletonRoot;
        public Transform RootBone;
        public Transform[] Bones;
        public L2ModularSkeletalCharacterBehaviour.SlotBinding[] SlotBindings;
        public Animator Animator;
    }

    public static void Create(
        L2ModularCharacterArchetypeAssetBase archetype,
        string prefabPath,
        string rootName,
        Action<BuildResult> customize,
        Action<GameObject> beforeSave = null)
    {
        if (archetype?.BaseAsset == null)
        {
            throw new InvalidOperationException("Modular character archetype must contain a base skeletal asset.");
        }

        if (customize == null)
        {
            throw new ArgumentNullException(nameof(customize));
        }

        var asset = archetype.BaseAsset;
        var session = L2SceneSkeletalAssetBridge.CreateSession(asset);
        var bindFrame = session.CaptureBindPoseDebugFrame();
        var bonePoses = CreatureSkeletalImportUtility.BuildBonePoses(bindFrame.Bones, asset.Bones);

        var root = new GameObject(rootName);
        try
        {
            var skeletonRoot = new GameObject("Skeleton").transform;
            skeletonRoot.SetParent(root.transform, false);

            var boneTransforms = CreatureSkeletalImportUtility.CreateBoneHierarchy(asset.Bones, bonePoses, skeletonRoot);
            var rootBone = CreatureSkeletalImportUtility.ResolveRootBone(asset.Bones, boneTransforms);
            var slotBindings = BuildSlotBindings(root.transform, archetype.Slots);

            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = archetype.AnimatorController;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            root.AddComponent<L2AnimationNotifyReceiver>();

            customize(new BuildResult
            {
                Root = root,
                SkeletonRoot = skeletonRoot,
                RootBone = rootBone,
                Bones = boneTransforms,
                SlotBindings = slotBindings,
                Animator = animator
            });

            beforeSave?.Invoke(root);
            L2AssetManager.EnsureParentFolderExists(prefabPath);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    private static L2ModularSkeletalCharacterBehaviour.SlotBinding[] BuildSlotBindings(
        Transform root,
        IEnumerable<L2CharacterSlotCatalogData> slots)
    {
        var slotNames = (slots ?? Array.Empty<L2CharacterSlotCatalogData>())
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.SlotName))
            .Select(x => x.SlotName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var bindings = new List<L2ModularSkeletalCharacterBehaviour.SlotBinding>(slotNames.Length);
        foreach (var slotName in slotNames)
        {
            var slotRoot = new GameObject(slotName).transform;
            slotRoot.SetParent(root, false);
            bindings.Add(new L2ModularSkeletalCharacterBehaviour.SlotBinding
            {
                SlotName = slotName,
                Root = slotRoot
            });
        }

        return bindings.ToArray();
    }
}
