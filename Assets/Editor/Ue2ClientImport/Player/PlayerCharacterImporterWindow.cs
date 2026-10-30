using System;
using System.IO;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services.CharacterServices;
using UnityEditor;
using UnityEngine;

public sealed class PlayerCharacterImporterWindow : EditorWindow
{
    private PlayerCharacterImporterPanel _panel;
    private ImportDashboardStatus _status;

    [MenuItem("L2/Import Player Character Archetype")]
    private static void OpenWindow()
    {
        var window = GetWindow<PlayerCharacterImporterWindow>("Player Character Archetype");
        window.minSize = new Vector2(760f, 520f);
        window.Show();
    }

    private void OnEnable()
    {
        _status = new ImportDashboardStatus(Repaint);
        _panel = new PlayerCharacterImporterPanel(_status);
    }

    private void OnGUI()
    {
        _status ??= new ImportDashboardStatus(Repaint);
        _panel ??= new PlayerCharacterImporterPanel(_status);
        ImportDashboardFields.DrawPaths();
        _panel.OnGUI();
        _status.Draw();
    }
}

internal sealed class PlayerCharacterImporterPanel
{
    private readonly ImportDashboardStatus _status;
    private SceneCharacterBaseClass _baseClass = SceneCharacterBaseClass.HumanFighter;
    private SceneCharacterGender _gender = SceneCharacterGender.Male;
    private SceneCharacterAppearanceOptionsData _appearanceOptions;
    private SceneCharacterEquipmentCatalogData _equipmentCatalog;
    private SceneCharacterBaseClass _loadedBaseClass;
    private SceneCharacterGender _loadedGender;

    public PlayerCharacterImporterPanel(ImportDashboardStatus status)
    {
        _status = status;
    }

    public void OnGUI()
    {
        EditorGUILayout.LabelField("Import a player-character wardrobe archetype prefab", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            var newBaseClass = (SceneCharacterBaseClass)EditorGUILayout.EnumPopup("Base Class", _baseClass);
            var newGender = (SceneCharacterGender)EditorGUILayout.EnumPopup("Gender", _gender);
            if (newBaseClass != _baseClass || newGender != _gender)
            {
                _baseClass = newBaseClass;
                _gender = newGender;
            }

        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
        {
            if (GUILayout.Button("Import Character Archetype", GUILayout.Height(28f)))
            {
                ImportCurrentCharacter();
            }
        }

    }

    private bool HasLoadedAppearanceDataForCurrentArchetype()
    {
        return _appearanceOptions != null &&
               _equipmentCatalog != null &&
               _loadedBaseClass == _baseClass &&
               _loadedGender == _gender;
    }

    private void EnsureCatalogLoaded(bool forceReload = false)
    {
        if (!forceReload && HasLoadedAppearanceDataForCurrentArchetype())
        {
            return;
        }

        ConstInfo.L2DbRootPath = NormalizeDbRoot(ConstInfo.L2DbRootPath);
        _status.Set($"Loading archetype catalog for '{_gender} {_baseClass}'...");
        var optionsBuilder = new SceneCharacterAppearanceOptionsBuilder();
        _appearanceOptions = optionsBuilder.Build(ConstInfo.L2GameClientPath, _baseClass, _gender);

        var catalogBuilder = new SceneCharacterEquipmentCatalogBuilder();
        _equipmentCatalog = catalogBuilder.Build(ConstInfo.L2GameClientPath, ConstInfo.L2DbRootPath, _baseClass, _gender);
        _loadedBaseClass = _baseClass;
        _loadedGender = _gender;
        _status.Append($"Loaded: Faces={_appearanceOptions.FaceOptions.Count}, HairStyles={_appearanceOptions.HairStyleOptions.Count}, Slots={_equipmentCatalog.Slots.Count}.");
        if (_equipmentCatalog.Warnings.Count > 0)
        {
            _status.Append($"Catalog warnings: {_equipmentCatalog.Warnings.Count}");
        }
    }

    private void ImportCurrentCharacter()
    {
        try
        {
            ConstInfo.L2DbRootPath = NormalizeDbRoot(ConstInfo.L2DbRootPath);

            _status.Set($"Archetype import started for '{_gender} {_baseClass}'...");
            using var context = new MapImportExecutionContext("Import Player Character Archetype");
            var result = PlayerCharacterArchetypeBuilder.Import(
                ConstInfo.L2GameClientPath,
                ConstInfo.L2DbRootPath,
                _baseClass,
                _gender,
                HasLoadedAppearanceDataForCurrentArchetype() ? _appearanceOptions : null,
                HasLoadedAppearanceDataForCurrentArchetype() ? _equipmentCatalog : null,
                context,
                AppendStatus);
            _status.Append($"Done. Prefab: {result.PrefabPath}");
        }
        catch (OperationCanceledException)
        {
            _status.Append("Import cancelled by user.");
        }
        catch (Exception exception)
        {
            _status.Set(exception.ToString());
            Debug.LogException(exception);
        }
    }

    private void AppendStatus(string message)
    {
        _status.Append(message);
    }

    private static string NormalizeDbRoot(string dbRoot)
    {
        if (string.IsNullOrWhiteSpace(dbRoot))
        {
            return dbRoot;
        }

        if (File.Exists(Path.Combine(dbRoot, "armor.json")) &&
            File.Exists(Path.Combine(dbRoot, "weapon.json")))
        {
            return dbRoot;
        }

        var candidate = Path.Combine(dbRoot, "InterludeDb");
        if (File.Exists(Path.Combine(candidate, "armor.json")) &&
            File.Exists(Path.Combine(candidate, "weapon.json")))
        {
            return candidate;
        }

        if (Directory.Exists(dbRoot))
        {
            var armorPath = Directory.EnumerateFiles(dbRoot, "armor.json", SearchOption.AllDirectories).FirstOrDefault();
            var weaponPath = Directory.EnumerateFiles(dbRoot, "weapon.json", SearchOption.AllDirectories).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(armorPath) &&
                !string.IsNullOrWhiteSpace(weaponPath) &&
                string.Equals(Path.GetDirectoryName(armorPath), Path.GetDirectoryName(weaponPath), StringComparison.OrdinalIgnoreCase))
            {
                return Path.GetDirectoryName(armorPath);
            }
        }

        return dbRoot;
    }
}
