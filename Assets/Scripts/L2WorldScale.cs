using UnityEngine;

public static class L2WorldScale
{
    private const float UnrealRotatorUnitsToRadians = Mathf.PI / 32768f;
    public const float TerrainQuadrantSizeUnreal = 32768f;

    public const float UnrealToUnityScale = 0.016f * 3f;
    public const float BakeUnrealToUnityScale = UnrealToUnityScale;
    public const float UnityToUnrealScale = 1f / UnrealToUnityScale;
    public const float TerrainQuadrantSizeUnity = TerrainQuadrantSizeUnreal * UnrealToUnityScale;
    public static Vector3 TransformFromUnrealToUnityWithScale(this System.Numerics.Vector3 raw)
    {
        return new Vector3(raw.X * UnrealToUnityScale, raw.Z * UnrealToUnityScale, raw.Y * UnrealToUnityScale);
    }

    public static Quaternion ToEulerAngles(this System.Numerics.Vector3 rotDegrees)
    {
        return Quaternion.Euler(rotDegrees.X, -rotDegrees.Y, -rotDegrees.Z);
    }

    public static Quaternion ToUnityRotationFromUnrealRotator(this System.Numerics.Vector3 unrealRotator)
    {
        var unityRight = RotateUnrealVector(System.Numerics.Vector3.UnitX, unrealRotator).ToUnityVectorWithoutScale();
        var unityUp = RotateUnrealVector(System.Numerics.Vector3.UnitZ, unrealRotator).ToUnityVectorWithoutScale();
        var unityForward = RotateUnrealVector(System.Numerics.Vector3.UnitY, unrealRotator).ToUnityVectorWithoutScale();

        return Quaternion.LookRotation(unityForward.normalized, unityUp.normalized);
    }

    public static Vector3 ToDirectUnityVectorWithoutModification(this System.Numerics.Vector3 raw)
    {
        return new Vector3(raw.X, raw.Y, raw.Z);
    }

    public static Vector3 ToUnityVectorWithoutScale(this System.Numerics.Vector3 raw)
    {
        return new Vector3(raw.X, raw.Z, raw.Y);
    }

    private static System.Numerics.Vector3 RotateUnrealVector(System.Numerics.Vector3 vector, System.Numerics.Vector3 rotator)
    {
        var pitch = -rotator.X * UnrealRotatorUnitsToRadians;
        var yaw = rotator.Y * UnrealRotatorUnitsToRadians;
        var roll = rotator.Z * UnrealRotatorUnitsToRadians;

        var cp = Mathf.Cos(pitch);
        var sp = Mathf.Sin(pitch);
        var cy = Mathf.Cos(yaw);
        var sy = Mathf.Sin(yaw);
        var cr = Mathf.Cos(roll);
        var sr = Mathf.Sin(roll);

        var forward = new System.Numerics.Vector3(cp * cy, cp * sy, -sp);
        var right = new System.Numerics.Vector3(-sr * sp * cy + cr * sy, -sr * sp * sy - cr * cy, -sr * cp);
        var up = new System.Numerics.Vector3(cr * sp * cy + sr * sy, cr * sp * sy - sr * cy, cr * cp);

        return new System.Numerics.Vector3(
            forward.X * vector.X - right.X * vector.Y + up.X * vector.Z,
            forward.Y * vector.X - right.Y * vector.Y + up.Y * vector.Z,
            forward.Z * vector.X - right.Z * vector.Y + up.Z * vector.Z);
    }
}
