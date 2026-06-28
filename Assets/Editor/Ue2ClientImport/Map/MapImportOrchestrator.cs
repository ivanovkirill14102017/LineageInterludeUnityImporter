using System;
using System.Diagnostics;
using System.Threading.Tasks;

internal static class MapImportOrchestrator
{
    public static Task ImportTerrain(MapImportRequest request, Action<string> log)
    {
        return Run(async () =>
        {
            var source = await Ue2MapLoader.LoadAsync(request, log);
            await TerrainMapImporter.ImportAsync(request, source, log);
        }, "Import Terrain", log);
    }

    public static Task ImportMeshes(MapImportRequest request, Action<string> log)
    {
        return Run(async () =>
        {
            var source = await Ue2MapLoader.LoadAsync(request, log);
            await StaticMeshMapImporter.ImportAsync(request, source, log);
        }, "Import Meshes", log);
    }

    public static Task ImportBsp(MapImportRequest request, Action<string> log)
    {
        return Run(async () =>
        {
            var source = await Ue2MapLoader.LoadAsync(request, log);
            await BspMapImporter.ImportAsync(request, source, log);
            MapContextImporter.ImportAsync(request, source, log);
        }, "Import BSP", log);
    }

    public static Task ImportLights(MapImportRequest request, Action<string> log)
    {
        return Run(async () =>
        {
            var source = await Ue2MapLoader.LoadAsync(request, log);
            await LightingMapImporter.ImportAsync(request, source, log);
        }, "Import Lights", log);
    }

    public static Task ImportVolumes(MapImportRequest request, Action<string> log)
    {
        return Run(async () =>
        {
            var source = await Ue2MapLoader.LoadAsync(request, log);
            await VolumeMapImporter.ImportAsync(request, source, log);
            MapContextImporter.ImportAsync(request, source, log);
        }, "Import Volumes", log);
    }

    public static Task ImportParticles(MapImportRequest request, Action<string> log)
    {
        return Run(async () =>
        {
            var source = await Ue2MapLoader.LoadAsync(request, log);
            ParticleMapImporter.ImportAsync(request, source, log);
        }, "Import Particles", log);
    }

    public static Task ImportCreatures(MapImportRequest request, Action<string> log)
    {
        return Run(async () =>
        {
            var source = await Ue2MapLoader.LoadAsync(request, log);
            await CreatureMapImporter.ImportAsync(request, source, log);
        }, "Import Creatures", log);
    }

    public static Task ImportAll(MapImportRequest request, Action<string> log)
    {
        return Run(async () =>
        {
            var source = await Ue2MapLoader.LoadAsync(request, log);

            await TerrainMapImporter.ImportAsync(request, source, log);
            await StaticMeshMapImporter.ImportAsync(request, source, log);
            await BspMapImporter.ImportAsync(request, source, log);
            await LightingMapImporter.ImportAsync(request, source, log);
            await VolumeMapImporter.ImportAsync(request, source, log);
            ParticleMapImporter.ImportAsync(request, source, log);
            await CreatureMapImporter.ImportAsync(request, source, log);
            MapContextImporter.ImportAsync(request, source, log);
        }, "Import All", log);
    }

    private static async Task Run(Func<Task> action, string operationName, Action<string> log)
    {
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            log($"Error during {operationName}: {ex.Message}");
            UnityEngine.Debug.LogException(ex);
        }
    }
}
