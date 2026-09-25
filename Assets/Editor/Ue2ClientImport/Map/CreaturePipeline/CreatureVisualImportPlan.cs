using L2Viewer.SceneDomain.Models;

internal sealed class CreatureImportPlan
{
    public CreatureImportPlan(
        SceneCreatureSpawnData[] spawns,
        CreatureVisualImportPlan[] visuals,
        CreatureCompositionImportPlan[] compositions)
    {
        Spawns = spawns;
        Visuals = visuals;
        Compositions = compositions;
    }

    public SceneCreatureSpawnData[] Spawns { get; }
    public CreatureVisualImportPlan[] Visuals { get; }
    public CreatureCompositionImportPlan[] Compositions { get; }
}

internal sealed class CreatureVisualImportPlan
{
    public CreatureVisualImportPlan(
        string key,
        SceneSkeletalMeshResourceReference meshReference,
        SceneSurfaceResourceReference[] textureReferences,
        SceneCreatureSpawnData source)
    {
        Key = key;
        MeshReference = meshReference;
        TextureReferences = textureReferences;
        Source = source;
    }

    public string Key { get; }
    public SceneSkeletalMeshResourceReference MeshReference { get; }
    public SceneSurfaceResourceReference[] TextureReferences { get; }
    public SceneCreatureSpawnData Source { get; }
}

internal sealed class CreatureCompositionImportPlan
{
    public CreatureCompositionImportPlan(
        string key,
        CreatureVisualImportPlan visual,
        SceneCreatureSpawnData source,
        SceneCreatureSpawnData[] spawns)
    {
        Key = key;
        Visual = visual;
        Source = source;
        Spawns = spawns;
    }

    public string Key { get; }
    public CreatureVisualImportPlan Visual { get; }
    public SceneCreatureSpawnData Source { get; }
    public SceneCreatureSpawnData[] Spawns { get; }
}
