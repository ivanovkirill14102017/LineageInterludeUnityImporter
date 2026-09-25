using System;
using System.Linq;
using L2Viewer.SceneDomain.Models;
using L2Viewer.SceneDomain.Services;
using UnityEditor;
using UnityEngine;

public sealed class SkillVisualImporterWindow : EditorWindow
{
    private SkillVisualImporterPanel _panel;

    [MenuItem("L2/Import Skill Visual")]
    private static void OpenWindow()
    {
        var window = GetWindow<SkillVisualImporterWindow>("Skill Visual Import");
        window.minSize = new Vector2(700f, 520f);
        window.Show();
    }

    private void OnEnable()
    {
        _panel = new SkillVisualImporterPanel(Repaint);
    }

    private void OnGUI()
    {
        _panel ??= new SkillVisualImporterPanel(Repaint);
        _panel.OnGUI();
    }
}

internal sealed class SkillVisualImporterPanel
{
    private readonly Action _repaint;
    private int _skillId = 1;
    private bool _buildPrefab = true;
    private bool _createScenePreviewRig = true;
    private string _status = "Ready to import SceneDomain skill visual data.";
    private Vector2 _scroll;
    private ScenePlayableSkillCatalogData _skillCatalog;
    private string[] _classOptions = Array.Empty<string>();
    private int _selectedClassIndex;
    private bool _showSelectedClassSkills;
    private ScenePlayableSkillData[] _visibleSkills = Array.Empty<ScenePlayableSkillData>();
    private string[] _skillOptions = Array.Empty<string>();
    private int _selectedSkillIndex;
    private int _visibleSkillsClassId = int.MinValue;
    private bool _catalogLoadAttempted;

    public SkillVisualImporterPanel(Action repaint)
    {
        _repaint = repaint;
    }

