using System;
using System.Diagnostics;
using System.Threading.Tasks;

internal static class MapImportOrchestrator
{
    public static Task ImportTerrain(MapImportRequest request, Action<string> log)
    {
        return Run(async () =>
        {
            log("[Import Terrain] START Load map");
            var source = await Ue2MapLoader.LoadAsync(request, log);
            log("[Import Terrain] DONE Load map");

            log("[Import Terrain] START Terrain stage");
            var stopwatch = Stopwatch.StartNew();
            await TerrainMapImporter.ImportAsync(request, source, log);
            stopwatch.Stop();
            log($"Terrain stage took {stopwatch.Elapsed.TotalSeconds:F2}s");
            log("[Import Terrain] DONE Terrain stage");
        }, "Import Terrain", log);
    }

    public static Task ImportMeshes(MapImportRequest request, Action<string> log)
    {
        return Run(async () =>
        {
            log("[Import Meshes] START Load map");
            var source = await Ue2MapLoader.LoadAsync(request, log);
            log("[Import Meshes] DONE Load map");

            log("[Import Meshes] START Static mesh stage");
            var stopwatch = Stopwatch.StartNew();
            await StaticMeshMapImporter.ImportAsync(request, source, log);
            stopwatch.Stop();
            log($"Static mesh stage took {stopwatch.Elapsed.TotalSeconds:F2}s");
            log("[Import Meshes] DONE Static mesh stage");
        }, "Import Meshes", log);
    }

    public static Task ImportBsp(MapImportRequest request, Action<string> log)
    {
        return Run(async () =>
        {
            log("[Import BSP] START Load map");
            var source = await Ue2MapLoader.LoadAsync(request, log);
            log("[Import BSP] DONE Load map");

            log("[Import BSP] START BSP stage");
            var bspStopwatch = Stopwatch.StartNew();
            await BspMapImporter.ImportAsync(request, source, log);
            bspStopwatch.Stop();
            log($"BSP stage took {bspStopwatch.Elapsed.TotalSeconds:F2}s");
            log("[Import BSP] DONE BSP stage");

            log("[Import BSP] START Map context stage");
            var contextStopwatch = Stopwatch.StartNew();
            await MapContextImporter.ImportAsync(request, source, log);
            contextStopwatch.Stop();
            log($"Map context stage took {contextStopwatch.Elapsed.TotalSeconds:F2}s");
            log("[Import BSP] DONE Map context stage");
        }, "Import BSP", log);
    }

    public static Task ImportLights(MapImportRequest request, Action<string> log)
    {
        return Run(async () =>
        {
            log("[Import Lights] START Load map");
            var source = await Ue2MapLoader.LoadAsync(request, log);
            log("[Import Lights] DONE Load map");

            log("[Import Lights] START Lighting stage");
            var stopwatch = Stopwatch.StartNew();
            await LightingMapImporter.ImportAsync(request, source, log);
            stopwatch.Stop();
            log($"Lighting stage took {stopwatch.Elapsed.TotalSeconds:F2}s");
            log("[Import Lights] DONE Lighting stage");
        }, "Import Lights", log);
    }

    public static Task ImportVolumes(MapImportRequest request, Action<string> log)
    {
        return Run(async () =>
        {
            log("[Import Volumes] START Load map");
            var source = await Ue2MapLoader.LoadAsync(request, log);
            log("[Import Volumes] DONE Load map");

            log("[Import Volumes] START Volume stage");
            var volumeStopwatch = Stopwatch.StartNew();
            await VolumeMapImporter.ImportAsync(request, source, log);
            volumeStopwatch.Stop();
            log($"Volume stage took {volumeStopwatch.Elapsed.TotalSeconds:F2}s");
            log("[Import Volumes] DONE Volume stage");

            log("[Import Volumes] START Map context stage");
            var contextStopwatch = Stopwatch.StartNew();
            await MapContextImporter.ImportAsync(request, source, log);
            contextStopwatch.Stop();
            log($"Map context stage took {contextStopwatch.Elapsed.TotalSeconds:F2}s");
            log("[Import Volumes] DONE Map context stage");
        }, "Import Volumes", log);
    }

    public static Task ImportParticles(MapImportRequest request, Action<string> log)
    {
        return Run(async () =>
        {
            log("[Import Particles] START Load map");
            var source = await Ue2MapLoader.LoadAsync(request, log);
            log("[Import Particles] DONE Load map");

            log("[Import Particles] START Particle stage");
            var stopwatch = Stopwatch.StartNew();
            await ParticleMapImporter.ImportAsync(request, source, log);
            stopwatch.Stop();
            log($"Particle stage took {stopwatch.Elapsed.TotalSeconds:F2}s");
            log("[Import Particles] DONE Particle stage");
        }, "Import Particles", log);
    }

    public static Task ImportAll(MapImportRequest request, Action<string> log)
    {
        return Run(async () =>
        {
            log("[Import All] START Load map");
            var source = await Ue2MapLoader.LoadAsync(request, log);
            log("[Import All] DONE Load map");

            log("[Import All] START Terrain stage");
            var terrainStopwatch = Stopwatch.StartNew();
            await TerrainMapImporter.ImportAsync(request, source, log);
            terrainStopwatch.Stop();
            log($"Terrain stage took {terrainStopwatch.Elapsed.TotalSeconds:F2}s");
            log("[Import All] DONE Terrain stage");

            log("[Import All] START Static mesh stage");
            var staticMeshStopwatch = Stopwatch.StartNew();
            await StaticMeshMapImporter.ImportAsync(request, source, log);
            staticMeshStopwatch.Stop();
            log($"Static mesh stage took {staticMeshStopwatch.Elapsed.TotalSeconds:F2}s");
            log("[Import All] DONE Static mesh stage");

            log("[Import All] START BSP stage");
            var bspStopwatch = Stopwatch.StartNew();
            await BspMapImporter.ImportAsync(request, source, log);
            bspStopwatch.Stop();
            log($"BSP stage took {bspStopwatch.Elapsed.TotalSeconds:F2}s");
            log("[Import All] DONE BSP stage");

            log("[Import All] START Lighting stage");
            var lightingStopwatch = Stopwatch.StartNew();
            await LightingMapImporter.ImportAsync(request, source, log);
            lightingStopwatch.Stop();
            log($"Lighting stage took {lightingStopwatch.Elapsed.TotalSeconds:F2}s");
            log("[Import All] DONE Lighting stage");

            log("[Import All] START Volume stage");
            var volumeStopwatch = Stopwatch.StartNew();
            await VolumeMapImporter.ImportAsync(request, source, log);
            volumeStopwatch.Stop();
            log($"Volume stage took {volumeStopwatch.Elapsed.TotalSeconds:F2}s");
            log("[Import All] DONE Volume stage");

            log("[Import All] START Particle stage");
            var particleStopwatch = Stopwatch.StartNew();
            await ParticleMapImporter.ImportAsync(request, source, log);
            particleStopwatch.Stop();
            log($"Particle stage took {particleStopwatch.Elapsed.TotalSeconds:F2}s");
            log("[Import All] DONE Particle stage");

            log("[Import All] START Map context stage");
            var contextStopwatch = Stopwatch.StartNew();
            await MapContextImporter.ImportAsync(request, source, log);
            contextStopwatch.Stop();
            log($"Map context stage took {contextStopwatch.Elapsed.TotalSeconds:F2}s");
            log("[Import All] DONE Map context stage");

            log("Import All finished.");
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
