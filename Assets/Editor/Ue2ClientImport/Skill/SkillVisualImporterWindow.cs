using System;
using UnityEditor;
using UnityEngine;

public sealed class SkillVisualImporterWindow : EditorWindow
{
    private SkillVisualImporterPanel _panel;

    [MenuItem("L2/Import Skill Visual")]
    private static void OpenWindow()
    {
        var window = GetWindow<SkillVisualImporterWindow>("Skill Visual Import");
        window.minSize = new Vector2(700f, 420f);
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
    private bool _reuseExistingAssets = true;
    private string _status = "Ready to import SceneDomain skill visual data.";
    private Vector2 _scroll;

    public SkillVisualImporterPanel(Action repaint)
    {
        _repaint = repaint;
    }

    public void OnGUI()
    {
        EditorGUILayout.LabelField("Import skill/ability visual data", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Client", ConstInfo.L2GameClientPath);
            _skillId = EditorGUILayout.IntField("Skill Id", _skillId);
            _buildPrefab = EditorGUILayout.Toggle("Build Preview Prefab", _buildPrefab);
            _reuseExistingAssets = EditorGUILayout.Toggle("Reuse Existing Assets", _reuseExistingAssets);
            EditorGUILayout.LabelField("Asset Output", SkillVisualImportBuilder.AssetOutputRoot);
            EditorGUILayout.LabelField("Prefab Output", SkillVisualImportBuilder.PrefabOutputRoot);
            EditorGUILayout.LabelField("Texture Output", SkillVisualImportBuilder.TextureOutputRoot);
        }

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
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
                _reuseExistingAssets,
                context);

            _status +=
                $"\nDone. Asset: {result.AssetPath}" +
                (string.IsNullOrWhiteSpace(result.PrefabPath) ? string.Empty : $"\nPrefab: {result.PrefabPath}") +
                $"\nNames={result.NameCount}, Levels={result.LevelCount}, Sounds={result.SoundCount}, Stages={result.StageCount}, Layers={result.LayerCount}, MobTriggers={result.MobTriggerCount}, MobVisuals={result.MobVisualCount}, Warnings={result.WarningCount}.";
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