    public void OnGUI()
    {
        EnsureCatalogLoadedOnce();

        EditorGUILayout.LabelField("Import skill/ability visual data", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Client", ConstInfo.L2GameClientPath);
            EditorGUILayout.LabelField("DB", ConstInfo.L2DbRootPath);
            DrawSkillCatalogSelector();
            _buildPrefab = EditorGUILayout.Toggle("Build Preview Prefab", _buildPrefab);
            using (new EditorGUI.DisabledScope(!_buildPrefab))
            {
                _createScenePreviewRig = EditorGUILayout.Toggle("Create Scene Preview Rig", _createScenePreviewRig);
            }

            EditorGUILayout.LabelField("Asset Output", SkillVisualImportBuilder.AssetOutputRoot);
            EditorGUILayout.LabelField("Prefab Output", SkillVisualImportBuilder.PrefabOutputRoot);
            EditorGUILayout.LabelField("Texture Output", SkillVisualImportBuilder.TextureOutputRoot);
        }

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling || _skillId <= 0))
        {
            if (GUILayout.Button("Import Skill Visual", GUILayout.Height(34f)))
            {
                ImportCurrentSkill();
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

    private void EnsureCatalogLoadedOnce()
    {
        if (_catalogLoadAttempted)
        {
            return;
        }

        _catalogLoadAttempted = true;
        LoadSkillCatalog();
    }

    private void DrawSkillCatalogSelector()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.LabelField("Skill Catalog", _skillCatalog == null ? "Not loaded" : $"{_skillCatalog.Classes.Count} classes", GUILayout.MinWidth(180f));
            if (GUILayout.Button("Reload", GUILayout.Width(90f)))
            {
                LoadSkillCatalog();
            }
        }

        if (_skillCatalog == null || _skillCatalog.Classes.Count == 0)
        {
            _skillId = EditorGUILayout.IntField("Skill Id", _skillId);
            return;
        }

        var nextClassIndex = EditorGUILayout.Popup("Playable Class", _selectedClassIndex, _classOptions);
        if (nextClassIndex != _selectedClassIndex)
        {
            _selectedClassIndex = nextClassIndex;
            _showSelectedClassSkills = false;
            _selectedSkillIndex = 0;
        }

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Show Class Skills", GUILayout.Height(24f)))
            {
                _showSelectedClassSkills = true;
                RefreshVisibleSkills(force: true);
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.IntField("Selected Skill Id", _skillId);
            }
        }

        if (!_showSelectedClassSkills)
        {
            return;
        }

        var selectedClass = GetSelectedClass();
        if (selectedClass == null)
        {
            return;
        }

        RefreshVisibleSkills(force: false);

        if (_visibleSkills.Length == 0)
        {
            EditorGUILayout.HelpBox("No skills found for the selected class/search.", MessageType.Info);
            return;
        }

        _selectedSkillIndex = Mathf.Clamp(_selectedSkillIndex, 0, _visibleSkills.Length - 1);
        var nextSkillIndex = EditorGUILayout.Popup("Skill", _selectedSkillIndex, _skillOptions);
        if (nextSkillIndex != _selectedSkillIndex)
        {
            _selectedSkillIndex = nextSkillIndex;
        }

        var selectedSkill = _visibleSkills[_selectedSkillIndex];
        _skillId = selectedSkill.SkillId;
        EditorGUILayout.LabelField("Selected", $"{selectedSkill.SkillName} ({selectedSkill.SkillId})");
    }

    private void LoadSkillCatalog()
    {
        try
        {
            _skillCatalog = new ScenePlayableSkillCatalogBuilder().Build(ConstInfo.L2DbRootPath);
            _classOptions = _skillCatalog.Classes
                .Select(x => $"{x.ClassName} ({x.ClassId})")
                .ToArray();
            _selectedClassIndex = Mathf.Clamp(_selectedClassIndex, 0, Math.Max(0, _classOptions.Length - 1));
            _showSelectedClassSkills = false;
            _visibleSkills = Array.Empty<ScenePlayableSkillData>();
            _skillOptions = Array.Empty<string>();
            _visibleSkillsClassId = int.MinValue;
            _status = $"Skill catalog loaded: {_skillCatalog.Classes.Count} playable classes.";
            _repaint?.Invoke();
        }
        catch (Exception exception)
        {
            _skillCatalog = null;
            _classOptions = Array.Empty<string>();
            _status = $"Skill catalog load failed. Manual skill id input is still available.\n{exception}";
            Debug.LogException(exception);
        }
    }

    private ScenePlayableClassSkillData GetSelectedClass()
    {
        if (_skillCatalog == null || _skillCatalog.Classes.Count == 0)
        {
            return null;
        }

        return _skillCatalog.Classes[Mathf.Clamp(_selectedClassIndex, 0, _skillCatalog.Classes.Count - 1)];
    }

    private void RefreshVisibleSkills(bool force)
    {
        var selectedClass = GetSelectedClass();
        if (selectedClass == null)
        {
            _visibleSkills = Array.Empty<ScenePlayableSkillData>();
            _skillOptions = Array.Empty<string>();
            return;
        }

        if (!force && _visibleSkillsClassId == selectedClass.ClassId)
        {
            return;
        }

        _visibleSkillsClassId = selectedClass.ClassId;

        var skills = (selectedClass.Skills ?? Array.Empty<ScenePlayableSkillData>()).ToArray();

        _visibleSkills = skills.ToArray();
        _skillOptions = _visibleSkills
            .Select(x => x.MinLevel > 0
                ? $"{x.SkillName} ({x.SkillId}) - level {x.MinLevel}"
                : $"{x.SkillName} ({x.SkillId})")
            .ToArray();
        _selectedSkillIndex = Mathf.Clamp(_selectedSkillIndex, 0, Math.Max(0, _visibleSkills.Length - 1));
        if (_visibleSkills.Length > 0)
        {
            _skillId = _visibleSkills[_selectedSkillIndex].SkillId;
        }
    }

    private void ImportCurrentSkill()
    {
        try
        {
            _status = $"Import started for skill id={_skillId}...";
            using var context = new MapImportExecutionContext("Import Skill Visual");
            var result = SkillVisualImportBuilder.ImportBySkillId(
                ConstInfo.L2GameClientPath,
                _skillId,
                AppendStatus,
                _buildPrefab,
                context);

            _status +=
                $"\nDone. Asset: {result.AssetPath}" +
                (string.IsNullOrWhiteSpace(result.PrefabPath) ? string.Empty : $"\nPrefab: {result.PrefabPath}") +
                $"\nNames={result.NameCount}, Levels={result.LevelCount}, Sounds={result.SoundCount}, Stages={result.StageCount}, Layers={result.LayerCount}, MobTriggers={result.MobTriggerCount}, MobVisuals={result.MobVisualCount}, Warnings={result.WarningCount}.";

            if (_buildPrefab && _createScenePreviewRig && result.Asset != null)
            {
                var controller = SkillVisualScenePreviewFactory.Create(result.Asset);
                _status += $"\nScene preview rig created: {controller.transform.root.name}.";
            }
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
}


