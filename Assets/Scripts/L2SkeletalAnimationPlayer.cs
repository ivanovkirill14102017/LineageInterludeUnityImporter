using System;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

using QuaternionN = System.Numerics.Quaternion;
using Vector3N = System.Numerics.Vector3;

[ExecuteAlways]
[DisallowMultipleComponent]
[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public sealed class L2SkeletalAnimationPlayer : MonoBehaviour
{
    private static readonly Quaternion UnityBasisRotation = Quaternion.AngleAxis(-90f, Vector3.right);
    private static readonly Quaternion UnityBasisRotationInverse = Quaternion.Inverse(UnityBasisRotation);

    public enum DiagnosticPoseMode
    {
        StaticMesh = 0,
        BindPose = 1,
        Animated = 2,
        AnimatedWithoutInterpolation = 3
    }

    public L2SkeletalCharacterAsset Asset;
    public Mesh SourceMesh;
    public DiagnosticPoseMode PoseMode = DiagnosticPoseMode.BindPose;
    public int SelectedAnimationIndex;
    public bool PlayOnEnable;
    public bool Loop = true;
    public bool PreviewInEditMode = true;
    public float PlaybackSpeed = 1f;
    public bool IsPlaying;
    public float PreviewTimeSeconds;
    public bool ShowMesh = true;
    public bool ShowSkeleton = true;
    public bool DrawBoneLabels;
    public float BoneMarkerSize = 0.03f;
    public float BoneAxisLength = 0.06f;
    public Color BoneColor = new Color(0.15f, 0.95f, 0.85f, 1f);
    public Color BoneAxisColor = new Color(1f, 0.75f, 0.2f, 1f);

    private MeshFilter _meshFilter;
    private MeshRenderer _meshRenderer;
    private Mesh _runtimeMesh;
    private ActorXSkeletalAnimationPreviewSession _session;
    private Transform _debugSkeletonRoot;
    private Transform[] _debugBoneTransforms = Array.Empty<Transform>();
    private Vector3[] _boneWorldPositions = Array.Empty<Vector3>();
    private Vector3[] _boneWorldForward = Array.Empty<Vector3>();
    private double _lastEditorSampleTime;

    public string[] GetAnimationNames()
    {
        if (Asset == null || Asset.AnimationSequences == null || Asset.AnimationSequences.Length == 0)
        {
            return Array.Empty<string>();
        }

        return Asset.AnimationSequences
            .Select((x, i) => string.IsNullOrWhiteSpace(x.Name) ? $"Animation {i}" : x.Name)
            .ToArray();
    }

    public void SetSelectedAnimation(int index, bool resetTime)
    {
        SelectedAnimationIndex = Mathf.Clamp(index, 0, Math.Max(0, (Asset?.AnimationSequences?.Length ?? 1) - 1));
        if (resetTime)
        {
            PreviewTimeSeconds = 0f;
        }

        SampleNow();
    }

    public void SetPoseMode(DiagnosticPoseMode mode, bool stopPlayback)
    {
        PoseMode = mode;
        if (stopPlayback)
        {
            IsPlaying = false;
        }

        SampleNow();
    }

    public void SetVisibility(bool showMesh, bool showSkeleton)
    {
        ShowMesh = showMesh;
        ShowSkeleton = showSkeleton;
        RefreshPresentation();
    }

    public void PlaySelected()
    {
        IsPlaying = true;
        EnsureEditorTimeInitialized();
    }

    public void Pause()
    {
        IsPlaying = false;
    }

    public void SampleStaticMesh()
    {
        PreviewTimeSeconds = 0f;
        PoseMode = DiagnosticPoseMode.StaticMesh;
        IsPlaying = false;
        SampleNow();
    }

    public void SampleBindPose()
    {
        PreviewTimeSeconds = 0f;
        PoseMode = DiagnosticPoseMode.BindPose;
        IsPlaying = false;
        SampleNow();
    }

    public void SampleNow()
    {
        if (_runtimeMesh == null || _session == null || Asset == null)
        {
            return;
        }

        switch (PoseMode)
        {
            case DiagnosticPoseMode.StaticMesh:
                ApplyMeshAndBones(
                    L2SceneSkeletalAssetBridge.BuildRawMeshData(Asset),
                    _session.CaptureBindPoseDebugFrame());
                break;
            case DiagnosticPoseMode.BindPose:
                ApplyMeshAndBones(
                    _session.BuildBindPoseMesh(),
                    _session.CaptureBindPoseDebugFrame());
                break;
            case DiagnosticPoseMode.Animated:
                SampleAnimated(interpolate: true);
                break;
            case DiagnosticPoseMode.AnimatedWithoutInterpolation:
                SampleAnimated(interpolate: false);
                break;
        }
    }

    public float GetSelectedAnimationDuration()
    {
        var sequence = GetSelectedAnimation();
        if (sequence == null)
        {
            return 0f;
        }

        return sequence.AnimRate > 0.0001f && sequence.NumRawFrames > 0
            ? sequence.NumRawFrames / sequence.AnimRate
            : Math.Max(0.001f, sequence.TrackTime);
    }

    private void OnEnable()
    {
        EnsureComponents();
        RebuildSession();
        RebuildRuntimeMesh();
        EnsureEditorTimeInitialized();
        if (PlayOnEnable && (PoseMode == DiagnosticPoseMode.Animated || PoseMode == DiagnosticPoseMode.AnimatedWithoutInterpolation))
        {
            IsPlaying = true;
        }

        RefreshPresentation();
        SampleNow();
    }

    private void OnDisable()
    {
        ReleaseRuntimeMesh();
        ReleaseSkeletonMarkers();
    }

    private void OnValidate()
    {
        PlaybackSpeed = Mathf.Max(0f, PlaybackSpeed);
        SelectedAnimationIndex = Mathf.Max(0, SelectedAnimationIndex);
        BoneMarkerSize = Mathf.Max(0.001f, BoneMarkerSize);
        BoneAxisLength = Mathf.Max(0.001f, BoneAxisLength);
        EnsureComponents();
        RebuildSession();
        RebuildRuntimeMesh();
        RefreshPresentation();
        SampleNow();
    }

    private void Update()
    {
        if (Asset == null || _session == null)
        {
            return;
        }

        if (!Application.isPlaying && !PreviewInEditMode)
        {
            return;
        }

        if (IsPlaying && (PoseMode == DiagnosticPoseMode.Animated || PoseMode == DiagnosticPoseMode.AnimatedWithoutInterpolation))
        {
            var deltaSeconds = GetDeltaSeconds();
            if (deltaSeconds > 0f)
            {
                PreviewTimeSeconds += deltaSeconds * PlaybackSpeed;
            }
        }

        SampleNow();
    }

    private void OnDrawGizmos()
    {
        DrawSkeletonGizmos(false);
    }

    private void OnDrawGizmosSelected()
    {
        DrawSkeletonGizmos(true);
    }

    private void DrawSkeletonGizmos(bool selectedOnly)
    {
        if (!ShowSkeleton || _boneWorldPositions.Length == 0)
        {
            return;
        }

        Gizmos.color = BoneColor;
        for (var i = 0; i < _boneWorldPositions.Length; i++)
        {
            var position = _boneWorldPositions[i];
            Gizmos.DrawSphere(position, BoneMarkerSize);

            var parentIndex = Asset != null && Asset.Bones != null && i < Asset.Bones.Length
                ? Asset.Bones[i].ParentIndex
                : -1;
            if (parentIndex >= 0 && parentIndex < _boneWorldPositions.Length)
            {
                Gizmos.DrawLine(position, _boneWorldPositions[parentIndex]);
            }

            if (i < _boneWorldForward.Length && _boneWorldForward[i].sqrMagnitude > 0.000001f)
            {
                Gizmos.color = BoneAxisColor;
                Gizmos.DrawLine(position, position + (_boneWorldForward[i].normalized * BoneAxisLength));
                Gizmos.color = BoneColor;
            }
        }

#if UNITY_EDITOR
        if (!selectedOnly || !DrawBoneLabels || Asset == null || Asset.Bones == null)
        {
            return;
        }

        var style = new GUIStyle(EditorStyles.miniLabel)
        {
            normal = { textColor = BoneColor }
        };

        for (var i = 0; i < _boneWorldPositions.Length; i++)
        {
            var label = i < Asset.Bones.Length ? Asset.Bones[i].Name : $"Bone {i}";
            Handles.Label(_boneWorldPositions[i], label, style);
        }
#endif
    }

    private void EnsureComponents()
    {
        if (_meshFilter == null)
        {
            _meshFilter = GetComponent<MeshFilter>();
        }

        if (_meshRenderer == null)
        {
            _meshRenderer = GetComponent<MeshRenderer>();
        }
    }

    private void RebuildSession()
    {
        _session = null;
        if (Asset == null)
        {
            return;
        }

        try
        {
            _session = L2SceneSkeletalAssetBridge.CreateSession(Asset);
        }
        catch (Exception ex)
        {
            Debug.LogException(ex, this);
        }
    }

    private bool IsPrefabAssetContext()
    {
#if UNITY_EDITOR
        return PrefabUtility.IsPartOfPrefabAsset(gameObject) || EditorUtility.IsPersistent(gameObject);
#else
        return false;
#endif
    }

    private void RefreshPresentation()
    {
        if (_meshRenderer != null)
        {
            _meshRenderer.enabled = ShowMesh;
        }

        if (_debugSkeletonRoot != null)
        {
            _debugSkeletonRoot.localPosition = Vector3.zero;
            _debugSkeletonRoot.localRotation = Quaternion.identity;
            _debugSkeletonRoot.localScale = Vector3.one;
            _debugSkeletonRoot.gameObject.SetActive(ShowSkeleton);
        }
    }

    private void ReleaseRuntimeMesh()
    {
        if (_runtimeMesh == null)
        {
            return;
        }

        if (_meshFilter != null && _meshFilter.sharedMesh == _runtimeMesh)
        {
            _meshFilter.sharedMesh = SourceMesh;
        }

        if (Application.isPlaying)
        {
            Destroy(_runtimeMesh);
        }
        else
        {
            DestroyImmediate(_runtimeMesh);
        }

        _runtimeMesh = null;
    }

    private void ReleaseSkeletonMarkers()
    {
        _debugBoneTransforms = Array.Empty<Transform>();
        _boneWorldPositions = Array.Empty<Vector3>();
        _boneWorldForward = Array.Empty<Vector3>();
        if (_debugSkeletonRoot == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(_debugSkeletonRoot.gameObject);
        }
        else
        {
            DestroyImmediate(_debugSkeletonRoot.gameObject);
        }

        _debugSkeletonRoot = null;
    }

    private void RebuildSkeletonMarkers()
    {
        var count = Asset?.Bones?.Length ?? 0;
        if (count <= 0)
        {
            ReleaseSkeletonMarkers();
            return;
        }

        if (_boneWorldPositions.Length != count)
        {
            _boneWorldPositions = new Vector3[count];
        }

        if (_boneWorldForward.Length != count)
        {
            _boneWorldForward = new Vector3[count];
        }

        EnsureDebugSkeletonObjects(count);
    }

    private void EnsureDebugSkeletonObjects(int count)
    {
        if (IsPrefabAssetContext())
        {
            ReleaseSkeletonMarkers();
            return;
        }

        if (_debugSkeletonRoot == null)
        {
            var existingRoot = transform.Find("__L2DebugSkeleton");
            if (existingRoot != null)
            {
                _debugSkeletonRoot = existingRoot;
            }
            else
            {
                var rootObject = new GameObject("__L2DebugSkeleton");
                rootObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
                _debugSkeletonRoot = rootObject.transform;
                _debugSkeletonRoot.SetParent(transform, false);
            }
        }

        if (_debugBoneTransforms.Length == count)
        {
            return;
        }

        for (var i = _debugSkeletonRoot.childCount - 1; i >= 0; i--)
        {
            if (Application.isPlaying)
            {
                Destroy(_debugSkeletonRoot.GetChild(i).gameObject);
            }
            else
            {
                DestroyImmediate(_debugSkeletonRoot.GetChild(i).gameObject);
            }
        }

        _debugBoneTransforms = new Transform[count];
        for (var i = 0; i < count; i++)
        {
            var boneObject = new GameObject($"{i:000}_{Asset.Bones[i].Name}");
            boneObject.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild;
            var boneTransform = boneObject.transform;
            boneTransform.SetParent(_debugSkeletonRoot, false);
            _debugBoneTransforms[i] = boneTransform;
        }
    }

    private void EnsureEditorTimeInitialized()
    {
#if UNITY_EDITOR
        _lastEditorSampleTime = EditorApplication.timeSinceStartup;
#endif
    }

    private float GetDeltaSeconds()
    {
        if (Application.isPlaying)
        {
            return Time.deltaTime;
        }

#if UNITY_EDITOR
        var now = EditorApplication.timeSinceStartup;
        if (_lastEditorSampleTime <= 0d)
        {
            _lastEditorSampleTime = now;
            return 0f;
        }

        var delta = (float)(now - _lastEditorSampleTime);
        _lastEditorSampleTime = now;
        return Mathf.Max(0f, delta);
#else
        return 0f;
#endif
    }

    private L2SkeletalAnimationSequenceData GetSelectedAnimation()
    {
        if (Asset == null || Asset.AnimationSequences == null || Asset.AnimationSequences.Length == 0)
        {
            return null;
        }

        if (SelectedAnimationIndex < 0 || SelectedAnimationIndex >= Asset.AnimationSequences.Length)
        {
            SelectedAnimationIndex = 0;
        }

        return Asset.AnimationSequences[SelectedAnimationIndex];
    }

    private void RebuildRuntimeMesh()
    {
        EnsureComponents();
        RebuildSkeletonMarkers();

        if (_meshFilter == null)
        {
            return;
        }

        if (_session == null)
        {
            ReleaseRuntimeMesh();
            _meshFilter.sharedMesh = null;
            return;
        }

        var templateMesh = SourceMesh;
        if (templateMesh == null)
        {
            templateMesh = L2SceneSkeletalAssetBridge.BuildUnityMesh(
                _session.BuildBindPoseMesh(),
                $"{Asset?.CharacterName ?? "L2SkeletalCharacter"}_BindPose");
            SourceMesh = templateMesh;
        }

        if (IsPrefabAssetContext())
        {
            ReleaseRuntimeMesh();
            _meshFilter.sharedMesh = templateMesh;
            RefreshPresentation();
            return;
        }

        if (_runtimeMesh == null)
        {
            _runtimeMesh = Instantiate(templateMesh);
            _runtimeMesh.name = $"{templateMesh.name}_Runtime";
        }
        else
        {
            L2SceneSkeletalAssetBridge.ApplyMeshData(_runtimeMesh, _session.BuildBindPoseMesh());
        }

        _meshFilter.sharedMesh = _runtimeMesh;
        RefreshPresentation();
    }

    private void SampleAnimated(bool interpolate)
    {
        var sequence = GetSelectedAnimation();
        if (sequence == null)
        {
            ApplyMeshAndBones(
                _session.BuildBindPoseMesh(),
                _session.CaptureBindPoseDebugFrame());
            return;
        }

        if (interpolate)
        {
            var mesh = _session.BuildAnimatedMesh(sequence.Name, PreviewTimeSeconds, Loop, out var finished);
            var debug = _session.CaptureAnimatedDebugFrameAtElapsed(sequence.Name, PreviewTimeSeconds, Loop);
            if (!Loop && finished)
            {
                IsPlaying = false;
            }

            if (mesh != null && debug != null)
            {
                ApplyMeshAndBones(mesh, debug);
            }

            return;
        }

        var frameIndex = ComputeFrameIndex(sequence, PreviewTimeSeconds, Loop);
        var frameMesh = _session.BuildAnimatedMeshFrame(sequence.Name, frameIndex);
        var frameDebug = _session.CaptureAnimatedDebugFrame(sequence.Name, frameIndex);
        if (frameMesh != null && frameDebug != null)
        {
            ApplyMeshAndBones(frameMesh, frameDebug);
        }
    }

    private void ApplyMeshAndBones(L2Viewer.PackageCore.MeshData meshData, SkeletalDebugFrameSnapshot debugFrame)
    {
        if (_runtimeMesh == null || meshData == null || debugFrame == null)
        {
            return;
        }

        L2SceneSkeletalAssetBridge.ApplyMeshData(_runtimeMesh, meshData);
        ApplyDebugFrame(debugFrame);
    }

    private void ApplyDebugFrame(SkeletalDebugFrameSnapshot debugFrame)
    {
        RebuildSkeletonMarkers();
        if (debugFrame.Bones == null || debugFrame.Bones.Count == 0)
        {
            return;
        }

        var count = Mathf.Min(debugFrame.Bones.Count, _boneWorldPositions.Length);
        for (var i = 0; i < count; i++)
        {
            var bone = debugFrame.Bones[i];
            var localPosition = ToUnityPosition(bone.WorldLocation);
            var localRotation = ToUnityRotation(bone.WorldRotation);

            if (_debugBoneTransforms.Length > i && _debugBoneTransforms[i] != null)
            {
                _debugBoneTransforms[i].localPosition = localPosition;
                _debugBoneTransforms[i].localRotation = localRotation;
                _debugBoneTransforms[i].localScale = Vector3.one;
                _boneWorldPositions[i] = _debugBoneTransforms[i].position;
                _boneWorldForward[i] = _debugBoneTransforms[i].forward;
            }
            else
            {
                _boneWorldPositions[i] = transform.TransformPoint(localPosition);
                _boneWorldForward[i] = transform.TransformDirection(localRotation * Vector3.forward);
            }
        }
    }

    private static int ComputeFrameIndex(L2SkeletalAnimationSequenceData sequence, float elapsedSeconds, bool loop)
    {
        var duration = sequence.AnimRate > 0.0001f && sequence.NumRawFrames > 0
            ? sequence.NumRawFrames / sequence.AnimRate
            : Math.Max(0.001f, sequence.TrackTime);
        var localTime = loop
            ? (elapsedSeconds < 0f ? 0f : elapsedSeconds % duration)
            : Mathf.Min(Mathf.Max(0f, elapsedSeconds), duration);

        var frameIndex = sequence.AnimRate > 0.0001f
            ? Mathf.RoundToInt(localTime * sequence.AnimRate)
            : (sequence.NumRawFrames <= 1 ? 0 : Mathf.RoundToInt((localTime / duration) * (sequence.NumRawFrames - 1)));
        return Mathf.Clamp(frameIndex, 0, Math.Max(0, sequence.NumRawFrames - 1));
    }

    private static Quaternion ToUnityRotation(QuaternionN value)
    {
        var raw = new Quaternion(value.X, value.Y, value.Z, value.W);
        var converted = UnityBasisRotation * raw * UnityBasisRotationInverse;
        return NormalizeSafe(converted);
    }

    private static Vector3 ToUnityPosition(Vector3N value)
    {
        return new Vector3(value.X, value.Z, -value.Y);
    }

    private static Quaternion NormalizeSafe(Quaternion value)
    {
        var magnitude = Mathf.Sqrt((value.x * value.x) + (value.y * value.y) + (value.z * value.z) + (value.w * value.w));
        if (magnitude <= 0.000001f)
        {
            return Quaternion.identity;
        }

        return new Quaternion(value.x / magnitude, value.y / magnitude, value.z / magnitude, value.w / magnitude);
    }
}
