#nullable enable
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class SkillVisualScenePreviewFactory
{
    private const float DefaultTargetDistance = 4f;
    private const float CapsuleCenterHeight = L2SkillVisualController.DefaultCreatureHeight * 0.5f;

    public static L2SkillVisualController Create(L2SkillVisualAsset skill)
    {
        if (skill == null)
        {
            throw new ArgumentNullException(nameof(skill));
        }

        var root = new GameObject(BuildRootName(skill));
        Undo.RegisterCreatedObjectUndo(root, "Create skill visual preview rig");

        var targetDistance = ResolveTargetDistance(skill);
        var caster = CreateCapsule("Caster", root.transform, new Vector3(0f, CapsuleCenterHeight, 0f));
        var target = CreateCapsule("Target", root.transform, new Vector3(0f, CapsuleCenterHeight, targetDistance));
        CreateBodyAnchors(caster.transform);
        CreateBodyAnchors(target.transform);

        var castPoint = CreateAnchor("CastPoint", caster.transform, new Vector3(0f, -CapsuleCenterHeight, 0f));
        var targetPoint = CreateAnchor("TargetPoint", target.transform, new Vector3(0f, -CapsuleCenterHeight, 0f));

        var stageContainer = new GameObject("Stages");
        stageContainer.transform.SetParent(root.transform, false);

        var runtimeContainer = new GameObject("Runtime");
        runtimeContainer.transform.SetParent(root.transform, false);

        var controller = caster.AddComponent<L2SkillVisualController>();
        controller.CastPoint = castPoint.transform;
        controller.TargetPoint = targetPoint.transform;
        controller.StageContainer = stageContainer.transform;
        controller.RuntimeContainer = runtimeContainer.transform;
        controller.ApplySkillTemplate(skill);
        controller.CastPoint = castPoint.transform;
        controller.TargetPoint = targetPoint.transform;
        controller.StageContainer = stageContainer.transform;
        controller.RuntimeContainer = runtimeContainer.transform;
        controller.CastPoint.localPosition = new Vector3(0f, -CapsuleCenterHeight, 0f);
        controller.TargetPoint.localPosition = new Vector3(0f, -CapsuleCenterHeight, 0f);

        caster.transform.rotation = Quaternion.LookRotation(Vector3.forward, Vector3.up);
        target.transform.rotation = Quaternion.LookRotation(Vector3.back, Vector3.up);

        Selection.activeObject = controller;
        EditorGUIUtility.PingObject(controller);
        EditorSceneManager.MarkSceneDirty(root.scene);
        return controller;
    }

    private static GameObject CreateCapsule(string name, Transform parent, Vector3 localPosition)
    {
        var capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.name = name;
        capsule.transform.SetParent(parent, false);
        capsule.transform.localPosition = localPosition;
        capsule.transform.localScale = Vector3.one;
        var collider = capsule.GetComponent<CapsuleCollider>();
        collider.height = L2SkillVisualController.DefaultCreatureHeight;
        collider.radius = 0.5f;
        return capsule;
    }

    private static void CreateBodyAnchors(Transform capsule)
    {
        CreateAnchor("RightHand", capsule, new Vector3(0.35f, 0.35f, 0f));
        CreateAnchor("LeftHand", capsule, new Vector3(-0.35f, 0.35f, 0f));
        CreateAnchor("RightFoot", capsule, new Vector3(0.16f, -0.85f, 0f));
        CreateAnchor("LeftFoot", capsule, new Vector3(-0.16f, -0.85f, 0f));
    }

    private static GameObject CreateAnchor(string name, Transform parent, Vector3 localPosition)
    {
        var anchor = new GameObject(name);
        anchor.transform.SetParent(parent, false);
        anchor.transform.localPosition = localPosition;
        return anchor;
    }

    private static float ResolveTargetDistance(L2SkillVisualAsset skill)
    {
        var castRange = skill.Levels?
            .Where(x => x != null && x.CastRange > 0)
            .OrderBy(x => x.SkillLevel)
            .Select(x => x.CastRange)
            .FirstOrDefault() ?? 0;

        if (castRange <= 0)
        {
            return DefaultTargetDistance;
        }

        return Mathf.Clamp(castRange * L2WorldScale.BakeUnrealToUnityScale, 1.5f, 12f);
    }

    private static string BuildRootName(L2SkillVisualAsset skill)
    {
        var label = string.IsNullOrWhiteSpace(skill.DisplayName)
            ? $"Skill_{skill.SkillId:D5}"
            : $"{skill.SkillId:D5}_{skill.DisplayName}";
        return $"SkillPreview_{SanitizeObjectName(label)}";
    }

    private static string SanitizeObjectName(string value)
    {
        var sanitized = value.Trim();
        foreach (var invalidChar in Path.GetInvalidFileNameChars())
        {
            sanitized = sanitized.Replace(invalidChar, '_');
        }

        return sanitized.Replace('/', '_').Replace('\\', '_').Replace(':', '_');
    }
}
