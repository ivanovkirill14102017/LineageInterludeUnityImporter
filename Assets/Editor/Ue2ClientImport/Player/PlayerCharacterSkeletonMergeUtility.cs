using System;
using System.Collections.Generic;
using System.Linq;
using L2Viewer.SceneDomain.Models;

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
        var mergedBoneIndexByName = mergedBones
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.First().Index, StringComparer.OrdinalIgnoreCase);

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
            for (var boneIndex = 0; boneIndex < sourceBones.Length; boneIndex++)
            {
                EnsureBoneIncluded(
                    boneIndex,
                    sourceBones,
                    mergedBones,
                    mergedBoneIndexByName,
                    sourceRemap);
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
        ICollection<SceneSkeletalBone> mergedBones,
        IDictionary<string, int> mergedBoneIndexByName,
        IDictionary<int, int> sourceRemap)
    {
        if (sourceRemap.TryGetValue(sourceBoneIndex, out var existingIndex))
        {
            return existingIndex;
        }

        var sourceBone = sourceBones[sourceBoneIndex];
        if (!string.IsNullOrWhiteSpace(sourceBone.Name) &&
            mergedBoneIndexByName.TryGetValue(sourceBone.Name, out existingIndex))
        {
            sourceRemap[sourceBoneIndex] = existingIndex;
            return existingIndex;
        }

        var parentIndex = -1;
        if (sourceBone.ParentIndex >= 0 && sourceBone.ParentIndex < sourceBones.Length && sourceBone.ParentIndex != sourceBoneIndex)
        {
            parentIndex = EnsureBoneIncluded(
                sourceBone.ParentIndex,
                sourceBones,
                mergedBones,
                mergedBoneIndexByName,
                sourceRemap);
        }

        var mergedIndex = mergedBones.Count;
        var mergedBone = CloneBone(sourceBone, mergedIndex, parentIndex);
        mergedBones.Add(mergedBone);
        if (!string.IsNullOrWhiteSpace(mergedBone.Name) && !mergedBoneIndexByName.ContainsKey(mergedBone.Name))
        {
            mergedBoneIndexByName[mergedBone.Name] = mergedIndex;
        }

        sourceRemap[sourceBoneIndex] = mergedIndex;
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
