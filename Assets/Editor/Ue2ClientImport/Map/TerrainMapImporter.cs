using System;
using System.Threading.Tasks;

internal static class TerrainMapImporter
{
    public static Task ImportAsync(
        MapImportRequest request,
        Ue2MapSource source,
        Action<string> log,
        bool finalizeScene = true,
        bool buildTerrainVegetation = true)
    {
        var terrain = StaticMeshImportPipeline.ReadTerrainSurface(source);
        var mapRoot = UnitySceneObjectUtility.CreateMapRoot(request.ObjectName);
        var vegetation = buildTerrainVegetation
            ? StaticMeshImportPipeline.Analyze(source)
            : null;
        if (vegetation != null)
        {
            StaticMeshImportPipeline.PrepareTerrainVegetationResources(
                vegetation,
                terrain,
                mapRoot,
                source,
                request);
        }

        MapImportAssetPreparation.PrepareTerrainOutputFolder(request.OutputDir);
        TerrainAssetBuilder.BuildTerrain(terrain, request, mapRoot);
        if (vegetation != null)
        {
            StaticMeshImportPipeline.PopulateTerrainVegetation(
                vegetation,
                terrain,
                mapRoot,
                source,
                request);
        }

        if (finalizeScene)
        {
            MapImportFinalizer.Complete(mapRoot, log);
        }

        return Task.CompletedTask;
    }
}
