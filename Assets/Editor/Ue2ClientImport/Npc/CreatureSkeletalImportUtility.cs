using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using UnityEditor;
using UnityEngine;

internal static class CreatureSkeletalImportUtility
{
    private const float ImportedCreatureScale = 4f;
    private static readonly Quaternion UnityBasisRotation = Quaternion.AngleAxis(-90f, Vector3.right);
    private static readonly Quaternion UnityBasisRotationInverse = Quaternion.Inverse(UnityBasisRotation);

    internal readonly struct BonePose
    {
        public BonePose(Vector3 worldPosition, Quaternion worldRotation, Vector3 localPosition, Quaternion localRotation)
        {
            WorldPosition = worldPosition;
            WorldRotation = worldRotation;
            LocalPosition = localPosition;
            LocalRotation = localRotation;
        }

        public Vector3 WorldPosition { get; }
        public Quaternion WorldRotation { get; }
        public Vector3 LocalPosition { get; }
        public Quaternion LocalRotation { get; }
    }

    public static Transform[] CreateBoneHierarchy(L2SkeletalBoneData[] bones, BonePose[] bonePoses, Transform skeletonRoot)
    {
        var transforms = new Transform[bones.Length];
        for (var i = 0; i < bones.Length; i++)
        {
            var boneObject = new GameObject(string.IsNullOrWhiteSpace(bones[i].Name) ? $"Bone_{i:000}" : bones[i].Name);
            transforms[i] = boneObject.transform;
        }

        for (var i = 0; i < bones.Length; i++)
        {
            var parentIndex = bones[i].ParentIndex;
            var parent = parentIndex >= 0 && parentIndex < transforms.Length ? transforms[parentIndex] : skeletonRoot;
            transforms[i].SetParent(parent, false);
            transforms[i].localPosition = bonePoses[i].LocalPosition;
            transforms[i].localRotation = bonePoses[i].LocalRotation;
            transforms[i].localScale = Vector3.one;
        }

        return transforms;
    }

    public static BonePose[] BuildBonePoses(IReadOnlyList<SkeletalDebugBoneSnapshot> debugBones, L2SkeletalBoneData[] bones)
    {
        var result = new BonePose[bones.Length];
        var worldPositions = new Vector3[bones.Length];
        var worldRotations = new Quaternion[bones.Length];

        for (var i = 0; i < bones.Length; i++)
        {
            var source = debugBones != null && i < debugBones.Count ? debugBones[i] : null;
            worldPositions[i] = source != null ? ToUnityPosition(source.WorldLocation) : Vector3.zero;
            worldRotations[i] = source != null ? ToUnityRotation(source.WorldRotation) : Quaternion.identity;
        }

        for (var i = 0; i < bones.Length; i++)
        {
            var parentIndex = bones[i].ParentIndex;
            Vector3 localPosition;
            Quaternion localRotation;
            if (parentIndex >= 0 && parentIndex < bones.Length)
            {
                localPosition = Quaternion.Inverse(worldRotations[parentIndex]) * (worldPositions[i] - worldPositions[parentIndex]);
                localRotation = Quaternion.Inverse(worldRotations[parentIndex]) * worldRotations[i];
            }
            else
            {
                localPosition = worldPositions[i];
                localRotation = worldRotations[i];
            }

            result[i] = new BonePose(worldPositions[i], NormalizeSafe(worldRotations[i]), localPosition, NormalizeSafe(localRotation));
        }

        return result;
    }

    public static Transform ResolveRootBone(L2SkeletalBoneData[] bones, Transform[] transforms)
    {
        for (var i = 0; i < bones.Length; i++)
        {
            if (bones[i].ParentIndex < 0)
            {
                return transforms[i];
            }
        }

        return transforms.Length > 0 ? transforms[0] : null;
    }

