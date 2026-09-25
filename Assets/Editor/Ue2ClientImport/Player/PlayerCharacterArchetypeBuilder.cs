using System;
using L2Viewer.SceneDomain.Models;

internal static class PlayerCharacterArchetypeBuilder
{
    internal readonly struct ImportResult
    {
        public ImportResult(string prefabPath, string archetypeAssetPath)
        {
            PrefabPath = prefabPath;
            ArchetypeAssetPath = archetypeAssetPath;
        }

        public string PrefabPath { get; }
        public string ArchetypeAssetPath { get; }
    }

    public static ImportResult Import(
        string clientRoot,
        string dbRoot,
        SceneCharacterBaseClass baseClass,
        SceneCharacterGender gender,
        SceneCharacterAppearanceOptionsData appearanceOptions,
        SceneCharacterEquipmentCatalogData equipmentCatalog,
        MapImportExecutionContext context,
        Action<string> log)
    {
        return PlayerCharacterBatchPipeline.Import(
            clientRoot,
            dbRoot,
            baseClass,
            gender,
            appearanceOptions,
            equipmentCatalog,
            context);
    }
}
