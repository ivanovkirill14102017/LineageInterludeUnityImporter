using System;
using UnityEngine;

public abstract class L2ModularCharacterArchetypeAssetBase : ScriptableObject
{
    public string ArchetypeName;
    public L2SkeletalCharacterAsset BaseAsset;
    public RuntimeAnimatorController AnimatorController;
    public L2CharacterSlotCatalogData[] Slots = Array.Empty<L2CharacterSlotCatalogData>();
}

[Serializable]
public sealed class L2CharacterSlotCatalogData
{
    public string SlotName;
    public int DefaultVariantIndex;
    public L2CharacterVariantData[] Variants = Array.Empty<L2CharacterVariantData>();
}

[Serializable]
public sealed class L2CharacterVariantData
{
    public string VariantKey;
    public string DisplayName;
    public int VariantId = -1;
    public int AuxVariantId = -1;
    public L2CharacterVariantPartData[] Parts = Array.Empty<L2CharacterVariantPartData>();
}

[Serializable]
public sealed class L2CharacterVariantPartData
{
    public string Name;
    public Mesh Mesh;
    public Material[] Materials = Array.Empty<Material>();
}
