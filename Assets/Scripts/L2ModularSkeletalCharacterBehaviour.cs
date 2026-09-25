using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Animations;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public abstract class L2ModularSkeletalCharacterBehaviour : MonoBehaviour
{
    [Serializable]
    public sealed class SlotBinding
    {
        public string SlotName;
        public Transform Root;
    }

    [HideInInspector] public Transform SkeletonRoot;
    [HideInInspector] public Transform RootBone;
    [HideInInspector] public Transform[] Bones = Array.Empty<Transform>();
    [HideInInspector] public SlotBinding[] SlotBindings = Array.Empty<SlotBinding>();
    [HideInInspector] public Animator Animator;

    private readonly Dictionary<string, List<SkinnedMeshRenderer>> _runtimeRenderers =
        new Dictionary<string, List<SkinnedMeshRenderer>>(StringComparer.OrdinalIgnoreCase);

    protected abstract L2ModularCharacterArchetypeAssetBase GetArchetype();
    protected abstract int GetSelectedAnimationIndexValue();
    protected abstract void SetSelectedAnimationIndexValue(int index);
    protected abstract int GetSelectedSlotIndexValue(string slotName);
    protected abstract void SetSelectedSlotIndexValue(string slotName, int index);

    public string[] GetAnimationNames()
    {
        var sequences = GetArchetype()?.BaseAsset?.AnimationSequences ?? Array.Empty<L2SkeletalAnimationSequenceData>();
        return sequences
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Name))
            .Select(x => x.Name)
            .ToArray();
    }

    public string[] GetVariantDisplayNames(string slotName)
    {
        var slot = GetSlot(slotName);
        return slot?.Variants?.Select(x => x?.DisplayName ?? "<null>").ToArray() ?? Array.Empty<string>();
    }

    public void SetSelectedAnimation(int index)
    {
        SetSelectedAnimationIndexValue(Mathf.Max(0, index));
        ApplyAnimation();
    }

    public void SetSelectedVariant(string slotName, int index)
    {
        SetSelectedSlotIndexValue(slotName, Mathf.Max(0, index));
        ApplySlot(slotName);
    }

    public void ApplyAppearance()
    {
        foreach (var slot in GetArchetype()?.Slots ?? Array.Empty<L2CharacterSlotCatalogData>())
        {
            if (slot != null && !string.IsNullOrWhiteSpace(slot.SlotName))
            {
                ApplySlot(slot.SlotName);
            }
        }
    }

    public void ApplyAnimation()
    {
        var archetype = GetArchetype();
        if (Animator == null || archetype?.BaseAsset?.AnimationSequences == null)
        {
            return;
        }

        var sequences = archetype.BaseAsset.AnimationSequences;
        if (sequences.Length == 0)
        {
            return;
        }

        var selectedIndex = Mathf.Clamp(GetSelectedAnimationIndexValue(), 0, sequences.Length - 1);
        SetSelectedAnimationIndexValue(selectedIndex);
        var sequence = sequences[selectedIndex];
        if (sequence == null || string.IsNullOrWhiteSpace(sequence.Name))
        {
            return;
        }

        var stateName = ResolvePlayableStateName(sequence);
        if (string.IsNullOrWhiteSpace(stateName))
        {
            Debug.LogWarning($"[ModularCharacter] Animator state was not found for sequence '{sequence.Name}'.", this);
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying && TrySampleEditorPreview(stateName, sequence))
        {
            return;
        }
#endif

        Animator.Play(stateName, 0, 0f);
        Animator.Update(0f);
    }

    protected virtual void OnEnable()
    {
#if UNITY_EDITOR
        if (!Application.isPlaying && EditorUtility.IsPersistent(this))
        {
            return;
        }
#endif

        ApplyAppearance();
        if (Application.isPlaying)
        {
            ApplyAnimation();
        }
    }

    protected virtual void OnValidate()
    {
        if (Application.isPlaying)
        {
            ApplyAppearance();
            ApplyAnimation();
        }
    }

    protected virtual void OnDisable()
    {
    }

    private void ApplySlot(string slotName)
    {
        var slot = GetSlot(slotName);
        var binding = GetBinding(slotName);
        if (slot == null || binding?.Root == null)
        {
            return;
        }

        var variants = slot.Variants ?? Array.Empty<L2CharacterVariantData>();
        if (variants.Length == 0)
        {
            ClearSlotRenderers(slotName);
            return;
        }

        var selectedIndex = Mathf.Clamp(GetSelectedSlotIndexValue(slotName), 0, variants.Length - 1);
        SetSelectedSlotIndexValue(slotName, selectedIndex);
        var variant = variants[selectedIndex];
        var parts = variant?.Parts ?? Array.Empty<L2CharacterVariantPartData>();
        var renderers = EnsureSlotRendererCount(slotName, binding.Root, parts.Length);

        for (var i = 0; i < renderers.Count; i++)
        {
            var renderer = renderers[i];
            if (renderer == null)
            {
                continue;
            }

            if (i >= parts.Length || parts[i] == null)
            {
                AssignRuntimeMesh(renderer, null);
                renderer.sharedMaterials = Array.Empty<Material>();
                renderer.enabled = false;
                continue;
            }

            AssignRuntimeMesh(renderer, parts[i].Mesh);
            renderer.sharedMaterials = parts[i].Materials ?? Array.Empty<Material>();
            if (parts[i].UsesOwnSkeleton)
            {
                ApplyOwnSkeleton(renderer, parts[i]);
            }
            else
            {
                RemoveOwnSkeleton(renderer);
                renderer.rootBone = RootBone;
                renderer.bones = ResolvePartBones(parts[i]);
            }
            renderer.updateWhenOffscreen = true;
            renderer.localBounds = renderer.sharedMesh != null ? renderer.sharedMesh.bounds : default;
            renderer.transform.localPosition = Vector3.zero;
            renderer.transform.localRotation = Quaternion.identity;
            renderer.transform.localScale = Vector3.one;
            renderer.enabled = parts[i].Mesh != null;
        }
    }

    private Transform[] ResolvePartBones(L2CharacterVariantPartData part)
    {
        return L2SkeletalBoneBinding.Resolve(
            Bones,
            SkeletonRoot,
            part.BoneNames,
            part.BoneParentIndices,
            part.Name);
    }

    private void ApplyOwnSkeleton(SkinnedMeshRenderer renderer, L2CharacterVariantPartData part)
    {
        var names = part.BoneNames ?? Array.Empty<string>();
        var parents = part.BoneParentIndices ?? Array.Empty<int>();
        var bindPoses = part.Mesh != null ? part.Mesh.bindposes : Array.Empty<Matrix4x4>();
        if (names.Length != parents.Length || names.Length != bindPoses.Length)
        {
            throw new InvalidOperationException(
                $"Attached part '{part.Name}' has {names.Length} bones, {parents.Length} parents and {bindPoses.Length} bind poses.");
        }

        if (TryReuseOwnSkeleton(renderer, names, parents))
        {
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying && EditorUtility.IsPersistent(renderer))
        {
            return;
        }
#endif

        RemoveOwnSkeleton(renderer);

        var skeletonRoot = new GameObject("PartSkeleton").transform;
        skeletonRoot.SetParent(renderer.transform, false);
        var bones = new Transform[names.Length];
        for (var index = 0; index < names.Length; index++)
        {
            bones[index] = new GameObject(names[index]).transform;
        }

        Transform rootBone = null;
        for (var index = 0; index < bones.Length; index++)
        {
            var parentIndex = parents[index];
            var parent = parentIndex >= 0 && parentIndex < bones.Length
                ? bones[parentIndex]
                : skeletonRoot;
            var localBindMatrix = parentIndex >= 0 && parentIndex < bindPoses.Length
                ? bindPoses[parentIndex] * bindPoses[index].inverse
                : bindPoses[index].inverse;
            bones[index].SetParent(parent, false);
            bones[index].localPosition = localBindMatrix.GetColumn(3);
            bones[index].localRotation = localBindMatrix.rotation;
            bones[index].localScale = localBindMatrix.lossyScale;
            if (parentIndex < 0 && rootBone == null)
            {
                rootBone = bones[index];
            }
        }

        renderer.rootBone = rootBone ?? skeletonRoot;
        renderer.bones = bones;
    }

    private static bool TryReuseOwnSkeleton(
        SkinnedMeshRenderer renderer,
        string[] names,
        int[] parents)
    {
        var skeletonRoot = renderer != null ? renderer.transform.Find("PartSkeleton") : null;
        var bones = renderer != null ? renderer.bones : Array.Empty<Transform>();
        if (skeletonRoot == null || bones.Length != names.Length)
        {
            return false;
        }

        Transform rootBone = null;
        for (var index = 0; index < bones.Length; index++)
        {
            var bone = bones[index];
            var expectedParent = parents[index] >= 0 && parents[index] < bones.Length
                ? bones[parents[index]]
                : skeletonRoot;
            if (bone == null ||
                !string.Equals(bone.name, names[index], StringComparison.Ordinal) ||
                bone.parent != expectedParent)
            {
                return false;
            }

            if (parents[index] < 0 && rootBone == null)
            {
                rootBone = bone;
            }
        }

        renderer.rootBone = rootBone ?? skeletonRoot;
        return true;
    }

    private void RemoveOwnSkeleton(SkinnedMeshRenderer renderer)
    {
        var skeleton = renderer != null ? renderer.transform.Find("PartSkeleton") : null;
        if (skeleton == null)
        {
            return;
        }

#if UNITY_EDITOR
        if (!Application.isPlaying && EditorUtility.IsPersistent(skeleton))
        {
            return;
        }
#endif

        if (Application.isPlaying)
        {
            Destroy(skeleton.gameObject);
        }
        else
        {
            DestroyImmediate(skeleton.gameObject);
        }
    }

    private void ClearSlotRenderers(string slotName)
    {
        if (!_runtimeRenderers.TryGetValue(slotName, out var renderers))
        {
            return;
        }

        foreach (var renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            AssignRuntimeMesh(renderer, null);
            renderer.sharedMaterials = Array.Empty<Material>();
            renderer.enabled = false;
        }
    }

    private List<SkinnedMeshRenderer> EnsureSlotRendererCount(string slotName, Transform root, int count)
    {
        if (!_runtimeRenderers.TryGetValue(slotName, out var renderers))
        {
            renderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToList();
            _runtimeRenderers[slotName] = renderers;
        }

        while (renderers.Count < count)
        {
            var child = new GameObject($"{slotName}_{renderers.Count:D2}");
            child.transform.SetParent(root, false);
            var renderer = child.AddComponent<SkinnedMeshRenderer>();
            renderers.Add(renderer);
        }

        for (var i = renderers.Count - 1; i >= count; i--)
        {
            var renderer = renderers[i];
            if (renderer != null)
            {
                if (Application.isPlaying)
                {
                    Destroy(renderer.gameObject);
                }
                else
                {
                    DestroyImmediate(renderer.gameObject);
                }
            }

            renderers.RemoveAt(i);
        }

        return renderers;
    }

    protected L2CharacterSlotCatalogData GetSlot(string slotName)
    {
        return GetArchetype()?.Slots?.FirstOrDefault(x => string.Equals(x?.SlotName, slotName, StringComparison.OrdinalIgnoreCase));
    }

    private SlotBinding GetBinding(string slotName)
    {
        return SlotBindings?.FirstOrDefault(x => string.Equals(x?.SlotName, slotName, StringComparison.OrdinalIgnoreCase));
    }

    private string ResolvePlayableStateName(L2SkeletalAnimationSequenceData sequence)
    {
        if (sequence == null || Animator == null)
        {
            return null;
        }

        var archetype = GetArchetype();
        var candidates = new[]
        {
            sequence.Name,
            $"Base Layer.{sequence.Name}",
            $"{archetype?.BaseAsset?.CharacterName}_{SanitizeName(sequence.Name)}",
            $"Base Layer.{archetype?.BaseAsset?.CharacterName}_{SanitizeName(sequence.Name)}"
        }
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Distinct(StringComparer.Ordinal);

        foreach (var candidate in candidates)
        {
            if (Animator.HasState(0, Animator.StringToHash(candidate)))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string SanitizeName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray();
        return new string(chars);
    }

    private void AssignRuntimeMesh(SkinnedMeshRenderer renderer, Mesh sourceMesh)
    {
        if (renderer == null)
        {
            return;
        }

        if (renderer.sharedMesh == sourceMesh)
        {
            return;
        }

        renderer.sharedMesh = null;
        renderer.sharedMesh = sourceMesh;
    }

#if UNITY_EDITOR
    private bool TrySampleEditorPreview(string stateName, L2SkeletalAnimationSequenceData sequence)
    {
        if (string.IsNullOrWhiteSpace(stateName) || sequence == null)
        {
            return false;
        }

        var clip = ResolveEditorPreviewClip(stateName, sequence);
        if (clip == null)
        {
            return false;
        }

        if (!AnimationMode.InAnimationMode())
        {
            AnimationMode.StartAnimationMode();
        }

        AnimationMode.BeginSampling();
        try
        {
            AnimationMode.SampleAnimationClip(gameObject, clip, 0f);
        }
        finally
        {
            AnimationMode.EndSampling();
        }

        SceneView.RepaintAll();
        EditorApplication.QueuePlayerLoopUpdate();
        return true;
    }

    private AnimationClip ResolveEditorPreviewClip(string stateName, L2SkeletalAnimationSequenceData sequence)
    {
        var controller = GetArchetype()?.AnimatorController as AnimatorController;
        if (controller == null)
        {
            return null;
        }

        var shortStateName = stateName.StartsWith("Base Layer.", StringComparison.Ordinal)
            ? stateName.Substring("Base Layer.".Length)
            : stateName;

        foreach (var layer in controller.layers)
        {
            var clip = FindClipInStateMachine(layer.stateMachine, shortStateName);
            if (clip != null)
            {
                return clip;
            }
        }

        var sanitizedName = SanitizeName(sequence.Name);
        return controller.animationClips.FirstOrDefault(clip =>
            clip != null &&
            clip.name.EndsWith($"_{sanitizedName}", StringComparison.OrdinalIgnoreCase));
    }

    private static AnimationClip FindClipInStateMachine(AnimatorStateMachine stateMachine, string stateName)
    {
        if (stateMachine == null || string.IsNullOrWhiteSpace(stateName))
        {
            return null;
        }

        foreach (var childState in stateMachine.states)
        {
            if (string.Equals(childState.state?.name, stateName, StringComparison.Ordinal))
            {
                return childState.state.motion as AnimationClip;
            }
        }

        foreach (var childStateMachine in stateMachine.stateMachines)
        {
            var clip = FindClipInStateMachine(childStateMachine.stateMachine, stateName);
            if (clip != null)
            {
                return clip;
            }
        }

        return null;
    }
#endif
}
