using System.Linq;
using L2Viewer.SceneDomain.Models;
using UnityEngine;

internal static class L2SkeletalCharacterAssetFactory
{
    public static L2SkeletalCharacterAsset Build(string characterName, SceneSkeletalAsset source)
    {
        var asset = ScriptableObject.CreateInstance<L2SkeletalCharacterAsset>();
        asset.CharacterName = characterName;
        asset.SourcePackagePath = source.PackagePath ?? string.Empty;
        asset.MeshExportIndex = source.MeshExportIndex;
        asset.MeshObjectName = source.MeshObjectName ?? characterName;
        asset.AnimationObjectName = source.AnimationObjectName ?? string.Empty;
        asset.PrimaryTextureReference = source.PrimaryTextureReference ?? string.Empty;
        asset.BoundsMin = CreatureSkeletalImportUtility.ToUnityRaw(source.Mesh.BoundsMin);
        asset.BoundsMax = CreatureSkeletalImportUtility.ToUnityRaw(source.Mesh.BoundsMax);
        asset.UsedTextures = source.UsedTextures
            .Select(x => new L2SkeletalTextureRefData
            {
                Reference = x.Reference,
                ResolvedPackagePath = x.ResolvedPackagePath
            })
            .ToArray();
        asset.MaterialBindings = source.MaterialBindings
            .Select(x => new L2SkeletalMaterialBindingData
            {
                MaterialId = x.MaterialId,
                PackageName = x.PackageName,
                ObjectName = x.ObjectName,
                TextureReference = x.TextureReference,
                ResolvedPackagePath = x.ResolvedPackagePath
            })
            .ToArray();
        asset.Bones = source.Skeleton.Bones
            .OrderBy(x => x.Index)
            .Select(x => new L2SkeletalBoneData
            {
                Name = x.Name,
                ParentIndex = x.ParentIndex,
                RawBindPosition = CreatureSkeletalImportUtility.ToUnityRaw(x.RawBindPosition),
                RawBindRotation = CreatureSkeletalImportUtility.ToUnityRaw(x.RawBindRotation),
                StoredOrigLocation = CreatureSkeletalImportUtility.ToUnityRaw(x.StoredOrigLocation),
                StoredOrigQuaternion = CreatureSkeletalImportUtility.ToUnityRaw(x.StoredOrigQuaternion),
                PostQuaternion = CreatureSkeletalImportUtility.ToUnityRaw(x.PostQuaternion),
                IsRoot = x.IsRoot,
                DontInvertRoot = x.DontInvertRoot
            })
            .ToArray();
        asset.Points = source.Mesh.Points
            .Select(x => new L2SkeletalPointData
            {
                Position = CreatureSkeletalImportUtility.ToUnityRaw(x.Position)
            })
            .ToArray();
        asset.Wedges = source.Mesh.Wedges
            .Select(x => new L2SkeletalWedgeData
            {
                PointIndex = x.PointIndex,
                UV = new Vector2(x.UV.X, x.UV.Y),
                MaterialIndex = x.MaterialIndex
            })
            .ToArray();
        asset.Faces = source.Mesh.Faces
            .Select(x => new L2SkeletalFaceData
            {
                WedgeIndex0 = x.WedgeIndex0,
                WedgeIndex1 = x.WedgeIndex1,
                WedgeIndex2 = x.WedgeIndex2,
                MaterialIndex = x.MaterialIndex
            })
            .ToArray();
        asset.Weights = source.Mesh.Weights
            .Select(x => new L2SkeletalWeightData
            {
                Weight = x.Weight,
                PointIndex = x.PointIndex,
                BoneIndex = x.BoneIndex
            })
            .ToArray();
        asset.SubMeshes = source.Mesh.SubMeshes
            .Select(x => new L2SkeletalSubMeshData
            {
                MaterialId = x.MaterialId,
                FaceCount = x.FaceCount
            })
            .ToArray();
        asset.AnimationBones = source.AnimationSet.Bones
            .OrderBy(x => x.Index)
            .Select(x => new L2SkeletalAnimationBoneData
            {
                Name = x.Name,
                ParentIndex = x.ParentIndex
            })
            .ToArray();
        asset.AnimationSequences = source.AnimationSet.Sequences
            .Select(x => new L2SkeletalAnimationSequenceData
            {
                Name = x.Name,
                TotalBones = x.TotalBones,
                TrackTime = x.TrackTime,
                AnimRate = x.AnimRate,
                FirstRawFrame = x.FirstRawFrame,
                NumRawFrames = x.NumRawFrames
            })
            .ToArray();
        asset.AnimationKeys = source.AnimationSet.Keys
            .Select(x => new L2SkeletalAnimationKeyData
            {
                Position = CreatureSkeletalImportUtility.ToUnityRaw(x.Position),
                Orientation = CreatureSkeletalImportUtility.ToUnityRaw(x.Orientation),
                Time = x.Time
            })
            .ToArray();
        return asset;
    }
}
