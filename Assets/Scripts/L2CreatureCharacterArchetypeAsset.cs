using UnityEngine;

[CreateAssetMenu(menuName = "L2/Creature Character Archetype", fileName = "L2CreatureCharacterArchetype")]
public sealed class L2CreatureCharacterArchetypeAsset : L2ModularCharacterArchetypeAssetBase
{
    public string DisplayName;
    public string SkeletonReference;
    public string SkeletonUri;
}
