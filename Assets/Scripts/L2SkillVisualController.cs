using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

[ExecuteAlways]
[DisallowMultipleComponent]
public sealed class L2SkillVisualController : MonoBehaviour
{
    public const float DefaultCreatureHeight = 2f;
    private const float CreatureMidpointHeight = DefaultCreatureHeight * 0.5f;
    public L2SkillVisualAsset Skill;
    public Transform CastPoint;
    public Transform TargetPoint;
    public Transform StageContainer;
    public Transform RuntimeContainer;
    public L2SkillVisualStageBinding[] StageBindings = Array.Empty<L2SkillVisualStageBinding>();
    public int SelectedStageIndex;
    public bool PlayOnEnable;
    public bool LoopSelectedStage;
    public float StageIntervalSeconds = 0.45f;
    public float ProjectileSpeed = 8f;
    public float ProjectileArcHeight;
    public float RuntimeInstanceLifetime = 3f;
    public bool IsPlaying;

    private readonly List<GameObject> _runtimeInstances = new List<GameObject>();
    private Coroutine _playbackRoutine;
#if UNITY_EDITOR
    private bool _editorPreviewActive;
    private int _editorPreviewIndex;
    private double _editorNextStageTime;
    private bool _editorStopAfterCurrentStage;
    private int[] _editorPhaseActions;
    private int _editorPhaseActionIndex;
    private int _editorPhaseEndIndex;
    private double _editorPhaseStartTime;
    private float _editorPhaseDuration;
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
        binding.StageRoot.transform.position = ResolveStagePosition(binding);
        binding.StageRoot.transform.rotation = ResolveStageRotation(binding);
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
        StopEditorPreview();
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
        return instance;
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

    private IEnumerator PlayAllStagesRoutine(int startIndex)
    {
        var bindings = StageBindings ?? Array.Empty<L2SkillVisualStageBinding>();
        for (var i = Mathf.Clamp(startIndex, 0, bindings.Length); i < bindings.Length;)
        {
            if (bindings[i] == null)
            {
                i++;
                continue;
            }

            var phaseEnd = FindPhaseEnd(bindings, i);
            var actions = OrderPhaseActions(bindings, i, phaseEnd);
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

            i = phaseEnd;
        }

        IsPlaying = false;
        _playbackRoutine = null;
    }

    private static int FindPhaseEnd(L2SkillVisualStageBinding[] bindings, int start)
    {
        var placement = bindings[start].Placement ?? throw new InvalidOperationException($"Stage '{bindings[start].StageName}' has no placement.");
        if (string.IsNullOrWhiteSpace(placement.VisualReference) || !Enum.IsDefined(typeof(L2SkillVisualPhase), placement.Phase))
        {
            throw new InvalidOperationException($"Stage '{bindings[start].StageName}' has no visual reference or action phase. Reimport the skill visual.");
        }

        var end = start + 1;
        while (end < bindings.Length && bindings[end]?.Placement != null &&
               string.Equals(bindings[end].Placement.VisualReference, placement.VisualReference, StringComparison.OrdinalIgnoreCase) &&
               bindings[end].Placement.Phase == placement.Phase)
        {
            end++;
        }

        return end;
    }

    private static int[] OrderPhaseActions(L2SkillVisualStageBinding[] bindings, int start, int end)
    {
        return Enumerable.Range(start, end - start)
            .OrderBy(index => Mathf.Max(0f, bindings[index].Placement.SpawnDelay))
            .ThenBy(index => index)
            .ToArray();
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

        var instance = SpawnStageInstance(binding, ResolveStagePosition(binding), ResolveStageRotation(binding));
        if (instance != null && RuntimeInstanceLifetime > 0f && Application.isPlaying)
        {
            Destroy(instance, RuntimeInstanceLifetime);
        }

        return Math.Max(0.01f, StageIntervalSeconds);
    }

