using UnityEditor;
using UnityEngine;
using System;

internal static class StaticMeshOverridePrefabUtility
{
    private const string OverrideRoot = "Assets/L2ImportOverrides";
    private const string VfxRoot = OverrideRoot + "/Vfx";
    private const string DefaultFlame01PrefabPath = VfxRoot + "/Default_Flame01.prefab";

    public static GameObject GetDefaultFlame01OverridePrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(DefaultFlame01PrefabPath);
        if (existing == null)
        {
            throw new InvalidOperationException(
                $"Default_Flame01 override prefab was not found at '{DefaultFlame01PrefabPath}'. " +
                "Create and maintain it as a normal project asset in Assets/L2ImportOverrides/Vfx.");
        }

        if (existing.GetComponentInChildren<DefaultFlame01Override>(true) == null)
        {
            throw new InvalidOperationException(
                $"Default_Flame01 override prefab at '{DefaultFlame01PrefabPath}' does not contain a {nameof(DefaultFlame01Override)} component.");
        }

        return existing;
    }
}
