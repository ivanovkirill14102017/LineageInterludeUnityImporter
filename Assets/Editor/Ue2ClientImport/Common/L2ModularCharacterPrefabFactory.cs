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
        Action<GameObject> beforeSave = null,
        bool replaceExisting = false)
    {
        if (archetype?.BaseAsset == null)
        {
            throw new InvalidOperationException("Modular character archetype must contain a base skeletal asset.");
        }

        if (customize == null)
        {
            throw new ArgumentNullException(nameof(customize));
        }

        if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) != null && !replaceExisting)
        {
            return;
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
            var slotBindings = BuildSlotBindings(root.transform, archetype.Slots, boneTransforms);

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
        IEnumerable<L2CharacterSlotCatalogData> slots,
        IReadOnlyList<Transform> bones)
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
            slotRoot.SetParent(ResolveSlotParent(root, slotName, bones), false);
            bindings.Add(new L2ModularSkeletalCharacterBehaviour.SlotBinding
            {
                SlotName = slotName,
                Root = slotRoot
            });
        }

        return bindings.ToArray();
    }

    internal static Transform ResolveSlotParent(Transform root, string slotName, IReadOnlyList<Transform> bones)
    {
        if (string.Equals(slotName, "RightHand", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(slotName, "LeftRightHand", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(slotName, "Weapon", StringComparison.OrdinalIgnoreCase))
        {
            return FindBone(bones, RightHandBoneNames) ?? root;
        }

        if (string.Equals(slotName, "LeftHand", StringComparison.OrdinalIgnoreCase))
        {
            return FindBone(bones, LeftHandBoneNames) ?? root;
        }

        return root;
    }

    private static Transform FindBone(IReadOnlyList<Transform> bones, IReadOnlyList<string> candidateNames)
    {
        if (bones == null || candidateNames == null)
        {
            return null;
        }

        foreach (var candidate in candidateNames)
        {
            var normalizedCandidate = NormalizeBoneName(candidate);
            var exact = bones.FirstOrDefault(x => string.Equals(NormalizeBoneName(x?.name), normalizedCandidate, StringComparison.OrdinalIgnoreCase));
            if (exact != null)
            {
                return exact;
            }
        }

        foreach (var candidate in candidateNames)
        {
            var normalizedCandidate = NormalizeBoneName(candidate);
            var partial = bones.FirstOrDefault(x => NormalizeBoneName(x?.name).Contains(normalizedCandidate));
            if (partial != null)
            {
                return partial;
            }
        }

        return null;
    }

    private static string NormalizeBoneName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static readonly string[] RightHandBoneNames =
    {
        "rhand",
        "r hand",
        "right hand",
        "bip01 r hand",
        "bip01 rhand",
        "bip01 right hand",
        "hand r",
        "hand right",
        "weapon r",
        "rightweapon"
    };

    private static readonly string[] LeftHandBoneNames =
    {
        "lhand",
        "l hand",
        "left hand",
        "bip01 l hand",
        "bip01 lhand",
        "bip01 left hand",
        "hand l",
        "hand left",
        "weapon l",
        "leftweapon"
    };
}
