using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2SkillVisualController : MonoBehaviour
{
    public L2SkillVisualAsset Skill;
    public Transform CastPoint;
    public Transform TargetPoint;
    public Transform StageContainer;
    public Transform RuntimeContainer;
    public L2SkillVisualStageBinding[] StageBindings = Array.Empty<L2SkillVisualStageBinding>();
    public int SelectedStageIndex;
    public bool PlayOnEnable;
    public bool LoopSelectedStage;
    public bool PlayImpactAfterProjectile = true;
    public float StageIntervalSeconds = 0.45f;
    public float ProjectileSpeed = 8f;
    public float ProjectileArcHeight;
    public float RuntimeInstanceLifetime = 3f;
    public bool IsPlaying;

    private readonly List<GameObject> _runtimeInstances = new List<GameObject>();
    private Coroutine _playbackRoutine;

    public string[] GetStageDisplayNames()
    {
        return (StageBindings ?? Array.Empty<L2SkillVisualStageBinding>())
            .Select((x, i) => x == null ? $"Stage {i}" : x.GetDisplayName(i))
            .ToArray();
    }

    public void SetSelectedStage(int index, bool preview)
    {
        var maxIndex = Math.Max(0, (StageBindings?.Length ?? 1) - 1);
        SelectedStageIndex = Mathf.Clamp(index, 0, maxIndex);
        if (preview)
        {
            PreviewSelectedStage();
        }
    }

    public void PreviewSelectedStage()
    {
        Stop();
        var binding = GetSelectedBinding();
        if (binding?.StageRoot == null)
        {
            return;
        }

        DeactivateTemplateStages();
        binding.StageRoot.transform.position = ResolveStagePosition(binding.Role);
        binding.StageRoot.transform.rotation = ResolveStageRotation(binding.Role);
        binding.StageRoot.SetActive(true);
        PlayParticleSystems(binding.StageRoot);
    }

    public void PlaySelectedStage()
    {
        if (!Application.isPlaying)
        {
            PreviewSelectedStage();
            return;
        }

        Stop();
        var binding = GetSelectedBinding();
        if (binding == null)
        {
            return;
        }

        IsPlaying = true;
        _playbackRoutine = StartCoroutine(PlaySelectedStageRoutine(binding));
    }

    public void PlayAllStages()
    {
        if (!Application.isPlaying)
        {
            PreviewSelectedStage();
            return;
        }

        Stop();
        IsPlaying = true;
        _playbackRoutine = StartCoroutine(PlayAllStagesRoutine());
    }

    public void Stop()
    {
        if (_playbackRoutine != null)
        {
            StopCoroutine(_playbackRoutine);
            _playbackRoutine = null;
        }

        IsPlaying = false;
        ClearRuntimeInstances();
        DeactivateTemplateStages();
    }

    public void RebuildStageBindingsFromChildren()
    {
        var sourceRoot = StageContainer != null ? StageContainer : transform;
        StageBindings = sourceRoot
            .Cast<Transform>()
            .Select((child, index) => new L2SkillVisualStageBinding
            {
                StageKey = ExtractStageKey(child.name),
                StageOrder = index,
                StageName = child.name,
                Role = InferStageRoleFromName(child.name),
                StageRoot = child.gameObject
            })
            .OrderBy(x => x.StageOrder)
            .ThenBy(x => x.StageName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        SelectedStageIndex = Mathf.Clamp(SelectedStageIndex, 0, Math.Max(0, StageBindings.Length - 1));
    }

    public void ApplySkillTemplate(L2SkillVisualAsset skill)
    {
        Stop();
        Skill = skill;
        EnsureAuthoringContainers();
        ClearTemplateStages();

        if (skill == null)
        {
            StageBindings = Array.Empty<L2SkillVisualStageBinding>();
            SelectedStageIndex = 0;
            return;
        }

        var templateController = skill.PreviewPrefab != null
            ? skill.PreviewPrefab.GetComponent<L2SkillVisualController>()
            : null;
        if (templateController != null && templateController != this)
        {
            CopyTemplateController(templateController);
            return;
        }

        StageBindings = (skill.Stages ?? Array.Empty<L2SkillVisualStageData>())
            .OrderBy(x => x.StageOrder)
            .ThenBy(x => x.ObjectName, StringComparer.OrdinalIgnoreCase)
            .Select(x => new L2SkillVisualStageBinding
            {
                StageKey = x.StageKey ?? string.Empty,
                StageOrder = x.StageOrder,
                StageName = x.ObjectName ?? string.Empty,
                Role = x.PlaybackRole,
                StageRoot = null
            })
            .ToArray();
        SelectedStageIndex = Mathf.Clamp(SelectedStageIndex, 0, Math.Max(0, StageBindings.Length - 1));
    }

    internal static void PlayParticleSystems(GameObject root)
    {
        if (root == null)
        {
            return;
        }

        foreach (var particleSystem in root.GetComponentsInChildren<ParticleSystem>(true))
        {
            particleSystem.Clear(true);
            particleSystem.Play(true);
        }
    }

    internal GameObject SpawnStageInstance(L2SkillVisualStageBinding binding, Vector3 position, Quaternion rotation)
    {
        if (binding?.StageRoot == null)
        {
            return null;
        }

        EnsureRuntimeContainer();
        var instance = Instantiate(binding.StageRoot, RuntimeContainer);
        instance.name = $"Runtime_{binding.StageRoot.name}";
        instance.transform.position = position;
        instance.transform.rotation = rotation;
        instance.SetActive(true);
        PlayParticleSystems(instance);
        _runtimeInstances.Add(instance);
        return instance;
    }

    internal void TrackRuntimeInstance(GameObject instance)
    {
        if (instance != null && !_runtimeInstances.Contains(instance))
        {
            _runtimeInstances.Add(instance);
        }
    }

    private IEnumerator PlaySelectedStageRoutine(L2SkillVisualStageBinding binding)
    {
        do
        {
            var duration = PlayStageBinding(binding);
            yield return new WaitForSeconds(duration);
            ClearRuntimeInstances();
        }
        while (LoopSelectedStage);

        IsPlaying = false;
        _playbackRoutine = null;
    }

    private IEnumerator PlayAllStagesRoutine()
    {
        var bindings = StageBindings ?? Array.Empty<L2SkillVisualStageBinding>();
        var skipIndexes = new HashSet<int>();
        for (var i = 0; i < bindings.Length; i++)
        {
            if (skipIndexes.Contains(i))
            {
                continue;
            }

            var binding = bindings[i];
            if (binding == null)
            {
                continue;
            }

            if (IsProjectileRole(binding.Role))
            {
                var impactIndex = FindLinkedImpactStageIndex(i);
                if (impactIndex >= 0)
                {
                    skipIndexes.Add(impactIndex);
                }
            }

            var duration = PlayStageBinding(binding);
            yield return new WaitForSeconds(duration);
        }

        IsPlaying = false;
        _playbackRoutine = null;
    }

    private float PlayStageBinding(L2SkillVisualStageBinding binding)
    {
        if (binding == null)
        {
            return StageIntervalSeconds;
        }

        if (IsProjectileRole(binding.Role))
        {
            return PlayProjectileStage(binding);
        }

        var instance = SpawnStageInstance(binding, ResolveStagePosition(binding.Role), ResolveStageRotation(binding.Role));
        if (instance != null && RuntimeInstanceLifetime > 0f)
        {
            Destroy(instance, RuntimeInstanceLifetime);
        }

        return Math.Max(0.01f, StageIntervalSeconds);
    }

    private float PlayProjectileStage(L2SkillVisualStageBinding binding)
    {
        var origin = ResolveStagePosition(L2SkillVisualStagePlaybackRole.Caster);
        var target = ResolveStagePosition(L2SkillVisualStagePlaybackRole.Target);
        var rotation = ResolveTravelRotation(origin, target);
        var instance = SpawnStageInstance(binding, origin, rotation);
        if (instance == null)
        {
            return StageIntervalSeconds;
        }

        var impactBinding = PlayImpactAfterProjectile ? FindLinkedImpactStage(binding) : null;
        var projectile = instance.GetComponent<L2SkillProjectileRuntime>();
        if (projectile == null)
        {
            projectile = instance.AddComponent<L2SkillProjectileRuntime>();
        }

        projectile.Initialize(
            this,
            origin,
            target,
            Math.Max(0.01f, ProjectileSpeed),
            Math.Max(0f, ProjectileArcHeight),
            impactBinding,
            RuntimeContainer,
            RuntimeInstanceLifetime);

        var distance = Vector3.Distance(origin, target);
        var travelTime = distance / Math.Max(0.01f, ProjectileSpeed);
        return Math.Max(StageIntervalSeconds, travelTime);
    }

    private L2SkillVisualStageBinding GetSelectedBinding()
    {
        var bindings = StageBindings ?? Array.Empty<L2SkillVisualStageBinding>();
        if (bindings.Length == 0)
        {
            return null;
        }

        SelectedStageIndex = Mathf.Clamp(SelectedStageIndex, 0, bindings.Length - 1);
        return bindings[SelectedStageIndex];
    }

    private L2SkillVisualStageBinding FindLinkedImpactStage(L2SkillVisualStageBinding projectileBinding)
    {
        var bindings = StageBindings ?? Array.Empty<L2SkillVisualStageBinding>();
        var projectileIndex = Array.IndexOf(bindings, projectileBinding);
        var impactIndex = FindLinkedImpactStageIndex(projectileIndex);
        return impactIndex >= 0 ? bindings[impactIndex] : null;
    }

    private int FindLinkedImpactStageIndex(int projectileIndex)
    {
        var bindings = StageBindings ?? Array.Empty<L2SkillVisualStageBinding>();
        if (projectileIndex < 0 || projectileIndex >= bindings.Length)
        {
            return -1;
        }

        for (var i = projectileIndex + 1; i < bindings.Length; i++)
        {
            if (IsTargetLikeRole(bindings[i]?.Role ?? L2SkillVisualStagePlaybackRole.Auto))
            {
                return i;
            }
        }

        for (var i = 0; i < bindings.Length; i++)
        {
            if (IsTargetLikeRole(bindings[i]?.Role ?? L2SkillVisualStagePlaybackRole.Auto))
            {
                return i;
            }
        }

        return -1;
    }

    private Vector3 ResolveStagePosition(L2SkillVisualStagePlaybackRole role)
    {
        if (IsTargetLikeRole(role))
        {
            return TargetPoint != null ? TargetPoint.position : transform.position + transform.forward * 4f;
        }

        return CastPoint != null ? CastPoint.position : transform.position;
    }

    private Quaternion ResolveStageRotation(L2SkillVisualStagePlaybackRole role)
    {
        if (IsProjectileRole(role))
        {
            return ResolveTravelRotation(
                CastPoint != null ? CastPoint.position : transform.position,
                TargetPoint != null ? TargetPoint.position : transform.position + transform.forward * 4f);
        }

        return transform.rotation;
    }

    private static Quaternion ResolveTravelRotation(Vector3 origin, Vector3 target)
    {
        var delta = target - origin;
        return delta.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(delta.normalized, Vector3.up)
            : Quaternion.identity;
    }

    private void EnsureRuntimeContainer()
    {
        if (RuntimeContainer != null)
        {
            return;
        }

        var runtime = transform.Find("Runtime");
        if (runtime == null)
        {
            var runtimeObject = new GameObject("Runtime");
            runtimeObject.transform.SetParent(transform, false);
            runtime = runtimeObject.transform;
        }

        RuntimeContainer = runtime;
    }

    private void EnsureAuthoringContainers()
    {
        CastPoint = EnsureChildTransform(CastPoint, "CastPoint");
        TargetPoint = EnsureChildTransform(TargetPoint, "TargetPoint");
        if (TargetPoint != null && TargetPoint.localPosition == Vector3.zero)
        {
            TargetPoint.localPosition = Vector3.forward * 4f;
        }

        StageContainer = EnsureChildTransform(StageContainer, "Stages");
        RuntimeContainer = EnsureChildTransform(RuntimeContainer, "Runtime");
    }

    private Transform EnsureChildTransform(Transform current, string childName)
    {
        if (current != null)
        {
            return current;
        }

        var existing = transform.Find(childName);
        if (existing != null)
        {
            return existing;
        }

        var child = new GameObject(childName);
        child.transform.SetParent(transform, false);
        return child.transform;
    }

    private void CopyTemplateController(L2SkillVisualController templateController)
    {
        if (templateController.CastPoint != null && CastPoint != null)
        {
            CastPoint.localPosition = templateController.CastPoint.localPosition;
            CastPoint.localRotation = templateController.CastPoint.localRotation;
            CastPoint.localScale = templateController.CastPoint.localScale;
        }

        if (templateController.TargetPoint != null && TargetPoint != null)
        {
            TargetPoint.localPosition = templateController.TargetPoint.localPosition;
            TargetPoint.localRotation = templateController.TargetPoint.localRotation;
            TargetPoint.localScale = templateController.TargetPoint.localScale;
        }

        StageIntervalSeconds = templateController.StageIntervalSeconds;
        ProjectileSpeed = templateController.ProjectileSpeed;
        ProjectileArcHeight = templateController.ProjectileArcHeight;
        RuntimeInstanceLifetime = templateController.RuntimeInstanceLifetime;
        PlayImpactAfterProjectile = templateController.PlayImpactAfterProjectile;

        var bindings = new List<L2SkillVisualStageBinding>();
        foreach (var binding in templateController.StageBindings ?? Array.Empty<L2SkillVisualStageBinding>())
        {
            GameObject stageRoot = null;
            if (binding?.StageRoot != null && StageContainer != null)
            {
                stageRoot = Instantiate(binding.StageRoot, StageContainer);
                stageRoot.name = binding.StageRoot.name;
                stageRoot.SetActive(false);
            }

            bindings.Add(new L2SkillVisualStageBinding
            {
                StageKey = binding?.StageKey ?? string.Empty,
                StageOrder = binding?.StageOrder ?? bindings.Count,
                StageName = binding?.StageName ?? (stageRoot != null ? stageRoot.name : string.Empty),
                Role = binding?.Role ?? L2SkillVisualStagePlaybackRole.Auto,
                StageRoot = stageRoot
            });
        }

        StageBindings = bindings.ToArray();
        SelectedStageIndex = Mathf.Clamp(SelectedStageIndex, 0, Math.Max(0, StageBindings.Length - 1));
    }

    private void ClearRuntimeInstances()
    {
        for (var i = _runtimeInstances.Count - 1; i >= 0; i--)
        {
            DestroyObject(_runtimeInstances[i]);
        }

        _runtimeInstances.Clear();

        if (RuntimeContainer == null)
        {
            return;
        }

        for (var i = RuntimeContainer.childCount - 1; i >= 0; i--)
        {
            DestroyObject(RuntimeContainer.GetChild(i).gameObject);
        }
    }

    private void DeactivateTemplateStages()
    {
        foreach (var binding in StageBindings ?? Array.Empty<L2SkillVisualStageBinding>())
        {
            if (binding?.StageRoot != null)
            {
                binding.StageRoot.SetActive(false);
            }
        }
    }

    private void ClearTemplateStages()
    {
        if (StageContainer == null)
        {
            return;
        }

        for (var i = StageContainer.childCount - 1; i >= 0; i--)
        {
            DestroyObject(StageContainer.GetChild(i).gameObject);
        }
    }

    private static void DestroyObject(GameObject target)
    {
        if (target == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            Destroy(target);
        }
        else
        {
            DestroyImmediate(target);
        }
    }

    private void OnEnable()
    {
        if (PlayOnEnable && Application.isPlaying)
        {
            PlayAllStages();
        }
    }

    private void OnDisable()
    {
        Stop();
    }

    private static bool IsProjectileRole(L2SkillVisualStagePlaybackRole role)
    {
        return role == L2SkillVisualStagePlaybackRole.Projectile;
    }

    private static bool IsTargetLikeRole(L2SkillVisualStagePlaybackRole role)
    {
        return role == L2SkillVisualStagePlaybackRole.Target ||
               role == L2SkillVisualStagePlaybackRole.Impact;
    }

    private static string ExtractStageKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var lastUnderscore = name.LastIndexOf('_');
        return lastUnderscore >= 0 && lastUnderscore < name.Length - 1
            ? name.Substring(lastUnderscore + 1)
            : string.Empty;
    }

    private static L2SkillVisualStagePlaybackRole InferStageRoleFromName(string name)
    {
        var stageKey = ExtractStageKey(name);
        if (stageKey.Equals("pr", StringComparison.OrdinalIgnoreCase) ||
            stageKey.Equals("fl", StringComparison.OrdinalIgnoreCase) ||
            name.IndexOf("projectile", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("arrow", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("bolt", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("shot", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return L2SkillVisualStagePlaybackRole.Projectile;
        }

        if (stageKey.Equals("ta", StringComparison.OrdinalIgnoreCase) ||
            stageKey.Equals("to", StringComparison.OrdinalIgnoreCase) ||
            name.IndexOf("target", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return L2SkillVisualStagePlaybackRole.Target;
        }

        if (name.IndexOf("hit", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("impact", StringComparison.OrdinalIgnoreCase) >= 0 ||
            name.IndexOf("explosion", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return L2SkillVisualStagePlaybackRole.Impact;
        }

        return L2SkillVisualStagePlaybackRole.Caster;
    }
}

public enum L2SkillVisualStagePlaybackRole
{
    Auto = 0,
    Caster = 1,
    Projectile = 2,
    Target = 3,
    Impact = 4,
    Persistent = 5
}

[Serializable]
public sealed class L2SkillVisualStageBinding
{
    public string StageKey;
    public int StageOrder;
    public string StageName;
    public L2SkillVisualStagePlaybackRole Role = L2SkillVisualStagePlaybackRole.Auto;
    public GameObject StageRoot;

    public string GetDisplayName(int fallbackIndex)
    {
        var name = string.IsNullOrWhiteSpace(StageName)
            ? StageRoot != null ? StageRoot.name : $"Stage {fallbackIndex}"
            : StageName;
        return string.IsNullOrWhiteSpace(StageKey)
            ? $"{StageOrder:D2} {Role} - {name}"
            : $"{StageOrder:D2} {StageKey} {Role} - {name}";
    }
}
