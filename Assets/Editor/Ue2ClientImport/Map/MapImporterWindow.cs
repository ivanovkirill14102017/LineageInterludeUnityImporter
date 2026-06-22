using UnityEditor;
using UnityEngine;
using System.Collections.Concurrent;
using System;
using System.Threading.Tasks;

public sealed class MapImporterWindow : EditorWindow
{
    private const string DefaultMapRelativePath = @"Maps\20_20.unr";

    private string _mapRelativePath = DefaultMapRelativePath;
    private bool _importTrees = true;
    private bool _importNonTrees = true;
    private bool _reuseExistingMaterialTextureAssets = true;
    private bool _isImportRunning;
    private string _status = "Ready to import.";
    private Vector2 _scroll;
    private ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();

    [MenuItem("L2/Import Terrain")]
    private static void OpenWindow()
    {
        var window = GetWindow<MapImporterWindow>("L2 Map Import");
        window.minSize = new Vector2(560f, 320f);
        window.Show();
    }

    private void OnEnable()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    private void OnDisable()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    private void OnEditorUpdate()
    {
        bool hasMessages = false;
        while (_logQueue.TryDequeue(out var message))
        {
            _status = $"{_status}\n{message}";
            hasMessages = true;
        }

        if (hasMessages)
        {
            Repaint();
        }
    }

    private void OnGUI()
    {
        EditorGUILayout.LabelField("Import map content from the Lineage II client", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Client", ConstInfo.L2GameClientPath);
            _mapRelativePath = EditorGUILayout.TextField("Map", _mapRelativePath);
            _importTrees = EditorGUILayout.Toggle("Import Trees", _importTrees);
            _importNonTrees = EditorGUILayout.Toggle("Import Non-Trees", _importNonTrees);
            _reuseExistingMaterialTextureAssets = EditorGUILayout.Toggle("Reuse Existing Materials/Textures", _reuseExistingMaterialTextureAssets);
            EditorGUILayout.LabelField("Output Root", MapImportPaths.OutputRoot);
            EditorGUILayout.LabelField("Behavior", "Map folders keep terrain and BSP meshes. Shared meshes, materials and textures are reused from client-like package folders.");
        }

        EditorGUILayout.Space();

        using (new EditorGUI.DisabledScope(EditorApplication.isCompiling || _isImportRunning))
        {
            if (GUILayout.Button("Import All", GUILayout.Height(34f)))
            {
                QueueImport(ImportAllAsync);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Import Terrain Only", GUILayout.Height(24f)))
                {
                    QueueImport(ImportTerrainAsync);
                }
                if (GUILayout.Button("Import Meshes Only", GUILayout.Height(24f)))
                {
                    QueueImport(ImportMeshesAsync);
                }
                if (GUILayout.Button("Import BSP Only", GUILayout.Height(24f)))
                {
                    QueueImport(ImportBspAsync);
                }
                if (GUILayout.Button("Import Lights Only", GUILayout.Height(24f)))
                {
                    QueueImport(ImportLightsAsync);
                }
                if (GUILayout.Button("Import Volumes Only", GUILayout.Height(24f)))
                {
                    QueueImport(ImportVolumesAsync);
                }
                if (GUILayout.Button("Import Particles Only", GUILayout.Height(24f)))
                {
                    QueueImport(ImportParticlesAsync);
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

    private void QueueImport(Func<Task> importAction)
    {
        if (_isImportRunning)
        {
            return;
        }

        _isImportRunning = true;
        EditorApplication.delayCall += RunQueuedImport;

        async void RunQueuedImport()
        {
            try
            {
                await importAction();
            }
            finally
            {
                _isImportRunning = false;
                Repaint();
            }
        }
    }

    private async Task ImportAllAsync()
    {
        try
        {
            _status = "Ready to import all map content.";
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath, _importTrees, _importNonTrees, _reuseExistingMaterialTextureAssets);
            await MapImportOrchestrator.ImportAll(request, AppendStatus);
        }
        catch (System.Exception exception)
        {
            _status = exception.ToString();
            Debug.LogException(exception);
        }
    }

    private async Task ImportTerrainAsync()
    {
        try
        {
            _status = "Ready to import terrain.";
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath, _importTrees, _importNonTrees, _reuseExistingMaterialTextureAssets);
            await MapImportOrchestrator.ImportTerrain(request, AppendStatus);
        }
        catch (System.Exception exception)
        {
            _status = exception.ToString();
            Debug.LogException(exception);
        }
    }

    private async Task ImportMeshesAsync()
    {
        try
        {
            _status = "Ready to import meshes.";
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath, _importTrees, _importNonTrees, _reuseExistingMaterialTextureAssets);
            await MapImportOrchestrator.ImportMeshes(request, AppendStatus);
        }
        catch (System.Exception exception)
        {
            _status = exception.ToString();
            Debug.LogException(exception);
        }
    }

    private async Task ImportBspAsync()
    {
        try
        {
            _status = "Ready to import room-grouped BSP.";
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath, _importTrees, _importNonTrees, _reuseExistingMaterialTextureAssets);
            await MapImportOrchestrator.ImportBsp(request, AppendStatus);
        }
        catch (System.Exception exception)
        {
            _status = exception.ToString();
            Debug.LogException(exception);
        }
    }

    private async Task ImportLightsAsync()
    {
        try
        {
            _status = "Ready to import lights.";
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath, _importTrees, _importNonTrees, true);
            await MapImportOrchestrator.ImportLights(request, AppendStatus);
        }
        catch (System.Exception exception)
        {
            _status = exception.ToString();
            Debug.LogException(exception);
        }
    }

    private async Task ImportVolumesAsync()
    {
        try
        {
            _status = "Ready to import volumes.";
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath, _importTrees, _importNonTrees, true);
            await MapImportOrchestrator.ImportVolumes(request, AppendStatus);
        }
        catch (System.Exception exception)
        {
            _status = exception.ToString();
            Debug.LogException(exception);
        }
    }

    private async Task ImportParticlesAsync()
    {
        try
        {
            _status = "Ready to import particles.";
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath, _importTrees, _importNonTrees, true);
            await MapImportOrchestrator.ImportParticles(request, AppendStatus);
        }
        catch (System.Exception exception)
        {
            _status = exception.ToString();
            Debug.LogException(exception);
        }
    }

    private void AppendStatus(string message)
    {
        _logQueue.Enqueue(message);
    }
}
