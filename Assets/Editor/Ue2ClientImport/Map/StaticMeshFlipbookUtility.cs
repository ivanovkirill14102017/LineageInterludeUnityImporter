using UnityEngine;

internal static class StaticMeshFlipbookUtility
{
    private const bool EnableFlipbooksByDefault = false;

    public static void ApplyFlipbooks(GameObject gameObject, MeshRenderer renderer, Texture2D[][] flipbooks)
    {
        if (!EnableFlipbooksByDefault)
        {
            return;
        }

        if (flipbooks == null || flipbooks.Length == 0)
        {
            return;
        }

        var clonedMaterials = renderer.sharedMaterials;
        var hasAnimatedMaterials = false;

        for (var i = 0; i < clonedMaterials.Length && i < flipbooks.Length; i++)
        {
            if (flipbooks[i] == null || flipbooks[i].Length <= 1)
            {
                continue;
            }

            hasAnimatedMaterials = true;
            clonedMaterials[i] = new Material(clonedMaterials[i]);

            var animator = gameObject.AddComponent<FlipbookMaterialAnimator>();
            animator.frames = flipbooks[i];
            animator.targetMaterial = clonedMaterials[i];
        }

        if (hasAnimatedMaterials)
        {
            renderer.sharedMaterials = clonedMaterials;
        }
    }
}
