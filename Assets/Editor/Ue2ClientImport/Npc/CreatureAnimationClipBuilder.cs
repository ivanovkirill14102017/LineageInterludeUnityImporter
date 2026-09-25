using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using UnityEditor;
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

    public static ClipBuildInfo[] Build(L2SkeletalCharacterAsset asset, string[] sequenceNames, Action<string> log, out string notes)
    {
        if (sequenceNames == null || sequenceNames.Length == 0)
        {
            notes = "animation clips were not created because the asset has no sequences.";
            return Array.Empty<ClipBuildInfo>();
        }

        var clipFolder = L2AssetManager.UnrealAnimationsRoot;
        L2AssetManager.EnsureFolderExists(clipFolder);
        var animationReference = $"{Path.GetFileNameWithoutExtension(asset.SourcePackagePath)}.{asset.AnimationObjectName}";
        var sequenceBuilds = sequenceNames
            .Select(sequenceName => asset.AnimationSequences.FirstOrDefault(
                x => string.Equals(x.Name, sequenceName, StringComparison.OrdinalIgnoreCase)))
            .Where(sequence => sequence != null)
            .Select(sequence => new
            {
                Sequence = sequence,
                Path = L2AssetManager.BuildClientPackageAssetPath(
                    clipFolder,
                    $"{animationReference}.{CreatureSkeletalImportUtility.SanitizeName(sequence.Name)}",
                    "AN",
                    "anim",
                    "SkeletalAnimations")
            })
            .ToArray();
        var existingClips = sequenceBuilds.ToDictionary(
            x => x.Path,
            x => AssetDatabase.LoadAssetAtPath<AnimationClip>(x.Path),
            StringComparer.OrdinalIgnoreCase);
        var requiresBuild = existingClips.Values.Any(x => x == null);
        var session = requiresBuild ? L2SceneSkeletalAssetBridge.CreateSession(asset) : null;
        var bindPoses = requiresBuild
            ? CreatureSkeletalImportUtility.BuildBonePoses(session.CaptureBindPoseDebugFrame().Bones, asset.Bones)
            : null;
        var clipInfos = new List<ClipBuildInfo>();
        foreach (var build in sequenceBuilds)
        {
            var sequence = build.Sequence;
            if (existingClips[build.Path] is { } existingClip)
            {
                clipInfos.Add(new ClipBuildInfo(build.Path, existingClip));
                continue;
            }

            var clip = new AnimationClip
            {
                name = $"{asset.CharacterName}_{CreatureSkeletalImportUtility.SanitizeName(sequence.Name)}",
                wrapMode = sequence.SuggestedLoop ? WrapMode.Loop : WrapMode.Once
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
            CreatureSkeletalImportUtility.SetClipLoop(clip, sequence.SuggestedLoop);
            AttachAnimationEvents(clip, sequence);

            var clipAsset = UnityAssetDatabaseUtility.CreateAssetIfMissing(clip, build.Path);
            clipInfos.Add(new ClipBuildInfo(build.Path, clipAsset));
            log?.Invoke($"[SkinnedPOC] AnimationClip ready: {build.Path}");
        }

        notes = clipInfos.Count > 0
            ? $"clips baked from SceneDomain skeletal samples for sequences: {string.Join(", ", clipInfos.Select(x => x.Clip.name))}."
            : "no clips were baked on Unity side.";
        return clipInfos.ToArray();
    }

    private static void AttachAnimationEvents(AnimationClip clip, L2SkeletalAnimationSequenceData sequence)
    {
        if (clip == null)
        {
            return;
        }

        var events = (sequence?.Notifies ?? Array.Empty<L2SkeletalAnimationNotifyData>())
            .Select((notify, index) => BuildAnimationEvent(notify, index))
            .Where(x => x != null)
            .ToArray();

        AnimationUtility.SetAnimationEvents(clip, events);
    }

    private static AnimationEvent BuildAnimationEvent(L2SkeletalAnimationNotifyData notify, int index)
    {
        if (notify == null)
        {
            return null;
        }

        return new AnimationEvent
        {
            time = Mathf.Max(0f, notify.Time),
            functionName = nameof(L2AnimationNotifyReceiver.OnL2AnimationNotify),
            stringParameter = BuildNotifyPayload(notify, index)
        };
    }

    private static string BuildNotifyPayload(L2SkeletalAnimationNotifyData notify, int index)
    {
        return string.Join("|", new[]
        {
            index.ToString(),
            notify.Time.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
            SanitizePayloadSegment(notify.FunctionName),
            SanitizePayloadSegment(notify.NotifyClassName),
            SanitizePayloadSegment(notify.NotifyObjectName),
            SanitizePayloadSegment(notify.ExtraText),
            notify.IsCombatImpact ? "1" : "0",
            notify.IsProjectileRelease ? "1" : "0",
            notify.IsSoundCue ? "1" : "0"
        });
    }

    private static string SanitizePayloadSegment(string value)
    {
        return string.IsNullOrEmpty(value)
            ? string.Empty
            : value.Replace("|", "/").Replace("\r", " ").Replace("\n", " ");
    }
}