    private float PlayProjectileStage(L2SkillVisualStageBinding binding)
    {
        var origin = ResolveProjectileOrigin();
        var target = ResolveProjectileTarget();
        var distance = Vector3.Distance(origin, target);
        var flightTime = binding.Placement != null ? binding.Placement.FlyingTime : 0f;
        var speed = flightTime > 0f ? distance / flightTime : Math.Max(0.01f, ProjectileSpeed);
        var rotation = ResolveTravelRotation(origin, target);
        var instance = SpawnStageInstance(binding, origin, rotation);
        if (instance == null)
        {
            return StageIntervalSeconds;
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

        var travelTime = distance / Math.Max(0.01f, speed);
        return Math.Max(StageIntervalSeconds, travelTime);
    }

#if UNITY_EDITOR
    public void PreviewStagesInEditor(int startIndex, bool stopAfterCurrentStage)
    {
        if (Application.isPlaying)
        {
            PlayAllStagesFrom(startIndex);
            return;
        }

        Stop();
        DeactivateTemplateStages();
        ClearRuntimeInstances();
        var bindings = StageBindings ?? Array.Empty<L2SkillVisualStageBinding>();
        IsPlaying = true;
        _editorPreviewActive = true;
        _editorPreviewIndex = Mathf.Clamp(startIndex, 0, Math.Max(0, bindings.Length));
        _editorNextStageTime = EditorApplication.timeSinceStartup;
        _editorStopAfterCurrentStage = stopAfterCurrentStage;
        _editorPhaseActions = null;
        EditorApplication.update -= TickEditorPreview;
        EditorApplication.update += TickEditorPreview;
    }

    private void TickEditorPreview()
    {
        if (!_editorPreviewActive || Application.isPlaying)
        {
            StopEditorPreview();
            return;
        }

        var bindings = StageBindings ?? Array.Empty<L2SkillVisualStageBinding>();
        if (_editorPreviewIndex >= bindings.Length)
        {
            StopEditorPreview();
            IsPlaying = false;
            return;
        }

        var now = EditorApplication.timeSinceStartup;
        if (now < _editorNextStageTime)
        {
            return;
        }

        if (_editorPhaseActions == null)
        {
            _editorPhaseEndIndex = _editorStopAfterCurrentStage
                ? _editorPreviewIndex + 1
                : FindPhaseEnd(bindings, _editorPreviewIndex);
            _editorPhaseActions = OrderPhaseActions(bindings, _editorPreviewIndex, _editorPhaseEndIndex);
            _editorPhaseActionIndex = 0;
            _editorPhaseStartTime = now;
            _editorPhaseDuration = 0f;
        }

        while (_editorPhaseActionIndex < _editorPhaseActions.Length)
        {
            var binding = bindings[_editorPhaseActions[_editorPhaseActionIndex]];
            var delay = Mathf.Max(0f, binding.Placement.SpawnDelay);
            if (now < _editorPhaseStartTime + delay)
            {
                _editorNextStageTime = _editorPhaseStartTime + delay;
                return;
            }

            _editorPhaseDuration = Mathf.Max(_editorPhaseDuration, delay + PlayStageBinding(binding));
            _editorPhaseActionIndex++;
        }

        _editorNextStageTime = _editorPhaseStartTime + _editorPhaseDuration;
        if (now >= _editorNextStageTime)
        {
            _editorPreviewIndex = _editorStopAfterCurrentStage ? bindings.Length : _editorPhaseEndIndex;
            _editorPhaseActions = null;
            _editorNextStageTime = now;
        }

        SceneView.RepaintAll();
    }

    private void StopEditorPreview()
    {
        if (!_editorPreviewActive)
        {
            return;
        }

        EditorApplication.update -= TickEditorPreview;
        _editorPreviewActive = false;
        _editorStopAfterCurrentStage = false;
        _editorPhaseActions = null;
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

    private Vector3 ResolveStagePosition(L2SkillVisualStagePlaybackRole role)
    {
        if (IsProjectileRole(role))
        {
            return ResolveProjectileOrigin();
        }

        if (IsTargetLikeRole(role))
        {
            return TargetPoint != null ? TargetPoint.position : transform.position + transform.forward * 4f;
        }

        return CastPoint != null ? CastPoint.position : transform.position;
    }

    private Vector3 ResolveStagePosition(L2SkillVisualStageBinding binding)
    {
        if (binding.Role == L2SkillVisualStagePlaybackRole.Projectile)
        {
            return ResolveProjectileOrigin();
        }

        var placement = binding.Placement ?? throw new InvalidOperationException($"Stage '{binding.StageName}' has no SkillAction placement.");
        if (placement.Absolute)
        {
            throw new NotSupportedException($"Absolute attachment for stage '{binding.StageName}' is not implemented.");
        }

        var point = placement.SpawnOnTarget ? TargetPoint : CastPoint;
        if (point == null)
        {
            throw new InvalidOperationException($"Stage '{binding.StageName}' requires a {(placement.SpawnOnTarget ? "TargetPoint" : "CastPoint")}.");
        }

        var attachment = ResolveAttachment(point, placement, binding.StageName);
        var rawOffset = placement.Offset;
        var scale = L2WorldScale.UnrealToUnityScale;
        var horizontalScale = scale;
        var verticalScale = scale;
        if (placement.RelativeToCylinder)
        {
            var collider = point.parent != null ? point.parent.GetComponent<CapsuleCollider>() : null;
            if (collider == null)
            {
                throw new InvalidOperationException($"Stage '{binding.StageName}' requires a capsule collider for bRelativeToCylinder.");
            }

            horizontalScale = collider.radius * Mathf.Max(Mathf.Abs(collider.transform.lossyScale.x), Mathf.Abs(collider.transform.lossyScale.z));
            verticalScale = collider.height * Mathf.Abs(collider.transform.lossyScale.y) * 0.5f;
        }

        var direction = ResolveTravelRotation(ResolveStagePosition(L2SkillVisualStagePlaybackRole.Caster), ResolveStagePosition(L2SkillVisualStagePlaybackRole.Target));
        var forward = direction * Vector3.forward;
        var right = direction * Vector3.right;
        return attachment.position + forward * (rawOffset.x * horizontalScale) + right * (rawOffset.y * horizontalScale) + Vector3.up * (rawOffset.z * verticalScale);
    }

    private Quaternion ResolveStageRotation(L2SkillVisualStageBinding binding)
    {
        if (binding.Role == L2SkillVisualStagePlaybackRole.Projectile)
        {
            return ResolveStageRotation(binding.Role);
        }

        var placement = binding.Placement ?? throw new InvalidOperationException($"Stage '{binding.StageName}' has no SkillAction placement.");
        if (placement.UseCharacterRotation)
        {
            var point = placement.SpawnOnTarget ? TargetPoint : CastPoint;
            return point != null && point.parent != null ? point.parent.rotation : transform.rotation;
        }

        return ResolveTravelRotation(ResolveStagePosition(L2SkillVisualStagePlaybackRole.Caster), ResolveStagePosition(L2SkillVisualStagePlaybackRole.Target));
    }

    private static Transform ResolveAttachment(Transform point, L2SkillVisualPlacementData placement, string stageName)
    {
        if (placement.AttachOn == L2SkillEffectAttachMethod.None || placement.AttachOn == L2SkillEffectAttachMethod.Trail)
        {
            return point;
        }

        var actor = point.parent != null ? point.parent : point;
        var animator = actor.GetComponentInChildren<Animator>();
        HumanBodyBones? humanoidBone = placement.AttachOn switch
        {
            L2SkillEffectAttachMethod.RightHand => HumanBodyBones.RightHand,
            L2SkillEffectAttachMethod.LeftHand => HumanBodyBones.LeftHand,
            L2SkillEffectAttachMethod.RightFoot => HumanBodyBones.RightFoot,
            L2SkillEffectAttachMethod.LeftFoot => HumanBodyBones.LeftFoot,
            _ => null
        };
        if (humanoidBone.HasValue && animator != null && animator.isHuman)
        {
            var bone = animator.GetBoneTransform(humanoidBone.Value);
            if (bone != null)
            {
                return bone;
            }
        }

        var name = placement.AttachOn switch
        {
            L2SkillEffectAttachMethod.RightHand => "RightHand",
            L2SkillEffectAttachMethod.LeftHand => "LeftHand",
            L2SkillEffectAttachMethod.RightFoot => "RightFoot",
            L2SkillEffectAttachMethod.LeftFoot => "LeftFoot",
            L2SkillEffectAttachMethod.BoneSpecified or L2SkillEffectAttachMethod.AliasSpecified => placement.AttachBoneName,
            _ => throw new NotSupportedException($"Stage '{stageName}' uses unknown AttachOn={placement.AttachOn}.")
        };
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException($"Stage '{stageName}' has no attachment bone name.");
        }

        var normalized = NormalizeBoneName(name);
        foreach (var candidate in actor.GetComponentsInChildren<Transform>(true))
        {
            var candidateName = NormalizeBoneName(candidate.name);
            if (candidateName == normalized || MatchesStandardAttachmentName(placement.AttachOn, candidateName))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException($"Stage '{stageName}' requires attachment bone '{name}' on '{actor.name}'.");
    }

    private static string NormalizeBoneName(string name)
    {
        return new string(name.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
    }

    private static bool MatchesStandardAttachmentName(L2SkillEffectAttachMethod method, string candidate)
    {
        return method switch
        {
            L2SkillEffectAttachMethod.RightHand => candidate == "rhand" || candidate == "handr" || candidate == "bip01rhand" || candidate == "bip01righthand",
            L2SkillEffectAttachMethod.LeftHand => candidate == "lhand" || candidate == "handl" || candidate == "bip01lhand" || candidate == "bip01lefthand",
            L2SkillEffectAttachMethod.RightFoot => candidate == "rfoot" || candidate == "bip01rfoot" || candidate == "bip01rightfoot",
            L2SkillEffectAttachMethod.LeftFoot => candidate == "lfoot" || candidate == "bip01lfoot" || candidate == "bip01leftfoot",
            _ => false
        };
    }

    private Quaternion ResolveStageRotation(L2SkillVisualStagePlaybackRole role)
    {
        if (IsProjectileRole(role))
        {
            return ResolveTravelRotation(ResolveProjectileOrigin(), ResolveProjectileTarget());
        }

        return IsTargetLikeRole(role) && TargetPoint != null ? TargetPoint.rotation : transform.rotation;
    }

    private Vector3 ResolveProjectileOrigin()
    {
        return ResolveStagePosition(L2SkillVisualStagePlaybackRole.Caster) + Vector3.up * CreatureMidpointHeight;
    }

    private Vector3 ResolveProjectileTarget()
    {
        return ResolveStagePosition(L2SkillVisualStagePlaybackRole.Target) + Vector3.up * CreatureMidpointHeight;
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
    public int StageOrder;
    public string StageName;
    public L2SkillVisualStagePlaybackRole Role = L2SkillVisualStagePlaybackRole.Auto;
    public L2SkillVisualPlacementData Placement;
    public GameObject StageRoot;

    public string GetDisplayName(int fallbackIndex)
    {
        var name = string.IsNullOrWhiteSpace(StageName)
            ? StageRoot != null ? StageRoot.name : $"Stage {fallbackIndex}"
            : StageName;
        return $"{StageOrder:D2} {Role} - {name}";
    }

    public static L2SkillVisualStagePlaybackRole ResolveRole(bool isProjectile, L2SkillVisualPlacementData placement, string stageName)
    {
        if (placement == null)
        {
            throw new NotSupportedException($"Skill stage '{stageName}' has no explicit SkillAction placement.");
        }

        return isProjectile
            ? L2SkillVisualStagePlaybackRole.Projectile
            : placement.SpawnOnTarget
                ? L2SkillVisualStagePlaybackRole.Target
                : L2SkillVisualStagePlaybackRole.Caster;
    }
}
