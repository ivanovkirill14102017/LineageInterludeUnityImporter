using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

internal static class SceneMeshRendererDiagnostics
{
    [MenuItem("L2/Diagnostics/Report Invalid Mesh Renderers")]
    private static void ReportInvalidMeshRenderers()
    {
        var renderers = Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        var issues = new List<string>();

        foreach (var renderer in renderers)
        {
            if (renderer == null)
            {
                continue;
            }

            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null)
            {
                issues.Add($"{BuildPath(renderer.transform)} | MeshRenderer without MeshFilter");
                continue;
            }

            if (filter.sharedMesh == null)
            {
                issues.Add($"{BuildPath(renderer.transform)} | sharedMesh=<null> | materials={renderer.sharedMaterials.Length}");
                continue;
            }

            if (filter.sharedMesh.subMeshCount != renderer.sharedMaterials.Length)
            {
                issues.Add($"{BuildPath(renderer.transform)} | subMeshes={filter.sharedMesh.subMeshCount} materials={renderer.sharedMaterials.Length}");
            }
        }

        if (issues.Count == 0)
        {
            Debug.Log("L2 Diagnostics: no invalid MeshRenderer/MeshFilter pairs found.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"L2 Diagnostics: found {issues.Count} invalid MeshRenderer entries.");
        foreach (var issue in issues)
        {
            sb.AppendLine(issue);
        }

        Debug.LogError(sb.ToString());
    }

    private static string BuildPath(Transform transform)
    {
        if (transform == null)
        {
            return "<null>";
        }

        var path = transform.name;
        var current = transform.parent;
        while (current != null)
        {
            path = $"{current.name}/{path}";
            current = current.parent;
        }

        return path;
    }
}
