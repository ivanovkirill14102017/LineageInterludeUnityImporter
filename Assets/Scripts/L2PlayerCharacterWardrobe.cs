using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2PlayerCharacterWardrobe : MonoBehaviour
{
    [Serializable]
    public sealed class SlotBinding
    {
        public string SlotName;
        public Transform Root;
    }

    public L2PlayerCharacterArchetypeAsset Archetype;
    [HideInInspector] public Transform SkeletonRoot;
    [HideInInspector] public Transform RootBone;
    [HideInInspector] public Transform[] Bones = Array.Empty<Transform>();
    [HideInInspector] public SlotBinding[] SlotBindings = Array.Empty<SlotBinding>();
    [HideInInspector] public Animator Animator;
    public int SelectedAnimationIndex;
    public int SelectedFaceIndex;
    public int SelectedHairIndex;
    public int SelectedChestIndex;
    public int SelectedLegsIndex;
    public int SelectedGlovesIndex;
    public int SelectedFeetIndex;

    private readonly Dictionary<string, List<SkinnedMeshRenderer>> _runtimeRenderers =
        new Dictionary<string, List<SkinnedMeshRenderer>>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<SkinnedMeshRenderer, Mesh> _runtimeMeshes =
        new Dictionary<SkinnedMeshRenderer, Mesh>();

    public string[] GetAnimationNames()
    {
        var sequences = Archetype?.BaseAsset?.AnimationSequences ?? Array.Empty<L2SkeletalAnimationSequenceData>();
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
        SelectedAnimationIndex = Mathf.Max(0, index);
        ApplyAnimation();
    }

    public void ApplyAppearance()
    {
        if (Archetype == null)
        {
            return;
        }

        ApplySlot("Face", SelectedFaceIndex);
        ApplySlot("Hair", SelectedHairIndex);
        ApplySlot("Chest", SelectedChestIndex);
        ApplySlot("Legs", SelectedLegsIndex);
        ApplySlot("Gloves", SelectedGlovesIndex);
        ApplySlot("Feet", SelectedFeetIndex);
    }

    public void ApplyAnimation()
    {
        if (Animator == null || Archetype?.BaseAsset?.AnimationSequences == null)
        {
            return;
        }

        var sequences = Archetype.BaseAsset.AnimationSequences;
        if (sequences.Length == 0)
        {
            return;
        }

        SelectedAnimationIndex = Mathf.Clamp(SelectedAnimationIndex, 0, sequences.Length - 1);
        var sequence = sequences[SelectedAnimationIndex];
        if (sequence == null || string.IsNullOrWhiteSpace(sequence.Name))
        {
            return;
        }

        var stateName = ResolvePlayableStateName(sequence);
        if (string.IsNullOrWhiteSpace(stateName))
        {
            Debug.LogWarning($"[PlayerWardrobe] Animator state was not found for sequence '{sequence.Name}'.", this);
            return;
        }

        Animator.Play(stateName, 0, 0f);
        Animator.Update(0f);
    }

    private void OnEnable()
    {
        ApplyAppearance();
        ApplyAnimation();
    }

    private void OnValidate()
    {
        ApplyAppearance();
        ApplyAnimation();
    }

    private void OnDisable()
    {
        ReleaseRuntimeMeshes();
    }

    private void ApplySlot(string slotName, int selectedIndex)
    {
        var slot = GetSlot(slotName);
        var binding = GetBinding(slotName);
        if (slot == null || binding?.Root == null)
        {
            return;
        }

        var variants = slot.Variants ?? Array.Empty<L2PlayerCharacterVariantData>();
        if (variants.Length == 0)
        {
            ClearSlotRenderers(slotName);
            return;
        }

        selectedIndex = Mathf.Clamp(selectedIndex, 0, variants.Length - 1);
        var variant = variants[selectedIndex];
        var parts = variant?.Parts ?? Array.Empty<L2PlayerCharacterVariantPartData>();
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
            renderer.rootBone = RootBone;
            renderer.bones = Bones ?? Array.Empty<Transform>();
            renderer.updateWhenOffscreen = true;
            renderer.localBounds = renderer.sharedMesh != null ? renderer.sharedMesh.bounds : default;
            renderer.enabled = parts[i].Mesh != null;
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

    private L2PlayerCharacterSlotCatalogData GetSlot(string slotName)
    {
        return Archetype?.Slots?.FirstOrDefault(x => string.Equals(x?.SlotName, slotName, StringComparison.OrdinalIgnoreCase));
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

        var candidates = new[]
        {
            sequence.Name,
            $"Base Layer.{sequence.Name}",
            $"{Archetype?.BaseAsset?.CharacterName}_{SanitizeName(sequence.Name)}",
            $"Base Layer.{Archetype?.BaseAsset?.CharacterName}_{SanitizeName(sequence.Name)}"
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

        if (sourceMesh == null)
        {
            ReleaseRuntimeMesh(renderer);
            renderer.sharedMesh = null;
            return;
        }

        if (!_runtimeMeshes.TryGetValue(renderer, out var runtimeMesh) || runtimeMesh == null)
        {
            runtimeMesh = Instantiate(sourceMesh);
            runtimeMesh.name = $"{sourceMesh.name}_Runtime";
            _runtimeMeshes[renderer] = runtimeMesh;
        }
        else if (!string.Equals(runtimeMesh.name, $"{sourceMesh.name}_Runtime", StringComparison.Ordinal))
        {
            ReleaseRuntimeMesh(renderer);
            runtimeMesh = Instantiate(sourceMesh);
            runtimeMesh.name = $"{sourceMesh.name}_Runtime";
            _runtimeMeshes[renderer] = runtimeMesh;
        }

        renderer.sharedMesh = runtimeMesh;
    }

    private void ReleaseRuntimeMeshes()
    {
        foreach (var pair in _runtimeMeshes.ToArray())
        {
            ReleaseRuntimeMesh(pair.Key);
        }

        _runtimeMeshes.Clear();
    }

    private void ReleaseRuntimeMesh(SkinnedMeshRenderer renderer)
    {
        if (renderer == null || !_runtimeMeshes.TryGetValue(renderer, out var runtimeMesh) || runtimeMesh == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(runtimeMesh);
        }
        else
        {
            DestroyImmediate(runtimeMesh);
        }

        _runtimeMeshes.Remove(renderer);
    }
}
