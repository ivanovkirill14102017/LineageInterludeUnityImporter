using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

internal static class CreatureSkeletalPrefabFactory
{
    public static void Create(L2SkeletalCharacterAsset asset, Mesh mesh, Material[] materials, AnimatorController controller, string prefabPath, string displayLabel)
    {
        var session = L2SceneSkeletalAssetBridge.CreateSession(asset);
        var bindFrame = session.CaptureBindPoseDebugFrame();
        var bonePoses = CreatureSkeletalImportUtility.BuildBonePoses(bindFrame.Bones, asset.Bones);

        var root = new GameObject($"NPC_{asset.CharacterName}");
        try
        {
            var skeletonRoot = new GameObject("Skeleton").transform;
            skeletonRoot.SetParent(root.transform, false);

            var boneTransforms = CreatureSkeletalImportUtility.CreateBoneHierarchy(asset.Bones, bonePoses, skeletonRoot);

            var geometry = new GameObject("Geometry");
            geometry.transform.SetParent(root.transform, false);
            geometry.transform.localScale = Vector3.one;

            var renderer = geometry.AddComponent<SkinnedMeshRenderer>();
            renderer.sharedMesh = mesh;
            renderer.sharedMaterials = materials;
            renderer.rootBone = CreatureSkeletalImportUtility.ResolveRootBone(asset.Bones, boneTransforms);
            renderer.bones = boneTransforms;
            renderer.updateWhenOffscreen = true;
            renderer.localBounds = mesh.bounds;

            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            CreatureSkeletalImportUtility.CreateLabel(root.transform, mesh, displayLabel);

            EditorUtility.SetDirty(renderer);
            EditorUtility.SetDirty(animator);
            EditorUtility.SetDirty(root);

            L2AssetManager.EnsureParentFolderExists(prefabPath);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }
}
