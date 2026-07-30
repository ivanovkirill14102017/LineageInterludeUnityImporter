using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public sealed class L2ImportDashboardWindow : EditorWindow
{
    private const string SessionShownKey = "L2ImportDashboardWindow.Shown";

    private MapImporterPanel _mapImporter;
    private CreatureNpcImporterPanel _creatureImporter;
    private PlayerCharacterImporterPanel _playerCharacterImporter;
    private SkillVisualImporterPanel _skillVisualImporter;
    private Vector2 _scroll;

    static L2ImportDashboardWindow()
    {
        EditorApplication.delayCall += OpenOnProjectLoad;
    }

    [MenuItem("L2/Import Dashboard")]
    public static void OpenWindow()
    {
        var window = GetWindow<L2ImportDashboardWindow>("L2 Import Dashboard");
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
        _mapImporter = new MapImporterPanel(Repaint);
        _creatureImporter = new CreatureNpcImporterPanel(Repaint);
        _playerCharacterImporter = new PlayerCharacterImporterPanel(Repaint);
        _skillVisualImporter = new SkillVisualImporterPanel(Repaint);
    }

    private void OnDisable()
    {
        _mapImporter?.Dispose();
        _mapImporter = null;
        _creatureImporter = null;
        _playerCharacterImporter = null;
        _skillVisualImporter = null;
    }

    private void OnGUI()
    {
        EnsurePanels();

        using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
        {
            _scroll = scroll.scrollPosition;

            EditorGUILayout.LabelField("Lineage II Interlude Import", EditorStyles.boldLabel);
            EditorGUILayout.Space(6f);

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
                _playerCharacterImporter.OnGUI();
            }

            EditorGUILayout.Space(8f);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                _skillVisualImporter.OnGUI();
            }
        }
    }

    private void EnsurePanels()
    {
        _mapImporter ??= new MapImporterPanel(Repaint);
        _creatureImporter ??= new CreatureNpcImporterPanel(Repaint);
        _playerCharacterImporter ??= new PlayerCharacterImporterPanel(Repaint);
        _skillVisualImporter ??= new SkillVisualImporterPanel(Repaint);
    }
}
