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
        _mapImporter = new MapImporterPanel(Repaint);
        _creatureImporter = new CreatureNpcImporterPanel(Repaint);
        _nonPermanentVisualImporter = new NonPermanentVisualImporterPanel(Repaint);
        _playerCharacterImporter = new PlayerCharacterImporterPanel(Repaint);
        _skillVisualImporter = new SkillVisualImporterPanel(Repaint);
    }

    private void OnDisable()
    {
        _mapImporter?.Dispose();
        _mapImporter = null;
        _creatureImporter = null;
        _nonPermanentVisualImporter = null;
        _playerCharacterImporter = null;
        _skillVisualImporter = null;
    }

    private void OnGUI()
    {
        EnsurePanels();

        using (var scroll = new EditorGUILayout.ScrollViewScope(_scroll))
        {
            _scroll = scroll.scrollPosition;

            EditorGUILayout.LabelField("Lineage II Interlude Import Startup", EditorStyles.boldLabel);
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
        }
    }

    private void EnsurePanels()
    {
        _mapImporter ??= new MapImporterPanel(Repaint);
        _creatureImporter ??= new CreatureNpcImporterPanel(Repaint);
        _nonPermanentVisualImporter ??= new NonPermanentVisualImporterPanel(Repaint);
        _playerCharacterImporter ??= new PlayerCharacterImporterPanel(Repaint);
        _skillVisualImporter ??= new SkillVisualImporterPanel(Repaint);
    }
}
