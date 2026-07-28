using System;
using L2Viewer.SceneDomain.Models;
using UnityEngine;

[CreateAssetMenu(menuName = "L2/Player Character Archetype", fileName = "L2PlayerCharacterArchetype")]
public sealed class L2PlayerCharacterArchetypeAsset : ScriptableObject
{
    public string ArchetypeName;
    public string BaseClass;
    public string Gender;
    public string VisualFamily;
    public L2SkeletalCharacterAsset BaseAsset;
    public RuntimeAnimatorController AnimatorController;
    public L2PlayerCharacterSlotCatalogData[] Slots = Array.Empty<L2PlayerCharacterSlotCatalogData>();
}

[Serializable]
public sealed class L2PlayerCharacterSlotCatalogData
{
    public string SlotName;
    public int DefaultVariantIndex;
    public L2PlayerCharacterVariantData[] Variants = Array.Empty<L2PlayerCharacterVariantData>();
}

[Serializable]
public sealed class L2PlayerCharacterVariantData
{
    public string VariantKey;
    public string DisplayName;
    public int VariantId = -1;
    public int AuxVariantId = -1;
    public L2PlayerCharacterVariantPartData[] Parts = Array.Empty<L2PlayerCharacterVariantPartData>();
}

[Serializable]
public sealed class L2PlayerCharacterVariantPartData
{
    public string Name;
    public Mesh Mesh;
    public Material[] Materials = Array.Empty<Material>();
}
