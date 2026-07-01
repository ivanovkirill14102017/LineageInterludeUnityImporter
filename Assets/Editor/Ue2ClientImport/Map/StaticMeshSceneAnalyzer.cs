using System;
using System.Diagnostics;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using L2Viewer.SceneDomain.Services.MaterialServices;
using L2Viewer.UnrFile;

internal static class StaticMeshSceneAnalyzer
{
    public static SceneInstancedMeshResult BuildInstancedMeshes(Ue2MapSource source, Action<string> log)
    {
        var textureManager = new BspTextureManager(source.ClientPath);
        var meshResolver = new SceneStaticMeshResolver(source.ClientPath, textureManager);
        var meshBuilder = new SceneInstancedMeshBuilder(meshResolver, textureManager);

        var referenceScanStopwatch = Stopwatch.StartNew();
        var references = source.UnrFile.ExportObjects
            .Select(e => e.Object as UnrActorBaseObject)
            .Where(a => a != null && a.StaticMeshReference != null && a.StaticMeshReference.RawReference != 0)
            .Select(a => a.StaticMeshReference)
            .GroupBy(r => r.ObjectName)
            .Select(g => g.First())
            .ToList();
        referenceScanStopwatch.Stop();
        log($"[StaticMesh/Analysis] Reference scan ({referenceScanStopwatch.Elapsed.TotalSeconds:F2}s)");

        log($"Found {references.Count} unique static mesh references to resolve. Resolving in batch...");

        var resolveStopwatch = Stopwatch.StartNew();
        try
        {
            meshResolver.ResolveMany(source.UnrFile.FilePath, references);
        }
        catch (Exception ex)
        {
            log($"Encountered error during mesh resolution: {ex.Message}");
        }
        resolveStopwatch.Stop();
        log($"[StaticMesh/Analysis] Resolve batch ({resolveStopwatch.Elapsed.TotalSeconds:F2}s)");

        var buildStopwatch = Stopwatch.StartNew();
        var result = meshBuilder.Build(source.UnrFile);
        buildStopwatch.Stop();
        log($"[StaticMesh/Analysis] BuildInstancedMeshResult ({buildStopwatch.Elapsed.TotalSeconds:F2}s)");
        return result;
    }
}
