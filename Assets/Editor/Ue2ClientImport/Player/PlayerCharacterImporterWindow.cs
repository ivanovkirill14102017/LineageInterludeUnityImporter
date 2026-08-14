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

    [MenuItem("L2/Import Player Character Archetype")]
    private static void OpenWindow()
    {
        var window = GetWindow<PlayerCharacterImporterWindow>("Player Character Archetype");
        window.minSize = new Vector2(760f, 520f);
        window.Show();
    }

    private void OnEnable()
    {
        _panel = new PlayerCharacterImporterPanel(Repaint);
    }

    private void OnGUI()
    {
        _panel ??= new PlayerCharacterImporterPanel(Repaint);
        _panel.OnGUI();
    }
}

internal sealed class PlayerCharacterImporterPanel
{
    private readonly Action _repaint;
    private SceneCharacterBaseClass _baseClass = SceneCharacterBaseClass.HumanFighter;
    private SceneCharacterGender _gender = SceneCharacterGender.Male;
    private string _status = "Ready to import a player-character wardrobe archetype prefab.";
    private Vector2 _scroll;
    private SceneCharacterAppearanceOptionsData _appearanceOptions;
    private SceneCharacterEquipmentCatalogData _equipmentCatalog;
    private SceneCharacterBaseClass _loadedBaseClass;
    private SceneCharacterGender _loadedGender;

    public PlayerCharacterImporterPanel(Action repaint)
    {
        _repaint = repaint;
    }

    public void OnGUI()
    {
        EditorGUILayout.LabelField("Import a player-character wardrobe archetype prefab", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Client", ConstInfo.L2GameClientPath);
            ConstInfo.L2DbRootPath = EditorGUILayout.TextField("DB Root", ConstInfo.L2DbRootPath);
            var newBaseClass = (SceneCharacterBaseClass)EditorGUILayout.EnumPopup("Base Class", _baseClass);
            var newGender = (SceneCharacterGender)EditorGUILayout.EnumPopup("Gender", _gender);
            if (newBaseClass != _baseClass || newGender != _gender)
            {
                _baseClass = newBaseClass;
                _gender = newGender;
            }

            EditorGUILayout.LabelField("Prefab Output", PlayerCharacterImportBuilder.PrefabOutputRoot);
            EditorGUILayout.LabelField("Asset Output", PlayerCharacterImportBuilder.AssetOutputRoot);
        }

        EditorGUILayout.Space();
        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
        {
            if (GUILayout.Button("Import Character Archetype", GUILayout.Height(28f)))
            {
                ImportCurrentCharacter();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
        using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.MinHeight(120f)))
        {
            _scroll = scroll.scrollPosition;
            EditorGUILayout.TextArea(_status, GUILayout.ExpandHeight(true));
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
        _status = $"Loading archetype catalog for '{_gender} {_baseClass}'...";
        var optionsBuilder = new SceneCharacterAppearanceOptionsBuilder();
        _appearanceOptions = optionsBuilder.Build(ConstInfo.L2GameClientPath, _baseClass, _gender);

        var catalogBuilder = new SceneCharacterEquipmentCatalogBuilder();
        _equipmentCatalog = catalogBuilder.Build(ConstInfo.L2GameClientPath, ConstInfo.L2DbRootPath, _baseClass, _gender);
        _loadedBaseClass = _baseClass;
        _loadedGender = _gender;
        _status += $"\nLoaded: Faces={_appearanceOptions.FaceOptions.Count}, HairStyles={_appearanceOptions.HairStyleOptions.Count}, Slots={_equipmentCatalog.Slots.Count}.";
        if (_equipmentCatalog.Warnings.Count > 0)
        {
            _status += $"\nCatalog warnings: {_equipmentCatalog.Warnings.Count}";
        }
    }

    private void ImportCurrentCharacter()
    {
        try
        {
            ConstInfo.L2DbRootPath = NormalizeDbRoot(ConstInfo.L2DbRootPath);

            _status = $"Archetype import started for '{_gender} {_baseClass}'...";
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
            _status += $"\nDone. Prefab: {result.PrefabPath}";
        }
        catch (OperationCanceledException)
        {
            _status = $"{_status}\nImport cancelled by user.";
        }
        catch (Exception exception)
        {
            _status = exception.ToString();
            Debug.LogException(exception);
        }
    }

    private void AppendStatus(string message)
    {
        _status = $"{_status}\n{message}";
        _repaint?.Invoke();
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
