using System;
using L2Viewer.SceneDomain.Models;
using UnityEditor;
using UnityEngine;

public sealed class PlayerCharacterImporterWindow : EditorWindow
{
    private SceneCharacterBaseClass _baseClass = SceneCharacterBaseClass.HumanFighter;
    private SceneCharacterGender _gender = SceneCharacterGender.Male;
    private string _status = "Ready to import a player-character skeleton debug prefab through SceneDomain.";
    private Vector2 _scroll;

    [MenuItem("L2/Import Player Character Debug Prefab")]
    private static void OpenWindow()
    {
        var window = GetWindow<PlayerCharacterImporterWindow>("Player Character Import");
        window.minSize = new Vector2(680f, 340f);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Import a player-character skeleton debug prefab", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Client", ConstInfo.L2GameClientPath);
            _baseClass = (SceneCharacterBaseClass)EditorGUILayout.EnumPopup("Base Class", _baseClass);
            _gender = (SceneCharacterGender)EditorGUILayout.EnumPopup("Gender", _gender);
            EditorGUILayout.LabelField("Prefab Output", PlayerCharacterImportBuilder.PrefabOutputRoot);
            EditorGUILayout.LabelField("Asset Output", PlayerCharacterImportBuilder.AssetOutputRoot);
            EditorGUILayout.LabelField("Note", "Unity only asks SceneDomain for the player appearance/skeleton resource and the resolved skeletal asset. No client package scanning or UKX resolving happens in Unity importer code.");
        }

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
        {
            if (GUILayout.Button("Import Player Character", GUILayout.Height(34f)))
            {
                ImportCurrentCharacter();
            }
        }

        EditorGUILayout.Space();
        EditorGUILayout.LabelField("Status", EditorStyles.boldLabel);
        using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
        {
            _scroll = scroll.scrollPosition;
            EditorGUILayout.TextArea(_status, GUILayout.ExpandHeight(true));
        }
    }

    private void ImportCurrentCharacter()
    {
        try
        {
            _status = $"Import started for '{_gender} {_baseClass}'...";
            var result = PlayerCharacterImportBuilder.Import(ConstInfo.L2GameClientPath, _baseClass, _gender, AppendStatus);
            _status += $"\nDone. Prefab: {result.PrefabPath}";
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
        Repaint();
    }
}