    public static string[] GetAllSequenceNames(L2SkeletalCharacterAsset asset)
    {
        var sequences = asset.AnimationSequences ?? Array.Empty<L2SkeletalAnimationSequenceData>();
        return sequences
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name) && x.NumRawFrames > 0)
            .Select(x => x.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public static float GetFrameTime(L2SkeletalAnimationSequenceData sequence, int frameIndex)
    {
        if (sequence.AnimRate > 0.0001f)
        {
            return frameIndex / sequence.AnimRate;
        }

        if (sequence.NumRawFrames <= 1)
        {
            return 0f;
        }

        return (sequence.TrackTime <= 0.0001f ? 1f : sequence.TrackTime) * (frameIndex / (float)(sequence.NumRawFrames - 1));
    }

    public static List<(int BoneIndex, float Weight)>[] BuildWeightsByPoint(L2SkeletalCharacterAsset asset)
    {
        var pointCount = asset.Points?.Length ?? 0;
        var result = new List<(int BoneIndex, float Weight)>[pointCount];
        foreach (var weight in asset.Weights ?? Array.Empty<L2SkeletalWeightData>())
        {
            if (weight == null || weight.Weight <= 0f || weight.PointIndex < 0 || weight.PointIndex >= pointCount)
            {
                continue;
            }

            result[weight.PointIndex] ??= new List<(int BoneIndex, float Weight)>();
            result[weight.PointIndex].Add((weight.BoneIndex, weight.Weight));
        }

        return result;
    }

    public static BoneWeight GetBoneWeight(List<(int BoneIndex, float Weight)>[] weightsByPoint, int pointIndex)
    {
        if (pointIndex < 0 || pointIndex >= weightsByPoint.Length || weightsByPoint[pointIndex] == null || weightsByPoint[pointIndex].Count == 0)
        {
            return default;
        }

        var normalized = weightsByPoint[pointIndex]
            .Where(x => x.Weight > 0f && x.BoneIndex >= 0)
            .OrderByDescending(x => x.Weight)
            .Take(4)
            .ToArray();
        if (normalized.Length == 0)
        {
            return default;
        }

        var total = normalized.Sum(x => x.Weight);
        if (total <= 0.000001f)
        {
            return default;
        }

        var weight = new BoneWeight();
        for (var i = 0; i < normalized.Length; i++)
        {
            var normalizedWeight = normalized[i].Weight / total;
            switch (i)
            {
                case 0:
                    weight.boneIndex0 = normalized[i].BoneIndex;
                    weight.weight0 = normalizedWeight;
                    break;
                case 1:
                    weight.boneIndex1 = normalized[i].BoneIndex;
                    weight.weight1 = normalizedWeight;
                    break;
                case 2:
                    weight.boneIndex2 = normalized[i].BoneIndex;
                    weight.weight2 = normalizedWeight;
                    break;
                case 3:
                    weight.boneIndex3 = normalized[i].BoneIndex;
                    weight.weight3 = normalizedWeight;
                    break;
            }
        }

        return weight;
    }

    public static int[] GetMaterialIds(L2SkeletalCharacterAsset asset)
    {
        var fromSubMeshes = asset.SubMeshes?
            .Where(x => x != null)
            .Select(x => x.MaterialId)
            .Distinct()
            .ToArray();
        if (fromSubMeshes != null && fromSubMeshes.Length > 0)
        {
            return fromSubMeshes;
        }

        var fromFaces = asset.Faces?
            .Where(x => x != null)
            .Select(x => x.MaterialIndex)
            .Distinct()
            .OrderBy(x => x)
            .ToArray();
        return fromFaces != null && fromFaces.Length > 0 ? fromFaces : new[] { 0 };
    }

    public static bool TryGetPoint(L2SkeletalCharacterAsset asset, int index, out L2SkeletalPointData point)
    {
        if (asset.Points != null && index >= 0 && index < asset.Points.Length)
        {
            point = asset.Points[index];
            return point != null;
        }

        point = null;
        return false;
    }

    public static bool TryGetWedge(L2SkeletalCharacterAsset asset, int index, out L2SkeletalWedgeData wedge)
    {
        if (asset.Wedges != null && index >= 0 && index < asset.Wedges.Length)
        {
            wedge = asset.Wedges[index];
            return wedge != null;
        }

        wedge = null;
        return false;
    }

    public static string BuildBonePath(L2SkeletalBoneData[] bones, int boneIndex)
    {
        var segments = new Stack<string>();
        var current = boneIndex;
        while (current >= 0 && current < bones.Length)
        {
            segments.Push(string.IsNullOrWhiteSpace(bones[current].Name) ? $"Bone_{current:000}" : bones[current].Name);
            current = bones[current].ParentIndex;
        }

        return $"Skeleton/{string.Join("/", segments)}";
    }

    public static string SanitizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "Sequence";
        }

        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Select(x => invalid.Contains(x) || char.IsWhiteSpace(x) ? '_' : x).ToArray();
        return new string(chars);
    }

    public static bool IsPreferredDefaultStateName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.IndexOf("wait", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("idle", StringComparison.OrdinalIgnoreCase) >= 0 ||
               value.IndexOf("stand", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    public static void SetClipLoop(AnimationClip clip, bool enabled)
    {
        if (clip == null)
        {
            return;
        }

        var serializedObject = new SerializedObject(clip);
        var settings = serializedObject.FindProperty("m_AnimationClipSettings");
        if (settings == null)
        {
            return;
        }

        var loopTime = settings.FindPropertyRelative("m_LoopTime");
        if (loopTime != null)
        {
            loopTime.boolValue = enabled;
        }

        var loopBlend = settings.FindPropertyRelative("m_LoopBlend");
        if (loopBlend != null)
        {
            loopBlend.boolValue = enabled;
        }

        serializedObject.ApplyModifiedPropertiesWithoutUndo();
    }

    public static void CreateLabel(Transform root, Mesh sourceMesh, string displayLabel)
    {
        var labelObject = new GameObject("NameLabel");
        labelObject.transform.SetParent(root, false);

        var text = labelObject.AddComponent<TextMesh>();
        text.text = string.IsNullOrWhiteSpace(displayLabel) ? root.name : displayLabel;
        text.anchor = TextAnchor.LowerCenter;
        text.alignment = TextAlignment.Center;
        text.fontSize = 48;
        text.characterSize = 0.05f;
        text.color = new Color(1f, 0.95f, 0.65f, 1f);

        var height = sourceMesh != null ? sourceMesh.bounds.max.y : 0f;
        var offset = sourceMesh != null ? Mathf.Max(0.18f, sourceMesh.bounds.size.y * 0.1f) : 0.18f;
        labelObject.transform.localPosition = new Vector3(0f, height + offset, 0f);
        labelObject.transform.localRotation = Quaternion.identity;
        labelObject.transform.localScale = Vector3.one;
    }

    public static Vector3 ToUnityPosition(System.Numerics.Vector3 value)
    {
        return new Vector3(value.X, value.Z, -value.Y) * ImportedCreatureScale;
    }

    public static Vector3 ToUnityPosition(Vector3 value)
    {
        return new Vector3(value.x, value.z, -value.y) * ImportedCreatureScale;
    }

    public static Quaternion ToUnityRotation(System.Numerics.Quaternion value)
    {
        var raw = new Quaternion(value.X, value.Y, value.Z, value.W);
        return NormalizeSafe(UnityBasisRotation * raw * UnityBasisRotationInverse);
    }

    public static Quaternion NormalizeSafe(Quaternion value)
    {
        var magnitude = Mathf.Sqrt((value.x * value.x) + (value.y * value.y) + (value.z * value.z) + (value.w * value.w));
        if (magnitude <= 0.000001f)
        {
            return Quaternion.identity;
        }

        return new Quaternion(value.x / magnitude, value.y / magnitude, value.z / magnitude, value.w / magnitude);
    }

    public static Vector3 ToUnityRaw(System.Numerics.Vector3 value)
    {
        return new Vector3(value.X, value.Y, value.Z);
    }

    public static Quaternion ToUnityRaw(System.Numerics.Quaternion value)
    {
        return new Quaternion(value.X, value.Y, value.Z, value.W);
    }
}
