using System;
using System.IO;
using UnityEditor;
using UnityEngine;

public sealed class AngelNpcImporterWindow : EditorWindow
{
    private static readonly string RepoRoot = @"C:\Users\User\source\l2InterludeExporter";
    private static readonly string DefaultPackageRelativePath = @"animations\LineageMonsters.ukx";
    private static readonly string DefaultAngelMeshName = "angel_m00";
    private static readonly string DefaultBoxMeshName = "alchemic_box_m00";
    private static readonly string DefaultAngelPskPath = Path.Combine(
        RepoRoot,
        ".tmp_umodel_angel_actorx",
        "LineageMonsters",
        "SkeletalMesh",
        "angel_m00.psk");
    private static readonly string DefaultAngelPsaPath = Path.Combine(
        RepoRoot,
        ".tmp_umodel_angel_actorx_anim",
        "LineageMonsters",
        "MeshAnimation",
        "angel_anim.psa");
    private static readonly string DefaultBoxPskPath = Path.Combine(
        RepoRoot,
        ".tmp_umodel_alchemic_box",
        "LineageMonsters",
        "SkeletalMesh",
        "alchemic_box_m00.psk");
    private static readonly string DefaultBoxPsaPath = Path.Combine(
        RepoRoot,
        ".tmp_umodel_alchemic_box",
        "LineageMonsters",
        "MeshAnimation",
        "mimic_anim.psa");

    private string _packageRelativePath = DefaultPackageRelativePath;
    private string _meshName = DefaultAngelMeshName;
    private string _actorXPskPath = DefaultAngelPskPath;
    private string _actorXPsaPath = DefaultAngelPsaPath;
    private string _status = "Ready to import an ActorX skeletal prefab.";
    private Vector2 _scroll;

    [MenuItem("L2/Import Skeletal Diagnostic NPC")]
    private static void OpenWindow()
    {
        var window = GetWindow<AngelNpcImporterWindow>("Skeletal NPC Import");
        window.minSize = new Vector2(620f, 320f);
        window.Show();
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Import an ActorX skeletal prefab", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Client", ConstInfo.L2GameClientPath);
            _packageRelativePath = EditorGUILayout.TextField("Package", _packageRelativePath);
            _meshName = EditorGUILayout.TextField("Mesh", _meshName);
            _actorXPskPath = EditorGUILayout.TextField("ActorX PSK (legacy)", _actorXPskPath);
            _actorXPsaPath = EditorGUILayout.TextField("ActorX PSA (legacy)", _actorXPsaPath);
            EditorGUILayout.LabelField("Output", AngelNpcImportBuilder.OutputRoot);
            EditorGUILayout.LabelField("Note", "This mode now imports through shared SceneDomain skeletal logic, the same path used by the WPF diagnostic viewer. PSK/PSA paths are legacy fields and are no longer used by the importer.");
        }

        EditorGUILayout.Space();

        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("Preset: Angel"))
            {
                ApplyPreset(DefaultPackageRelativePath, DefaultAngelMeshName, DefaultAngelPskPath, DefaultAngelPsaPath);
            }

            if (GUILayout.Button("Preset: Alchemic Box"))
            {
                ApplyPreset(DefaultPackageRelativePath, DefaultBoxMeshName, DefaultBoxPskPath, DefaultBoxPsaPath);
            }
        }

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling))
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Import Current Mesh", GUILayout.Height(34f)))
                {
                    ImportCurrentMesh();
                }

                if (GUILayout.Button("Import Angel", GUILayout.Height(34f)))
                {
                    ApplyPreset(DefaultPackageRelativePath, DefaultAngelMeshName, DefaultAngelPskPath, DefaultAngelPsaPath);
                    ImportCurrentMesh();
                }

                if (GUILayout.Button("Import Box", GUILayout.Height(34f)))
                {
                    ApplyPreset(DefaultPackageRelativePath, DefaultBoxMeshName, DefaultBoxPskPath, DefaultBoxPsaPath);
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

    private void ApplyPreset(string packageRelativePath, string meshName, string actorXPskPath, string actorXPsaPath)
    {
        _packageRelativePath = packageRelativePath;
        _meshName = meshName;
        _actorXPskPath = actorXPskPath ?? string.Empty;
        _actorXPsaPath = actorXPsaPath ?? string.Empty;
        _status = $"Preset selected: {meshName} from {actorXPskPath}";
    }

    private void ImportCurrentMesh()
    {
        try
        {
            _status = $"Import started for '{_meshName}'...";
            var options = new AngelNpcImportBuilder.ImportOptions(_actorXPskPath, _actorXPsaPath);
            var result = AngelNpcImportBuilder.Import(ConstInfo.L2GameClientPath, _packageRelativePath, _meshName, options, AppendStatus);
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
