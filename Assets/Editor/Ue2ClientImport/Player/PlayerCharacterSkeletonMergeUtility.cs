using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using UnityEngine;

internal static class PlayerCharacterSkeletonMergeUtility
{
    public static SceneSkeletalAsset BuildCanonicalAsset(
        SceneSkeletalAsset baseAsset,
        IEnumerable<SceneSkeletalSkeleton> additionalSkeletons)
    {
        if (baseAsset == null)
        {
            throw new ArgumentNullException(nameof(baseAsset));
        }

        var mergedSkeleton = MergeSkeletons(
            baseAsset.Skeleton,
            additionalSkeletons ?? Array.Empty<SceneSkeletalSkeleton>());
        if (mergedSkeleton.Bones.Count == baseAsset.Skeleton.Bones.Count)
        {
            return baseAsset;
        }

        var remappedMesh = PlayerCharacterPartAssetBuilder.RemapGeometryWeights(
            baseAsset.Mesh,
            baseAsset.Skeleton,
            mergedSkeleton);
        return new SceneSkeletalAsset
        {
            PackagePath = baseAsset.PackagePath,
            MeshExportIndex = baseAsset.MeshExportIndex,
            MeshObjectName = baseAsset.MeshObjectName,
            AnimationObjectName = baseAsset.AnimationObjectName,
            Source = baseAsset.Source,
            Details = $"{baseAsset.Details}\r\nMode=CanonicalPlayerSupersetSkeleton",
            Skeleton = mergedSkeleton,
            Mesh = remappedMesh,
            AnimationSet = baseAsset.AnimationSet,
            MaterialBindings = baseAsset.MaterialBindings,
            PrimaryTextureReference = baseAsset.PrimaryTextureReference,
            UsedTextures = baseAsset.UsedTextures,
            RoutingProfiles = baseAsset.RoutingProfiles,
            ConsumerWarnings = baseAsset.ConsumerWarnings,
            RequiresExplicitConsumerRouting = baseAsset.RequiresExplicitConsumerRouting
        };
    }

    public static SceneSkeletalSkeleton MergeSkeletons(
        SceneSkeletalSkeleton baseSkeleton,
        IEnumerable<SceneSkeletalSkeleton> additionalSkeletons)
    {
        if (baseSkeleton == null)
        {
            throw new ArgumentNullException(nameof(baseSkeleton));
        }

        var mergedBones = baseSkeleton.Bones
            .OrderBy(x => x.Index)
            .Select((bone, index) => CloneBone(bone, index, bone.ParentIndex))
            .ToList();

        foreach (var skeleton in additionalSkeletons ?? Array.Empty<SceneSkeletalSkeleton>())
        {
            if (skeleton?.Bones == null || skeleton.Bones.Count == 0)
            {
                continue;
            }

            var sourceBones = skeleton.Bones
                .OrderBy(x => x.Index)
                .ToArray();
            var sourceRemap = new Dictionary<int, int>();
            var sourceUsedTargetIndices = new HashSet<int>();
            for (var boneIndex = 0; boneIndex < sourceBones.Length; boneIndex++)
            {
                EnsureBoneIncluded(
                    boneIndex,
                    sourceBones,
                    mergedBones,
                    sourceRemap,
                    sourceUsedTargetIndices);
            }
        }

        return new SceneSkeletalSkeleton
        {
            Name = baseSkeleton.Name,
            Bones = mergedBones
        };
    }

    private static int EnsureBoneIncluded(
        int sourceBoneIndex,
        SceneSkeletalBone[] sourceBones,
        IList<SceneSkeletalBone> mergedBones,
        IDictionary<int, int> sourceRemap,
        ISet<int> sourceUsedTargetIndices)
    {
        if (sourceRemap.TryGetValue(sourceBoneIndex, out var existingIndex))
        {
            return existingIndex;
        }

        var sourceBone = sourceBones[sourceBoneIndex];
        var parentIndex = -1;
        if (sourceBone.ParentIndex >= 0 && sourceBone.ParentIndex < sourceBones.Length && sourceBone.ParentIndex != sourceBoneIndex)
        {
            parentIndex = EnsureBoneIncluded(
                sourceBone.ParentIndex,
                sourceBones,
                mergedBones,
                sourceRemap,
                sourceUsedTargetIndices);
        }

        for (var index = 0; index < mergedBones.Count; index++)
        {
            var candidate = mergedBones[index];
            if (!sourceUsedTargetIndices.Contains(index) &&
                candidate.ParentIndex == parentIndex &&
                string.Equals(candidate.Name, sourceBone.Name, StringComparison.OrdinalIgnoreCase))
            {
                sourceRemap[sourceBoneIndex] = index;
                sourceUsedTargetIndices.Add(index);
                return index;
            }
        }

        var mergedIndex = mergedBones.Count;
        var mergedBone = CloneBone(sourceBone, mergedIndex, parentIndex);
        mergedBones.Add(mergedBone);
        sourceRemap[sourceBoneIndex] = mergedIndex;
        sourceUsedTargetIndices.Add(mergedIndex);
        return mergedIndex;
    }

    private static SceneSkeletalBone CloneBone(SceneSkeletalBone source, int index, int parentIndex)
    {
        return new SceneSkeletalBone
        {
            Index = index,
            Name = source.Name,
            ParentIndex = parentIndex,
            RawBindPosition = source.RawBindPosition,
            RawBindRotation = source.RawBindRotation,
            StoredOrigLocation = source.StoredOrigLocation,
            StoredOrigQuaternion = source.StoredOrigQuaternion,
            PostQuaternion = source.PostQuaternion,
            IsRoot = parentIndex < 0,
            DontInvertRoot = source.DontInvertRoot
        };
    }
}

internal static class PlayerCharacterHairBinding
{
    public static string ResolveRootAttachment(string slotName, string[] boneNames, int[] parentIndices)
    {
        return slotName == "Hair" &&
               boneNames.Length > 0 &&
               parentIndices.Length > 0 &&
               parentIndices[0] < 0 &&
               string.Equals(boneNames[0], "Hair01", StringComparison.OrdinalIgnoreCase)
            ? "Bip01_head"
            : null;
    }

    public static void AttachRootsToHead(
        Transform skeletonRoot,
        Transform[] bones,
        IEnumerable<string> hairRootNames)
    {
        var roots = hairRootNames.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (roots.Length == 0)
        {
            return;
        }

        var head = bones.Single(x => string.Equals(x.name, "Bip01_head", StringComparison.OrdinalIgnoreCase));
        foreach (var rootName in roots)
        {
            var hairRoot = bones.Single(x =>
                x.parent == skeletonRoot &&
                string.Equals(x.name, rootName, StringComparison.OrdinalIgnoreCase));
            hairRoot.SetParent(head, true);
        }
    }
}
