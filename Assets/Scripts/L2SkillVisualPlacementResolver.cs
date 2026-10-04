using System;
using System.Linq;
using UnityEngine;

internal sealed class L2SkillVisualPlacementResolver
{
    private const float CreatureMidpointHeight = L2SkillVisualController.DefaultCreatureHeight * 0.5f;
    private readonly L2SkillVisualController _controller;
    private Transform CastPoint => _controller.CastPoint;
    private Transform TargetPoint => _controller.TargetPoint;

    public L2SkillVisualPlacementResolver(L2SkillVisualController controller)
    {
        _controller = controller ?? throw new ArgumentNullException(nameof(controller));
    }

    private Vector3 ResolveStagePosition(L2SkillVisualStagePlaybackRole role)
    {
        if (role == L2SkillVisualStagePlaybackRole.Projectile)
        {
            return ResolveProjectileOrigin();
        }

        if (IsTargetLikeRole(role))
        {
            return TargetPoint != null ? TargetPoint.position : _controller.transform.position + _controller.transform.forward * 4f;
        }

        return CastPoint != null ? CastPoint.position : _controller.transform.position;
    }

    public Vector3 ResolveStagePosition(L2SkillVisualStageBinding binding)
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
        return attachment.position + forward * (rawOffset.x * horizontalScale) + Vector3.up * (rawOffset.y * verticalScale) + right * (rawOffset.z * scale);
    }

    public Quaternion ResolveStageRotation(L2SkillVisualStageBinding binding)
    {
        if (binding.Role == L2SkillVisualStagePlaybackRole.Projectile)
        {
            return ResolveStageRotation(binding.Role);
        }

        var placement = binding.Placement ?? throw new InvalidOperationException($"Stage '{binding.StageName}' has no SkillAction placement.");
        if (placement.UseCharacterRotation)
        {
            var point = placement.SpawnOnTarget ? TargetPoint : CastPoint;
            return point != null && point.parent != null ? point.parent.rotation : _controller.transform.rotation;
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
        if (role == L2SkillVisualStagePlaybackRole.Projectile)
        {
            return ResolveTravelRotation(ResolveProjectileOrigin(), ResolveProjectileTarget());
        }

        return IsTargetLikeRole(role) && TargetPoint != null ? TargetPoint.rotation : _controller.transform.rotation;
    }

    public Vector3 ResolveProjectileOrigin()
    {
        return ResolveStagePosition(L2SkillVisualStagePlaybackRole.Caster) + Vector3.up * CreatureMidpointHeight;
    }

    public Vector3 ResolveProjectileTarget()
    {
        return ResolveStagePosition(L2SkillVisualStagePlaybackRole.Target) + Vector3.up * CreatureMidpointHeight;
    }

    public static Quaternion ResolveTravelRotation(Vector3 origin, Vector3 target)
    {
        var delta = target - origin;
        return delta.sqrMagnitude > 0.0001f
            ? Quaternion.LookRotation(delta.normalized, Vector3.up)
            : Quaternion.identity;
    }

    private static bool IsTargetLikeRole(L2SkillVisualStagePlaybackRole role)
    {
        return role == L2SkillVisualStagePlaybackRole.Target ||
               role == L2SkillVisualStagePlaybackRole.Impact;
    }
}
