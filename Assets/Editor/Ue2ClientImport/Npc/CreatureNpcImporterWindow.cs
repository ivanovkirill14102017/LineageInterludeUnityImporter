using System;
using UnityEditor;
using UnityEngine;

public sealed class CreatureNpcImporterWindow : EditorWindow
{
    private CreatureNpcImporterPanel _panel;
    private ImportDashboardStatus _status;

    [MenuItem("L2/Import Creature Prefab")]
    private static void OpenWindow()
    {
        var window = GetWindow<CreatureNpcImporterWindow>("Creature Import");
        window.minSize = new Vector2(620f, 320f);
        window.Show();
    }

    private void OnEnable()
    {
        _status = new ImportDashboardStatus(Repaint);
        _panel = new CreatureNpcImporterPanel(_status);
    }

    private void OnGUI()
    {
        _status ??= new ImportDashboardStatus(Repaint);
        _panel ??= new CreatureNpcImporterPanel(_status);
        ImportDashboardFields.DrawPaths();
        _panel.OnGUI();
        _status.Draw();
    }
}

internal sealed class CreatureNpcImporterPanel
{
    private static readonly string DefaultCreatureId = string.Empty;

    private readonly ImportDashboardStatus _status;
    private string _creatureId = DefaultCreatureId;

    public CreatureNpcImporterPanel(ImportDashboardStatus status)
    {
        _status = status;
    }

    public void OnGUI()
    {
        EditorGUILayout.LabelField("Import a SceneDomain creature prefab", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            _creatureId = EditorGUILayout.TextField("Creature", _creatureId);
            EditorGUILayout.LabelField("Example", "25286, LineageMonster2.anakim, or anakim_m00");
        }

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
        {
            if (GUILayout.Button("Import Creature", GUILayout.Height(34f)))
            {
                ImportCurrentMesh();
            }
        }

    }

    private void ImportCurrentMesh()
    {
        try
        {
            _status.Set($"Import started for '{_creatureId}'...");
            var result = CreatureNpcImportBuilder.ImportByIdentifier(ConstInfo.L2GameClientPath, _creatureId, AppendStatus);
            _status.Append($"Done. Prefab: {result.PrefabPath}");
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
}
