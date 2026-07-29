using System;
using System.Diagnostics;
using System.Threading.Tasks;

internal static class MapImportOrchestrator
{
    public static Task ImportTerrain(MapImportRequest request, Action<string> log, MapImportExecutionContext context = null)
    {
        return Run(async () =>
        {
            context?.Report("Load Map", request.MapRelativePath, 0.02f);
            var source = await Ue2MapLoader.LoadAsync(request, log, context);
            context?.Report("Import Terrain", request.MapKey, 0.10f);
            await TerrainMapImporter.ImportAsync(request, source, log);
            context?.Report("Finalize", request.MapKey, 1f);
        }, "Import Terrain", log, context);
    }

    public static Task ImportMeshes(MapImportRequest request, Action<string> log, MapImportExecutionContext context = null)
    {
        return Run(async () =>
        {
            context?.Report("Load Map", request.MapRelativePath, 0.02f);
            var source = await Ue2MapLoader.LoadAsync(request, log, context);
            context?.Report("Import Static Meshes", request.MapKey, 0.10f);
            await StaticMeshMapImporter.ImportAsync(request, source, log, context);
            context?.Report("Finalize", request.MapKey, 1f);
        }, "Import Meshes", log, context);
    }

    public static Task ImportBsp(MapImportRequest request, Action<string> log, MapImportExecutionContext context = null)
    {
        return Run(async () =>
        {
            context?.Report("Load Map", request.MapRelativePath, 0.02f);
            var source = await Ue2MapLoader.LoadAsync(request, log, context);
            context?.Report("Import BSP", request.MapKey, 0.10f);
            await BspMapImporter.ImportAsync(request, source, log, context, finalizeScene: false);
            MapContextImporter.ImportAsync(request, source, log, finalizeScene: false);
            MapImportFinalizer.Complete(UnitySceneObjectUtility.CreateMapRoot(request.ObjectName), log);
            context?.Report("Finalize", request.MapKey, 1f);
        }, "Import BSP", log, context);
    }

    public static Task ImportLights(MapImportRequest request, Action<string> log, MapImportExecutionContext context = null)
    {
        return Run(async () =>
        {
            context?.Report("Load Map", request.MapRelativePath, 0.02f);
            var source = await Ue2MapLoader.LoadAsync(request, log, context);
            context?.Report("Import Lights", request.MapKey, 0.10f);
            await LightingMapImporter.ImportAsync(request, source, log, context);
            context?.Report("Finalize", request.MapKey, 1f);
        }, "Import Lights", log, context);
    }

    public static Task ImportVolumes(MapImportRequest request, Action<string> log, MapImportExecutionContext context = null)
    {
        return Run(async () =>
        {
            context?.Report("Load Map", request.MapRelativePath, 0.02f);
            var source = await Ue2MapLoader.LoadAsync(request, log, context);
            context?.Report("Import Volumes", request.MapKey, 0.10f);
            await VolumeMapImporter.ImportAsync(request, source, log, context, finalizeScene: false);
            MapContextImporter.ImportAsync(request, source, log, finalizeScene: false);
            MapImportFinalizer.Complete(UnitySceneObjectUtility.CreateMapRoot(request.ObjectName), log);
            context?.Report("Finalize", request.MapKey, 1f);
        }, "Import Volumes", log, context);
    }

    public static Task ImportParticles(MapImportRequest request, Action<string> log, MapImportExecutionContext context = null)
    {
        return Run(async () =>
        {
            context?.Report("Load Map", request.MapRelativePath, 0.02f);
            var source = await Ue2MapLoader.LoadAsync(request, log, context);
            context?.Report("Import Particles", request.MapKey, 0.10f);
            ParticleMapImporter.ImportAsync(request, source, log, context);
            context?.Report("Finalize", request.MapKey, 1f);
        }, "Import Particles", log, context);
    }

    public static Task ImportCreatures(MapImportRequest request, Action<string> log, MapImportExecutionContext context = null)
    {
        return Run(async () =>
        {
            context?.Report("Load Map", request.MapRelativePath, 0.02f);
            var source = await Ue2MapLoader.LoadAsync(request, log, context);
            context?.Report("Import Creatures", request.MapKey, 0.10f);
            await CreatureMapImporter.ImportAsync(request, source, log, context);
            context?.Report("Finalize", request.MapKey, 1f);
        }, "Import Creatures", log, context);
    }

    public static Task ImportAll(MapImportRequest request, Action<string> log, MapImportExecutionContext context = null)
    {
        return Run(async () =>
        {
            context?.Report("Load Map", request.MapRelativePath, 0.02f);
            var source = await Ue2MapLoader.LoadAsync(request, log, context);

            context?.Report("Import Terrain", request.MapKey, 0.08f);
            await TerrainMapImporter.ImportAsync(request, source, log, finalizeScene: false, buildTerrainVegetation: true);
            context?.Report("Import Static Meshes", request.MapKey, 0.22f);
            await StaticMeshMapImporter.ImportAsync(request, source, log, context, finalizeScene: false, convertTerrainDecorationsToTerrainVegetation: true);
            context?.Report("Import BSP", request.MapKey, 0.38f);
            await BspMapImporter.ImportAsync(request, source, log, context, finalizeScene: false);
            context?.Report("Import Lights", request.MapKey, 0.54f);
            await LightingMapImporter.ImportAsync(request, source, log, context, finalizeScene: false);
            context?.Report("Import Volumes", request.MapKey, 0.68f);
            await VolumeMapImporter.ImportAsync(request, source, log, context, finalizeScene: false);
            context?.Report("Import Particles", request.MapKey, 0.80f);
            ParticleMapImporter.ImportAsync(request, source, log, context, finalizeScene: false);
            context?.Report("Import Creatures", request.MapKey, 0.88f);
            await CreatureMapImporter.ImportAsync(request, source, log, context, finalizeScene: false);
            context?.Report("Finalize", request.MapKey, 0.98f);
            MapContextImporter.ImportAsync(request, source, log, finalizeScene: false);
            MapImportFinalizer.Complete(UnitySceneObjectUtility.CreateMapRoot(request.ObjectName), log);
            context?.Report("Done", request.MapKey, 1f);
        }, "Import All", log, context);
    }

    private static async Task Run(Func<Task> action, string operationName, Action<string> log, MapImportExecutionContext context)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await action();
            stopwatch.Stop();
            log($"{operationName} took {stopwatch.Elapsed.TotalSeconds:F2}s");
        }
        catch (OperationCanceledException)
        {
            stopwatch.Stop();
            log($"{operationName} cancelled after {stopwatch.Elapsed.TotalSeconds:F2}s");
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            log($"Error during {operationName}: {ex.Message}");
            UnityEngine.Debug.LogException(ex);
        }
        finally
        {
            context?.Dispose();
        }
    }
}
