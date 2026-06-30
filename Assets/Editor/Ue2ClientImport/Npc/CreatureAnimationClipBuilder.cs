using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

internal static class CreatureAnimationClipBuilder
{
    internal readonly struct ClipBuildInfo
    {
        public ClipBuildInfo(string path, AnimationClip clip)
        {
            Path = path;
            Clip = clip;
        }

        public string Path { get; }
        public AnimationClip Clip { get; }
    }

    private sealed class BoneCurves
    {
        public AnimationCurve LocalPositionX { get; } = new AnimationCurve();
        public AnimationCurve LocalPositionY { get; } = new AnimationCurve();
        public AnimationCurve LocalPositionZ { get; } = new AnimationCurve();
        public AnimationCurve LocalRotationX { get; } = new AnimationCurve();
        public AnimationCurve LocalRotationY { get; } = new AnimationCurve();
        public AnimationCurve LocalRotationZ { get; } = new AnimationCurve();
        public AnimationCurve LocalRotationW { get; } = new AnimationCurve();
    }

    public static ClipBuildInfo[] Build(L2SkeletalCharacterAsset asset, string referenceText, string assetRoot, string[] sequenceNames, Action<string> log, out string notes)
    {
        if (sequenceNames == null || sequenceNames.Length == 0)
        {
            notes = "animation clips were not created because the asset has no sequences.";
            return Array.Empty<ClipBuildInfo>();
        }

        var session = L2SceneSkeletalAssetBridge.CreateSession(asset);
        var bindFrame = session.CaptureBindPoseDebugFrame();
        var bindPoses = CreatureSkeletalImportUtility.BuildBonePoses(bindFrame.Bones, asset.Bones);
        var clipFolder = $"{assetRoot}/Animations";
        L2AssetManager.EnsureFolderExists(clipFolder);

        var clipInfos = new List<ClipBuildInfo>();
        foreach (var sequenceName in sequenceNames)
        {
            var sequence = asset.AnimationSequences.FirstOrDefault(x => string.Equals(x.Name, sequenceName, StringComparison.OrdinalIgnoreCase));
            if (sequence == null)
            {
                continue;
            }

            var clip = new AnimationClip
            {
                name = $"{asset.CharacterName}_{CreatureSkeletalImportUtility.SanitizeName(sequence.Name)}",
                wrapMode = WrapMode.Loop
            };

            var keyframesByBone = new Dictionary<int, BoneCurves>();
            var frameCount = Math.Max(1, sequence.NumRawFrames);
            for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
            {
                var debugFrame = session.CaptureAnimatedDebugFrame(sequence.Name, frameIndex);
                if (debugFrame == null)
                {
                    continue;
                }

                var poses = CreatureSkeletalImportUtility.BuildBonePoses(debugFrame.Bones, asset.Bones);
                var time = CreatureSkeletalImportUtility.GetFrameTime(sequence, frameIndex);
                for (var boneIndex = 0; boneIndex < poses.Length; boneIndex++)
                {
                    if (!keyframesByBone.TryGetValue(boneIndex, out var curves))
                    {
                        curves = new BoneCurves();
                        keyframesByBone[boneIndex] = curves;
                    }

                    curves.LocalPositionX.AddKey(time, poses[boneIndex].LocalPosition.x);
                    curves.LocalPositionY.AddKey(time, poses[boneIndex].LocalPosition.y);
                    curves.LocalPositionZ.AddKey(time, poses[boneIndex].LocalPosition.z);
                    curves.LocalRotationX.AddKey(time, poses[boneIndex].LocalRotation.x);
                    curves.LocalRotationY.AddKey(time, poses[boneIndex].LocalRotation.y);
                    curves.LocalRotationZ.AddKey(time, poses[boneIndex].LocalRotation.z);
                    curves.LocalRotationW.AddKey(time, poses[boneIndex].LocalRotation.w);
                }
            }

            for (var boneIndex = 0; boneIndex < asset.Bones.Length; boneIndex++)
            {
                if (!keyframesByBone.TryGetValue(boneIndex, out var curves))
                {
                    curves = new BoneCurves();
                    curves.LocalPositionX.AddKey(0f, bindPoses[boneIndex].LocalPosition.x);
                    curves.LocalPositionY.AddKey(0f, bindPoses[boneIndex].LocalPosition.y);
                    curves.LocalPositionZ.AddKey(0f, bindPoses[boneIndex].LocalPosition.z);
                    curves.LocalRotationX.AddKey(0f, bindPoses[boneIndex].LocalRotation.x);
                    curves.LocalRotationY.AddKey(0f, bindPoses[boneIndex].LocalRotation.y);
                    curves.LocalRotationZ.AddKey(0f, bindPoses[boneIndex].LocalRotation.z);
                    curves.LocalRotationW.AddKey(0f, bindPoses[boneIndex].LocalRotation.w);
                }

                var relativePath = CreatureSkeletalImportUtility.BuildBonePath(asset.Bones, boneIndex);
                clip.SetCurve(relativePath, typeof(Transform), "localPosition.x", curves.LocalPositionX);
                clip.SetCurve(relativePath, typeof(Transform), "localPosition.y", curves.LocalPositionY);
                clip.SetCurve(relativePath, typeof(Transform), "localPosition.z", curves.LocalPositionZ);
                clip.SetCurve(relativePath, typeof(Transform), "localRotation.x", curves.LocalRotationX);
                clip.SetCurve(relativePath, typeof(Transform), "localRotation.y", curves.LocalRotationY);
                clip.SetCurve(relativePath, typeof(Transform), "localRotation.z", curves.LocalRotationZ);
                clip.SetCurve(relativePath, typeof(Transform), "localRotation.w", curves.LocalRotationW);
            }

            clip.EnsureQuaternionContinuity();
            CreatureSkeletalImportUtility.SetClipLoop(clip, true);

            var clipPath = L2AssetManager.BuildClientPackageAssetPath(
                clipFolder,
                $"{referenceText}.{CreatureSkeletalImportUtility.SanitizeName(sequence.Name)}",
                "AN",
                "anim",
                "SkeletalAnimations");
            var clipAsset = UnityAssetDatabaseUtility.CreateOrReplaceAsset(clip, clipPath);
            clipInfos.Add(new ClipBuildInfo(clipPath, clipAsset));
            log?.Invoke($"[SkinnedPOC] AnimationClip updated: {clipPath}");
        }

        notes = clipInfos.Count > 0
            ? $"clips baked from SceneDomain skeletal samples for sequences: {string.Join(", ", clipInfos.Select(x => x.Clip.name))}."
            : "no clips were baked on Unity side.";
        return clipInfos.ToArray();
    }
}
