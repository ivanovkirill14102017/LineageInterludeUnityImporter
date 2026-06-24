using UnityEngine;

public static class L2WorldScale
{
    public const float UnrealToUnityScale = 0.016f * 3f;
    public const float BakeUnrealToUnityScale = UnrealToUnityScale;
    public const float UnityToUnrealScale = 1f / UnrealToUnityScale;
    public static Vector3 TransformFromUnrealToUnityWithScale(this System.Numerics.Vector3 raw)
    {
        return new Vector3(raw.X * UnrealToUnityScale, raw.Z * UnrealToUnityScale, raw.Y * UnrealToUnityScale);
    }

    public static Quaternion ToEulerAngles(this System.Numerics.Vector3 rotDegrees)
    {
        return Quaternion.Euler(rotDegrees.X, -rotDegrees.Y, -rotDegrees.Z);
    }

    public static Vector3 ToDirectUnityVectorWithoutModification(this System.Numerics.Vector3 raw)
    {
        return new Vector3(raw.X, raw.Y, raw.Z);
    }
}
