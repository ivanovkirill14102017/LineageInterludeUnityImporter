using UnityEngine;

internal static class PlayerCharacterAnimationAssetResolver
{
    public static RuntimeAnimatorController Resolve(
        L2SkeletalCharacterAsset baseAsset,
        string referenceText,
        PlayerCharacterImportProgress progress)
    {
        progress.Report("Animations", baseAsset.MeshObjectName, 0.74f);
        var sequenceNames = CreatureSkeletalImportUtility.GetAllSequenceNames(baseAsset);
        var clips = CreatureAnimationClipBuilder.Build(
            baseAsset,
            sequenceNames,
            null,
            out _);
        return CreatureAnimatorControllerBuilder.Build(
            baseAsset,
            referenceText,
            PlayerCharacterImportBuilder.PrefabOutputRoot,
            clips,
            null,
            out _);
    }
}
