using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

internal static class UnityShaderReport
{
    private const string ReportDirectory = "Assets/L2Imported/Diagnostics";
    private const string ReportPath = ReportDirectory + "/AvailableShaders.txt";

    [MenuItem("Tools/L2/Diagnostics/Dump Available Shaders")]
    private static void DumpAvailableShaders()
    {
        L2AssetManager.EnsureFolderExists("Assets/L2Imported");
        L2AssetManager.EnsureFolderExists(ReportDirectory);

        var shaderNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var shader in Resources.FindObjectsOfTypeAll<Shader>())
        {
            if (shader == null || string.IsNullOrWhiteSpace(shader.name))
            {
                continue;
            }

            shaderNames.Add(shader.name);
        }

        foreach (var guid in AssetDatabase.FindAssets("t:Shader"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
            if (shader == null || string.IsNullOrWhiteSpace(shader.name))
            {
                continue;
            }

            shaderNames.Add(shader.name);
        }

        var lines = new List<string>
        {
            $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
            $"Shader count: {shaderNames.Count}",
            string.Empty
        };
        lines.AddRange(shaderNames.OrderBy(name => name, StringComparer.OrdinalIgnoreCase));

        File.WriteAllLines(ReportPath, lines);
        AssetDatabase.ImportAsset(ReportPath, ImportAssetOptions.ForceUpdate);
        Debug.Log($"[Diagnostics] Shader report written to {ReportPath} ({shaderNames.Count} shaders).");
    }
}
