using System;
using UnityEngine;

[CreateAssetMenu(menuName = "L2/Imported Skill Visual", fileName = "L2SkillVisual")]
public sealed class L2SkillVisualAsset : ScriptableObject
{
    public int SkillId;
    public string DisplayName;
    public GameObject PreviewPrefab;
    public string ResolvedEffectStem;
    public string[] ResolvedEffectStems = Array.Empty<string>();
    public string[] Warnings = Array.Empty<string>();
    public L2SkillNameEntryData[] Names = Array.Empty<L2SkillNameEntryData>();
    public L2SkillLevelData[] Levels = Array.Empty<L2SkillLevelData>();
    public L2SkillSoundData[] Sounds = Array.Empty<L2SkillSoundData>();
    public L2MobSkillTriggerData[] MobTriggers = Array.Empty<L2MobSkillTriggerData>();
    public L2SkillVisualEffectData[] Effects = Array.Empty<L2SkillVisualEffectData>();
    public L2MobSkillVisualData[] MobVisuals = Array.Empty<L2MobSkillVisualData>();
    public L2SkillVisualStageData[] Stages = Array.Empty<L2SkillVisualStageData>();
}

[Serializable]
public sealed class L2SkillNameEntryData
{
    public int SkillLevel;
    public string Name;
    public string Description;
    public string DescriptionAdd1;
    public string DescriptionAdd2;
}

[Serializable]
public sealed class L2SkillLevelData
{
    public int SkillLevel;
    public int OperType;
    public int MpConsume;
    public int CastRange;
    public int CastStyle;
    public float HitTime;
    public int IsMagic;
    public string AnimationCharacter;
    public string DescriptionToken;
    public string IconName;
    public string IconName2;
    public int IsEnchanted;
    public int EnchantedSkillId;
    public int HpConsume;
}

[Serializable]
public sealed class L2SkillSoundData
{
    public int SkillLevel;
    public string[] SpellEffectSounds = Array.Empty<string>();
    public string[] ShotEffectSounds = Array.Empty<string>();
    public string[] ExpEffectSounds = Array.Empty<string>();
    public string[] CharacterSubSounds = Array.Empty<string>();
    public string[] CharacterThrowSounds = Array.Empty<string>();
    public float SoundVolume;
    public float SoundRadius;
}

[Serializable]
public sealed class L2MobSkillTriggerData
{
    public int NpcId;
    public int SkillId;
    public string SequenceName;
    public string SkillName;
    public string NpcName;
    public string NpcClass;
}

[Serializable]
public sealed class L2MobSkillVisualData
{
    public int NpcId;
    public string NpcClass;
    public string MeshReference;
    public string MeshPackagePath;
    public string SequenceName;
    public string SequenceCategory;
    public string ActorEffectReference;
    public string ActorEffectPackagePath;
}

[Serializable]
public sealed class L2SkillVisualEffectData
{
    public string Stem;
    public string Source;
    public L2SkillVisualStageData[] Stages = Array.Empty<L2SkillVisualStageData>();
}

[Serializable]
public sealed class L2SkillVisualStageData
{
    public string StageKey;
    public int StageOrder;
    public L2SkillVisualStagePlaybackRole PlaybackRole;
    public string ObjectName;
    public string SuperClassName;
    public L2ResourceReferenceData StageReference;
    public L2ResourceLocationData StageResource;
    public L2ResourceReferenceData[] EmitterReferences = Array.Empty<L2ResourceReferenceData>();
    public L2ResourceLocationData[] EmitterResources = Array.Empty<L2ResourceLocationData>();
    public L2SkillVisualLayerData[] Layers = Array.Empty<L2SkillVisualLayerData>();
}

[Serializable]
public sealed class L2SkillVisualLayerData
{
    public int ExportIndex;
    public string ObjectName;
    public string ClassName;
    public string LayerName;
    public L2ResourceReferenceData LayerReference;
    public L2ResourceLocationData LayerResource;
    public string StaticMeshReference;
    public L2ResourceReferenceData StaticMeshResourceReference;
    public L2ResourceLocationData StaticMeshResource;
    public L2SkillVisualMeshPartData[] MeshParts = Array.Empty<L2SkillVisualMeshPartData>();
    public string TextureReference;
    public bool HasDrawStyle;
    public byte DrawStyle;
    public bool UseMeshBlendMode;
    public bool HasTextureUSubdivisions;
    public int TextureUSubdivisions;
    public bool HasTextureVSubdivisions;
    public int TextureVSubdivisions;
    public bool HasSubdivisionStart;
    public int SubdivisionStart;
    public bool HasSubdivisionEnd;
    public int SubdivisionEnd;
    public bool UseRandomSubdivision;
    public bool BlendBetweenSubdivisions;
    public L2ResourceReferenceData TextureResourceReference;
    public L2ResourceLocationData TextureResource;
    public bool HasOpacity;
    public float Opacity;
    public bool HasFadeOutStartTime;
    public float FadeOutStartTime;
    public bool FadeOut;
    public bool HasFadeInEndTime;
    public float FadeInEndTime;
    public bool FadeIn;
    public bool HasMaxParticles;
    public int MaxParticles;
    public bool HasLifetimeRange;
    public L2FloatRangeData LifetimeRange;
    public bool HasAcceleration;
    public Vector3 Acceleration;
    public bool HasStartLocationRange;
    public L2RangeVectorData StartLocationRange;
    public bool HasStartSizeRange;
    public L2RangeVectorData StartSizeRange;
    public bool HasStartVelocityRange;
    public L2RangeVectorData StartVelocityRange;
    public bool HasStartSpinRange;
    public L2RangeVectorData StartSpinRange;
    public bool HasSpinsPerSecondRange;
    public L2RangeVectorData SpinsPerSecondRange;
    public L2ParticleColorScaleData[] ColorScale = Array.Empty<L2ParticleColorScaleData>();
    public L2ParticleSizeScaleData[] SizeScale = Array.Empty<L2ParticleSizeScaleData>();
}

[Serializable]
public sealed class L2SkillVisualMeshPartData
{
    public int SubMeshIndex;
    public int MaterialId;
    public int TriangleCount;
    public string MaterialReference;
    public L2ResourceLocationData MaterialResource;
    public string PrimaryTextureReference;
    public L2ResourceLocationData PrimaryTextureResource;
}

[Serializable]
public sealed class L2ResourceReferenceData
{
    public string Reference;
    public string ClassName;
    public string PackageName;
    public string ObjectName;
}

[Serializable]
public sealed class L2ResourceLocationData
{
    public string Reference;
    public string ClassName;
    public string PackageName;
    public string ObjectName;
    public string PackagePath;
    public string ClientRelativePath;
    public string Uri;
}

[Serializable]
public sealed class L2FloatRangeData
{
    public float Min;
    public float Max;
}

[Serializable]
public sealed class L2RangeVectorData
{
    public L2FloatRangeData X;
    public L2FloatRangeData Y;
    public L2FloatRangeData Z;
}

[Serializable]
public sealed class L2ParticleColorScaleData
{
    public int ArrayIndex;
    public int Type;
    public string StructName;
    public bool HasRelativeTime;
    public float RelativeTime;
    public bool HasColor;
    public Color32 Color;
}

[Serializable]
public sealed class L2ParticleSizeScaleData
{
    public int ArrayIndex;
    public int Type;
    public string StructName;
    public bool HasRelativeTime;
    public float RelativeTime;
    public bool HasRelativeSize;
    public float RelativeSize;
}
