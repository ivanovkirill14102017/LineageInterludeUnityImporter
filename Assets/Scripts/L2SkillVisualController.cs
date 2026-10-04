using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2SkillVisualController : MonoBehaviour
{
    public const float DefaultCreatureHeight = 2f;
    public L2SkillVisualAsset Skill;
    public Transform CastPoint;
    public Transform TargetPoint;
    public Transform StageContainer;
    public Transform RuntimeContainer;
    public L2SkillVisualStageBinding[] StageBindings = Array.Empty<L2SkillVisualStageBinding>();
    public int SelectedStageIndex;
    public bool PlayOnEnable;
    public bool LoopSelectedStage;
    public float SelectedStageReplayIntervalSeconds = 0.45f;
    public float ProjectileSpeed = 8f;
    public float ProjectileArcHeight;
    public float RuntimeInstanceLifetime = 3f;
    public bool IsPlaying;

    private readonly List<GameObject> _runtimeInstances = new List<GameObject>();
    private readonly List<GameObject> _castingInstances = new List<GameObject>();
    private readonly List<GameObject> _shotInstances = new List<GameObject>();
    private Coroutine _playbackRoutine;
    private L2SkillVisualPlacementResolver _placementResolver;
    private L2SkillVisualPlacementResolver PlacementResolver => _placementResolver ??= new L2SkillVisualPlacementResolver(this);
#if UNITY_EDITOR
    private L2SkillVisualEditorPreview _editorPreview;
#endif

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

        if (IsProjectileRole(binding.Role))
        {
            PlayStageAt(SelectedStageIndex);
            return;
        }

        DeactivateTemplateStages();
        binding.StageRoot.transform.position = PlacementResolver.ResolveStagePosition(binding);
        binding.StageRoot.transform.rotation = PlacementResolver.ResolveStageRotation(binding);
        binding.StageRoot.SetActive(true);
        PlayParticleSystems(binding.StageRoot);
    }

    public void PreviewStageAt(int index)
    {
        SetSelectedStage(index, preview: false);
        PreviewSelectedStage();
    }

    public void PlayStageAt(int index)
    {
        SetSelectedStage(index, preview: false);
        if (!Application.isPlaying)
        {
#if UNITY_EDITOR
            PreviewStagesInEditor(SelectedStageIndex, stopAfterCurrentStage: true);
#else
            PreviewSelectedStage();
#endif
            return;
        }

        PlaySelectedStage();
    }

    public void PlayAllStagesFrom(int index)
    {
        SetSelectedStage(index, preview: false);
        L2SkillVisualTimeline.ValidateStandaloneTimeline(Skill, StageBindings, SelectedStageIndex);
        if (!Application.isPlaying)
        {
#if UNITY_EDITOR
            PreviewStagesInEditor(SelectedStageIndex, stopAfterCurrentStage: false);
#else
            PreviewSelectedStage();
#endif
            return;
        }

        Stop();
        IsPlaying = true;
        _playbackRoutine = StartCoroutine(PlayAllStagesRoutine(SelectedStageIndex));
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
        L2SkillVisualTimeline.ValidateStandaloneTimeline(Skill, StageBindings, 0);
        if (!Application.isPlaying)
        {
#if UNITY_EDITOR
            PreviewStagesInEditor(0, stopAfterCurrentStage: false);
#else
            PreviewSelectedStage();
#endif
            return;
        }

        Stop();
        IsPlaying = true;
        _playbackRoutine = StartCoroutine(PlayAllStagesRoutine(0));
    }

    public void Stop()
    {
        if (_playbackRoutine != null)
        {
            StopCoroutine(_playbackRoutine);
            _playbackRoutine = null;
        }

#if UNITY_EDITOR
        _editorPreview?.Stop();
#endif
        IsPlaying = false;
        ClearRuntimeInstances();
        DeactivateTemplateStages();
    }

    public void RebuildStageBindingsFromChildren()
    {
        var sourceRoot = StageContainer != null ? StageContainer : transform;
        StageBindings = sourceRoot
            .Cast<Transform>()
            .Select(child =>
            {
                var source = child.GetComponent<L2SkillVisualStageSource>();
                if (source == null)
                {
                    throw new InvalidOperationException($"Stage '{child.name}' has no L2SkillVisualStageSource metadata. Reimport the skill visual before rebuilding bindings.");
                }

                return new L2SkillVisualStageBinding
                {
                    StageOrder = source.StageOrder,
                    StageName = source.ObjectName,
                    Role = L2SkillVisualStageBinding.ResolveRole(source.IsProjectile, source.Placement, source.ObjectName),
                    Placement = source.Placement,
                    StageRoot = child.gameObject
                };
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
                StageOrder = x.StageOrder,
                StageName = x.ObjectName ?? string.Empty,
                Role = L2SkillVisualStageBinding.ResolveRole(x.IsProjectile, x.Placement, x.ObjectName),
                Placement = x.Placement,
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
        if (binding.Placement?.Phase == L2SkillVisualPhase.Casting)
        {
            _castingInstances.Add(instance);
        }
        else if (binding.Placement?.Phase == L2SkillVisualPhase.Shot && !IsProjectileRole(binding.Role))
        {
            _shotInstances.Add(instance);
        }
        return instance;
    }

    private IEnumerator PlaySelectedStageRoutine(L2SkillVisualStageBinding binding)
    {
        do
        {
            var duration = PlayStageBinding(binding);
            yield return new WaitForSeconds(Mathf.Max(0.01f, duration, SelectedStageReplayIntervalSeconds));
            ClearRuntimeInstances();
        }
        while (LoopSelectedStage);

        IsPlaying = false;
        _playbackRoutine = null;
    }

    private IEnumerator PlayAllStagesRoutine(int startIndex)
    {
        var bindings = StageBindings ?? Array.Empty<L2SkillVisualStageBinding>();
        string visualReference = null;
        var visualElapsed = 0f;
        var sawCasting = false;
        for (var i = Mathf.Clamp(startIndex, 0, bindings.Length); i < bindings.Length;)
        {
            if (bindings[i] == null)
            {
                i++;
                continue;
            }

            var placement = bindings[i].Placement ?? throw new InvalidOperationException($"Stage '{bindings[i].StageName}' has no placement.");
            if (!string.Equals(visualReference, placement.VisualReference, StringComparison.OrdinalIgnoreCase))
            {
                visualReference = placement.VisualReference;
                visualElapsed = 0f;
                sawCasting = false;
            }

            L2SkillVisualTimeline.ValidateStandalonePhase(placement.Phase);
            if (placement.Phase == L2SkillVisualPhase.Casting)
            {
                sawCasting = true;
            }
            else if (placement.Phase == L2SkillVisualPhase.Shot && sawCasting)
            {
                var hitTime = L2SkillVisualTimeline.ResolveHitTime(Skill, visualReference);
                if (hitTime > visualElapsed)
                {
                    yield return new WaitForSeconds(hitTime - visualElapsed);
                    visualElapsed = hitTime;
                }
            }

            if (placement.Phase == L2SkillVisualPhase.Shot)
            {
                StopCastingEffects();
            }
            else if (placement.Phase == L2SkillVisualPhase.Explosion)
            {
                StopShotEffects();
            }

            var phaseEnd = L2SkillVisualTimeline.FindPhaseEnd(bindings, i);
            var actions = L2SkillVisualTimeline.OrderPhaseActions(bindings, i, phaseEnd);
            var elapsed = 0f;
            var phaseDuration = 0f;
            foreach (var actionIndex in actions)
            {
                var binding = bindings[actionIndex];
                var delay = Mathf.Max(0f, binding.Placement.SpawnDelay);
                if (delay > elapsed)
                {
                    yield return new WaitForSeconds(delay - elapsed);
                    elapsed = delay;
                }

                phaseDuration = Mathf.Max(phaseDuration, delay + PlayStageBinding(binding));
            }

            if (phaseDuration > elapsed)
            {
                yield return new WaitForSeconds(phaseDuration - elapsed);
            }

            visualElapsed += phaseDuration;
            i = phaseEnd;
        }

        IsPlaying = false;
        _playbackRoutine = null;
    }

    internal float PlayStageBinding(L2SkillVisualStageBinding binding)
    {
        if (binding == null)
        {
            return SelectedStageReplayIntervalSeconds;
        }

        if (IsProjectileRole(binding.Role))
        {
            return PlayProjectileStage(binding);
        }

        var instance = SpawnStageInstance(binding, PlacementResolver.ResolveStagePosition(binding), PlacementResolver.ResolveStageRotation(binding));
        if (instance != null && RuntimeInstanceLifetime > 0f && Application.isPlaying)
        {
            var lifetime = binding.Placement.Phase == L2SkillVisualPhase.Casting
                ? L2SkillVisualTimeline.ResolveHitTime(Skill, binding.Placement.VisualReference) + RuntimeInstanceLifetime
                : RuntimeInstanceLifetime;
            Destroy(instance, lifetime);
        }

        return 0f;
    }

    private float PlayProjectileStage(L2SkillVisualStageBinding binding)
    {
        var origin = PlacementResolver.ResolveProjectileOrigin();
        var target = PlacementResolver.ResolveProjectileTarget();
        var distance = Vector3.Distance(origin, target);
        var flightTime = binding.Placement != null ? binding.Placement.FlyingTime : 0f;
        var speed = flightTime > 0f ? distance / flightTime : Math.Max(0.01f, ProjectileSpeed);
        var rotation = L2SkillVisualPlacementResolver.ResolveTravelRotation(origin, target);
        var instance = SpawnStageInstance(binding, origin, rotation);
        var travelTime = distance / Math.Max(0.01f, speed);
        if (instance == null)
        {
            return travelTime;
        }

        var projectile = instance.GetComponent<L2SkillProjectileRuntime>();
        if (projectile == null)
        {
            projectile = instance.AddComponent<L2SkillProjectileRuntime>();
        }

        projectile.Initialize(
            origin,
            target,
            Math.Max(0.01f, speed),
            Math.Max(0f, ProjectileArcHeight));

        return travelTime;
    }

#if UNITY_EDITOR
    public void PreviewStagesInEditor(int startIndex, bool stopAfterCurrentStage)
    {
        if (Application.isPlaying)
        {
            PlayAllStagesFrom(startIndex);
            return;
        }

        (_editorPreview ??= new L2SkillVisualEditorPreview(this)).Play(startIndex, stopAfterCurrentStage);
    }
#endif

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

        SelectedStageReplayIntervalSeconds = templateController.SelectedStageReplayIntervalSeconds;
        ProjectileSpeed = templateController.ProjectileSpeed;
        ProjectileArcHeight = templateController.ProjectileArcHeight;
        RuntimeInstanceLifetime = templateController.RuntimeInstanceLifetime;

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
                StageOrder = binding?.StageOrder ?? bindings.Count,
                StageName = binding?.StageName ?? (stageRoot != null ? stageRoot.name : string.Empty),
                Role = binding?.Role ?? L2SkillVisualStagePlaybackRole.Auto,
                Placement = binding?.Placement,
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
        _castingInstances.Clear();
        _shotInstances.Clear();

        if (RuntimeContainer == null)
        {
            return;
        }

        for (var i = RuntimeContainer.childCount - 1; i >= 0; i--)
        {
            DestroyObject(RuntimeContainer.GetChild(i).gameObject);
        }
    }

    internal void StopCastingEffects()
    {
        StopEmitting(_castingInstances);
        _castingInstances.Clear();
    }

    internal void StopShotEffects()
    {
        StopEmitting(_shotInstances);
        _shotInstances.Clear();
    }

    private static void StopEmitting(IEnumerable<GameObject> instances)
    {
        foreach (var instance in instances)
        {
            if (instance == null)
            {
                continue;
            }

            foreach (var particleSystem in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
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

}
