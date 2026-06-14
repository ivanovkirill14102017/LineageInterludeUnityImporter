using System;
using UnityEngine;

[CreateAssetMenu(menuName = "L2/Imported Skeletal Character", fileName = "L2SkeletalCharacter")]
public sealed class L2SkeletalCharacterAsset : ScriptableObject
{
    public string CharacterName;
    public string SourcePackagePath;
    public int MeshExportIndex;
    public string MeshObjectName;
    public string AnimationObjectName;
    public string PrimaryTextureReference;
    public Vector3 BoundsMin;
    public Vector3 BoundsMax;
    public L2SkeletalTextureRefData[] UsedTextures = Array.Empty<L2SkeletalTextureRefData>();
    public L2SkeletalMaterialBindingData[] MaterialBindings = Array.Empty<L2SkeletalMaterialBindingData>();
    public L2SkeletalBoneData[] Bones = Array.Empty<L2SkeletalBoneData>();
    public L2SkeletalPointData[] Points = Array.Empty<L2SkeletalPointData>();
    public L2SkeletalWedgeData[] Wedges = Array.Empty<L2SkeletalWedgeData>();
    public L2SkeletalFaceData[] Faces = Array.Empty<L2SkeletalFaceData>();
    public L2SkeletalWeightData[] Weights = Array.Empty<L2SkeletalWeightData>();
    public L2SkeletalSubMeshData[] SubMeshes = Array.Empty<L2SkeletalSubMeshData>();
    public L2SkeletalAnimationBoneData[] AnimationBones = Array.Empty<L2SkeletalAnimationBoneData>();
    public L2SkeletalAnimationSequenceData[] AnimationSequences = Array.Empty<L2SkeletalAnimationSequenceData>();
    public L2SkeletalAnimationKeyData[] AnimationKeys = Array.Empty<L2SkeletalAnimationKeyData>();
}

[Serializable]
public sealed class L2SkeletalTextureRefData
{
    public string Reference;
    public string ResolvedPackagePath;
}

[Serializable]
public sealed class L2SkeletalMaterialBindingData
{
    public int MaterialId;
    public string PackageName;
    public string ObjectName;
    public string TextureReference;
    public string ResolvedPackagePath;
}

[Serializable]
public sealed class L2SkeletalBoneData
{
    public string Name;
    public int ParentIndex = -1;
    public Vector3 RawBindPosition;
    public Quaternion RawBindRotation = Quaternion.identity;
    public Vector3 StoredOrigLocation;
    public Quaternion StoredOrigQuaternion = Quaternion.identity;
    public Quaternion PostQuaternion = Quaternion.identity;
    public bool IsRoot;
    public bool DontInvertRoot = true;
}

[Serializable]
public sealed class L2SkeletalPointData
{
    public Vector3 Position;
}

[Serializable]
public sealed class L2SkeletalWedgeData
{
    public int PointIndex;
    public Vector2 UV;
    public int MaterialIndex;
}

[Serializable]
public sealed class L2SkeletalFaceData
{
    public int WedgeIndex0;
    public int WedgeIndex1;
    public int WedgeIndex2;
    public int MaterialIndex;
}

[Serializable]
public sealed class L2SkeletalWeightData
{
    public float Weight;
    public int PointIndex;
    public int BoneIndex;
}

[Serializable]
public sealed class L2SkeletalSubMeshData
{
    public int MaterialId;
    public int FaceCount;
}

[Serializable]
public sealed class L2SkeletalAnimationBoneData
{
    public string Name;
    public int ParentIndex = -1;
}

[Serializable]
public sealed class L2SkeletalAnimationSequenceData
{
    public string Name;
    public int TotalBones;
    public float TrackTime;
    public float AnimRate;
    public int FirstRawFrame;
    public int NumRawFrames;
}

[Serializable]
public sealed class L2SkeletalAnimationKeyData
{
    public Vector3 Position;
    public Quaternion Orientation = Quaternion.identity;
    public float Time;
}
