using System;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public sealed class L2StartupWindow : EditorWindow
{
    private const string SessionShownKey = "L2StartupWindow.Shown";

    private MapImporterPanel _mapImporter;
    private CreatureNpcImporterPanel _creatureImporter;
    private NonPermanentVisualImporterPanel _nonPermanentVisualImporter;
    private PlayerCharacterImporterPanel _playerCharacterImporter;
    private SkillVisualImporterPanel _skillVisualImporter;
    private ImportDashboardStatus _status;
    private Vector2 _scroll;

    static L2StartupWindow()
    {
        EditorApplication.delayCall += OpenOnProjectLoad;
    }

    [MenuItem("L2/Startup")]
    public static void OpenWindow()
    {
        var window = GetWindow<L2StartupWindow>("L2 Startup");
        window.minSize = new Vector2(900f, 720f);
        window.Show();
    }

    private static void OpenOnProjectLoad()
    {
        if (Application.isBatchMode || SessionState.GetBool(SessionShownKey, false))
        {
            return;
        }

        SessionState.SetBool(SessionShownKey, true);
        OpenWindow();
    }

    private void OnEnable()
    {
        _status = new ImportDashboardStatus(Repaint);
        EnsurePanels();
    }

    private void OnDisable()
    {
        _mapImporter?.Dispose();
        _mapImporter = null;
        _creatureImporter = null;
        _nonPermanentVisualImporter = null;
        _playerCharacterImporter = null;
        _skillVisualImporter = null;
        _status = null;
    }

    private void OnGUI()
    {
        EnsurePanels();

        using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
        {
            _scroll = scroll.scrollPosition;

            EditorGUILayout.LabelField("Lineage II Interlude Import Startup", EditorStyles.boldLabel);
            EditorGUILayout.Space(6f);
            ImportDashboardFields.DrawPaths();
            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _mapImporter.OnGUI();
            }

            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _creatureImporter.OnGUI();
            }

            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _nonPermanentVisualImporter.OnGUI();
            }

            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _playerCharacterImporter.OnGUI();
            }

            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _skillVisualImporter.OnGUI();
            }

            EditorGUILayout.Space(8f);
            _status.Draw();
        }
    }

    private void EnsurePanels()
    {
        _status ??= new ImportDashboardStatus(Repaint);
        _mapImporter ??= new MapImporterPanel(Repaint, _status);
        _creatureImporter ??= new CreatureNpcImporterPanel(_status);
        _nonPermanentVisualImporter ??= new NonPermanentVisualImporterPanel(_status);
        _playerCharacterImporter ??= new PlayerCharacterImporterPanel(_status);
        _skillVisualImporter ??= new SkillVisualImporterPanel(_status);
    }
}

internal static class ImportDashboardFields
{
    public static void DrawPaths()
    {
        ConstInfo.L2GameClientPath = EditorGUILayout.TextField("Client", ConstInfo.L2GameClientPath);
        ConstInfo.L2DbRootPath = EditorGUILayout.TextField("DB Root", ConstInfo.L2DbRootPath);
    }
}

internal sealed class ImportDashboardStatus
{
    private readonly Action _repaint;
    private string _current = "Ready to import.";
    private string _log = string.Empty;
    private Vector2 _scroll;

    public ImportDashboardStatus(Action repaint)
    {
        _repaint = repaint;
    }

    public void Set(string message)
    {
        _current = message;
        _log = string.IsNullOrEmpty(_log) ? message : $"{_log}\n{message}";
        _repaint?.Invoke();
    }

    public void Append(string message)
    {
        _log = string.IsNullOrEmpty(_log) ? message : $"{_log}\n{message}";
        _repaint?.Invoke();
    }

    public void Draw()
    {
        EditorGUILayout.LabelField("Status", _current, EditorStyles.boldLabel);
        using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll, GUILayout.MinHeight(120f)))
        {
            _scroll = scroll.scrollPosition;
            EditorGUILayout.TextArea(_log, GUILayout.ExpandHeight(true));
        }
    }
}
