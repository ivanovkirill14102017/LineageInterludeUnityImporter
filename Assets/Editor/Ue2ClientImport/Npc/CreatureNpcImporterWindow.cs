using System;
using UnityEditor;
using UnityEngine;

public sealed class CreatureNpcImporterWindow : EditorWindow
{
    private static readonly string DefaultCreatureId = string.Empty;

    private string _creatureId = DefaultCreatureId;
    private string _status = "Ready to import a SceneDomain creature prefab.";
    private Vector2 _scroll;

    [MenuItem("L2/Import Creature Prefab")]
    private static void OpenWindow()
    {
        var window = GetWindow<CreatureNpcImporterWindow>("Creature Import");
        window.minSize = new Vector2(620f, 320f);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Import a SceneDomain creature prefab", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Client", ConstInfo.L2GameClientPath);
            _creatureId = EditorGUILayout.TextField("Creature", _creatureId);
            EditorGUILayout.LabelField("Prefab Output", CreatureNpcImportBuilder.PrefabOutputRoot);
            EditorGUILayout.LabelField("Asset Output", CreatureNpcImportBuilder.AssetOutputRoot);
            EditorGUILayout.LabelField("Example", "PF_orc_fighter_m00 or orc_fighter_m00");
            EditorGUILayout.LabelField("Note", "This importer uses the same Unity-side skinned mesh + AnimationClip + AnimatorController pipeline as map creature import.");
        }

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Import Creature", GUILayout.Height(34f)))
                {
                    ImportCurrentMesh();
                }
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

    private void ImportCurrentMesh()
    {
        try
        {
            _status = $"Import started for '{_creatureId}'...";
            var result = CreatureNpcImportBuilder.ImportByIdentifier(ConstInfo.L2GameClientPath, _creatureId, AppendStatus);
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
