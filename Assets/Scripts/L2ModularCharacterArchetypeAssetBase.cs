using System;
using System.Linq;
using UnityEngine;

public abstract class L2ModularCharacterArchetypeAssetBase : ScriptableObject
{
    public string ArchetypeName;
    public L2SkeletalCharacterAsset BaseAsset;
    public RuntimeAnimatorController AnimatorController;
    public L2CharacterSlotCatalogData[] Slots = Array.Empty<L2CharacterSlotCatalogData>();
}

[Serializable]
public sealed class L2CharacterSlotBinding
{
    public string SlotName;
    public Transform Root;
}

public enum L2WeaponAnimationClass
{
    None,
    Hand,
    OneHanded,
    TwoHanded,
    Bow,
    Dual,
    Pole,
    Fishing
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
    public L2WeaponAnimationClass WeaponAnimationClass;
    public uint RawWeaponType;
    public uint RawHandness;
    public L2CharacterVariantPartData[] Parts = Array.Empty<L2CharacterVariantPartData>();
}

[Serializable]
public sealed class L2CharacterVariantPartData
{
    public string Name;
    public Mesh Mesh;
    public Material[] Materials = Array.Empty<Material>();
    public string[] BoneNames = Array.Empty<string>();
    public int[] BoneParentIndices = Array.Empty<int>();
    public bool UsesOwnSkeleton;
}
public static class L2SkeletalBoneBinding
{
    public static Transform[] Resolve(
        Transform[] targetBones,
        Transform skeletonRoot,
        string[] sourceNames,
        int[] sourceParentIndices,
        string partName)
    {
        targetBones = targetBones ?? Array.Empty<Transform>();
        sourceNames = sourceNames ?? Array.Empty<string>();
        if (sourceNames.Length == targetBones.Length &&
            sourceNames.Select((name, index) =>
                    string.Equals(name, targetBones[index].name, StringComparison.OrdinalIgnoreCase))
                .All(x => x))
        {
            return targetBones;
        }

        sourceParentIndices = sourceParentIndices ?? Array.Empty<int>();
        if (sourceParentIndices.Length != sourceNames.Length)
        {
            throw new InvalidOperationException(
                $"Part '{partName}' has {sourceNames.Length} bone names but {sourceParentIndices.Length} parent indices.");
        }

        var candidatesByName = targetBones
            .GroupBy(x => x.name, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(x => x.Key, x => x.ToArray(), StringComparer.OrdinalIgnoreCase);
        var resolved = new Transform[sourceNames.Length];
        var used = new System.Collections.Generic.HashSet<Transform>();

        Transform ResolveBone(int index)
        {
            if (resolved[index] != null)
            {
                return resolved[index];
            }

            var parentIndex = sourceParentIndices[index];
            if (parentIndex >= sourceNames.Length || parentIndex == index)
            {
                throw new InvalidOperationException(
                    $"Part '{partName}' bone '{sourceNames[index]}' has invalid parent index {parentIndex}.");
            }

            var expectedParent = parentIndex >= 0 ? ResolveBone(parentIndex) : skeletonRoot;
            if (!candidatesByName.TryGetValue(sourceNames[index], out var candidates))
            {
                throw new InvalidOperationException(
                    $"Part '{partName}' bone '{sourceNames[index]}' does not exist in the canonical skeleton.");
            }

            var candidate = candidates.FirstOrDefault(x => !used.Contains(x) && x.parent == expectedParent);
            if (candidate == null)
            {
                var parentName = expectedParent != null ? expectedParent.name : "<root>";
                throw new InvalidOperationException(
                    $"Part '{partName}' bone '{sourceNames[index]}' has no canonical match below parent '{parentName}'.");
            }

            resolved[index] = candidate;
            used.Add(candidate);
            return candidate;
        }

        for (var index = 0; index < sourceNames.Length; index++)
        {
            ResolveBone(index);
        }

        return resolved;
    }
}
