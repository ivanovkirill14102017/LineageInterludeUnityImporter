using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

internal sealed class L2SkinnedPartVisualController
{
    private readonly MonoBehaviour _owner;
    private readonly Dictionary<string, List<SkinnedMeshRenderer>> _renderers =
        new Dictionary<string, List<SkinnedMeshRenderer>>(StringComparer.OrdinalIgnoreCase);

    public L2SkinnedPartVisualController(MonoBehaviour owner)
    {
        _owner = owner;
    }

    public void Apply(
        L2ModularCharacterArchetypeAssetBase archetype,
        L2CharacterSlotBinding[] bindings,
        Transform skeletonRoot,
        Transform rootBone,
        Transform[] bones,
        string slotName,
        int selectedIndex,
        bool visible = true)
    {
        var slot = archetype?.Slots?.FirstOrDefault(x => string.Equals(x?.SlotName, slotName, StringComparison.OrdinalIgnoreCase));
        var binding = bindings?.FirstOrDefault(x => string.Equals(x?.SlotName, slotName, StringComparison.OrdinalIgnoreCase));
        if (slot == null || binding?.Root == null)
        {
            return;
        }

        var variants = slot.Variants ?? Array.Empty<L2CharacterVariantData>();
        if (!visible || variants.Length == 0)
        {
            Clear(slotName);
            return;
        }

        var variant = variants[Mathf.Clamp(selectedIndex, 0, variants.Length - 1)];
        var parts = variant?.Parts ?? Array.Empty<L2CharacterVariantPartData>();
        var slotRenderers = EnsureRendererCount(slotName, binding.Root, parts.Length);
        for (var i = 0; i < slotRenderers.Count; i++)
        {
            var renderer = slotRenderers[i];
            if (renderer == null)
            {
                continue;
            }

            renderer.enabled = false;
            var part = i < parts.Length ? parts[i] : null;
            if (part == null || part.Mesh == null)
            {
                AssignMesh(renderer, null);
                renderer.sharedMaterials = Array.Empty<Material>();
                continue;
            }

            AssignMesh(renderer, part.Mesh);
            renderer.sharedMaterials = part.Materials ?? Array.Empty<Material>();
            if (part.UsesOwnSkeleton)
            {
                ApplyOwnSkeleton(renderer, part);
            }
            else
            {
                RemoveOwnSkeleton(renderer);
                renderer.rootBone = rootBone;
                renderer.bones = L2SkeletalBoneBinding.Resolve(
                    bones, skeletonRoot, part.BoneNames, part.BoneParentIndices, part.Name);
            }

            renderer.updateWhenOffscreen = true;
            renderer.localBounds = part.Mesh.bounds;
            renderer.transform.localPosition = Vector3.zero;
            renderer.transform.localRotation = Quaternion.identity;
            renderer.transform.localScale = Vector3.one;
            renderer.enabled = true;
        }
    }

    public void Clear(string slotName)
    {
        if (!_renderers.TryGetValue(slotName, out var slotRenderers))
        {
            return;
        }

        foreach (var renderer in slotRenderers)
        {
            if (renderer == null)
            {
                continue;
            }

            renderer.enabled = false;
            AssignMesh(renderer, null);
            renderer.sharedMaterials = Array.Empty<Material>();
        }
    }

    private List<SkinnedMeshRenderer> EnsureRendererCount(string slotName, Transform root, int count)
    {
        if (!_renderers.TryGetValue(slotName, out var slotRenderers))
        {
            slotRenderers = root.GetComponentsInChildren<SkinnedMeshRenderer>(true).ToList();
            _renderers[slotName] = slotRenderers;
        }

        while (slotRenderers.Count < count)
        {
            var child = new GameObject($"{slotName}_{slotRenderers.Count:D2}");
            child.transform.SetParent(root, false);
            slotRenderers.Add(child.AddComponent<SkinnedMeshRenderer>());
        }

        for (var i = slotRenderers.Count - 1; i >= count; i--)
        {
            var renderer = slotRenderers[i];
            if (renderer != null)
            {
                renderer.enabled = false;
                DestroyObject(renderer.gameObject);
            }

            slotRenderers.RemoveAt(i);
        }

        return slotRenderers;
    }

    private void ApplyOwnSkeleton(SkinnedMeshRenderer renderer, L2CharacterVariantPartData part)
    {
        var names = part.BoneNames ?? Array.Empty<string>();
        var parents = part.BoneParentIndices ?? Array.Empty<int>();
        var bindPoses = part.Mesh.bindposes;
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
        var partBones = new Transform[names.Length];
        for (var i = 0; i < names.Length; i++)
        {
            partBones[i] = new GameObject(names[i]).transform;
        }

        Transform firstRoot = null;
        for (var i = 0; i < partBones.Length; i++)
        {
            var parentIndex = parents[i];
            var parent = parentIndex >= 0 && parentIndex < partBones.Length
                ? partBones[parentIndex]
                : skeletonRoot;
            var localBindMatrix = parentIndex >= 0 && parentIndex < bindPoses.Length
                ? bindPoses[parentIndex] * bindPoses[i].inverse
                : bindPoses[i].inverse;
            partBones[i].SetParent(parent, false);
            partBones[i].localPosition = localBindMatrix.GetColumn(3);
            partBones[i].localRotation = localBindMatrix.rotation;
            partBones[i].localScale = localBindMatrix.lossyScale;
            if (parentIndex < 0 && firstRoot == null)
            {
                firstRoot = partBones[i];
            }
        }

        renderer.rootBone = firstRoot ?? skeletonRoot;
        renderer.bones = partBones;
    }

    private static bool TryReuseOwnSkeleton(SkinnedMeshRenderer renderer, string[] names, int[] parents)
    {
        var skeletonRoot = renderer.transform.Find("PartSkeleton");
        var bones = renderer.bones;
        if (skeletonRoot == null || bones.Length != names.Length)
        {
            return false;
        }

        Transform firstRoot = null;
        for (var i = 0; i < bones.Length; i++)
        {
            var expectedParent = parents[i] >= 0 && parents[i] < bones.Length
                ? bones[parents[i]]
                : skeletonRoot;
            if (bones[i] == null || bones[i].name != names[i] || bones[i].parent != expectedParent)
            {
                return false;
            }

            if (parents[i] < 0 && firstRoot == null)
            {
                firstRoot = bones[i];
            }
        }

        renderer.rootBone = firstRoot ?? skeletonRoot;
        return true;
    }

    private void RemoveOwnSkeleton(SkinnedMeshRenderer renderer)
    {
        var skeleton = renderer.transform.Find("PartSkeleton");
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

        DestroyObject(skeleton.gameObject);
    }

    private void DestroyObject(GameObject target)
    {
        if (_owner == null || target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(target);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(target);
        }
    }

    private static void AssignMesh(SkinnedMeshRenderer renderer, Mesh mesh)
    {
        if (renderer.sharedMesh == mesh)
        {
            return;
        }

        renderer.sharedMesh = null;
        renderer.sharedMesh = mesh;
    }
}
