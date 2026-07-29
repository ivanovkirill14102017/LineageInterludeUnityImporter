using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public sealed class MapImporterWindow : EditorWindow
{
    private MapImporterPanel _panel;

    [MenuItem("L2/Import Terrain")]
    private static void OpenWindow()
    {
        var window = GetWindow<MapImporterWindow>("L2 Map Import");
        window.minSize = new Vector2(560f, 320f);
        window.Show();
    }

    private void OnEnable()
    {
        _panel = new MapImporterPanel(Repaint);
    }

    private void OnDisable()
    {
        _panel?.Dispose();
        _panel = null;
    }

    private void OnGUI()
    {
        _panel ??= new MapImporterPanel(Repaint);
        _panel.OnGUI();
    }
}

internal sealed class MapImporterPanel : IDisposable
{
    private const string DefaultMapRelativePath = @"Maps\20_20.unr";

    private readonly Action _repaint;
    private readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();
    private string _mapRelativePath = DefaultMapRelativePath;
    private string _dbRootPath = ConstInfo.L2DbRootPath;
    private bool _isImportRunning;
    private string _status = "Ready to import.";
    private Vector2 _scroll;

    public MapImporterPanel(Action repaint)
    {
        _repaint = repaint;
        EditorApplication.update += OnEditorUpdate;
    }

    public void Dispose()
    {
        EditorApplication.update -= OnEditorUpdate;
    }

    public void OnGUI()
    {
        EditorGUILayout.LabelField("Import map content from the Lineage II client", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Client", ConstInfo.L2GameClientPath);
            _dbRootPath = EditorGUILayout.TextField("DB Root", _dbRootPath);
            _mapRelativePath = EditorGUILayout.TextField("Map", _mapRelativePath);
            EditorGUILayout.LabelField("Output Root", MapImportPaths.OutputRoot);
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

                if (GUILayout.Button("Import Creatures Only", GUILayout.Height(24f)))
                {
                    QueueImport(ImportCreaturesAsync);
                }
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

    private void OnEditorUpdate()
    {
        var hasMessages = false;
        while (_logQueue.TryDequeue(out var message))
        {
            _status = $"{_status}\n{message}";
            hasMessages = true;
        }

        if (hasMessages)
        {
            _repaint?.Invoke();
        }
    }

    private void QueueImport(Func<MapImportExecutionContext, Task> importAction)
    {
        if (_isImportRunning)
        {
            return;
        }

        _isImportRunning = true;
        EditorApplication.delayCall += RunQueuedImport;

        async void RunQueuedImport()
        {
            MapImportExecutionContext context = null;
            try
            {
                context = new MapImportExecutionContext("L2 Map Import");
                await importAction(context);
            }
            catch (OperationCanceledException)
            {
                _status = $"{_status}\nImport cancelled by user.";
            }
            finally
            {
                context?.Dispose();
                _isImportRunning = false;
                _repaint?.Invoke();
            }
        }
    }

    private async Task ImportAllAsync(MapImportExecutionContext context)
    {
        try
        {
            _status = "Ready to import all map content.";
            ConstInfo.L2DbRootPath = _dbRootPath;
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath);
            await MapImportOrchestrator.ImportAll(request, AppendStatus, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _status = exception.ToString();
            Debug.LogException(exception);
        }
    }

    private async Task ImportTerrainAsync(MapImportExecutionContext context)
    {
        try
        {
            _status = "Ready to import terrain.";
            ConstInfo.L2DbRootPath = _dbRootPath;
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath);
            await MapImportOrchestrator.ImportTerrain(request, AppendStatus, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _status = exception.ToString();
            Debug.LogException(exception);
        }
    }

    private async Task ImportMeshesAsync(MapImportExecutionContext context)
    {
        try
        {
            _status = "Ready to import meshes.";
            ConstInfo.L2DbRootPath = _dbRootPath;
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath);
            await MapImportOrchestrator.ImportMeshes(request, AppendStatus, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _status = exception.ToString();
            Debug.LogException(exception);
        }
    }

    private async Task ImportBspAsync(MapImportExecutionContext context)
    {
        try
        {
            _status = "Ready to import room-grouped BSP.";
            ConstInfo.L2DbRootPath = _dbRootPath;
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath);
            await MapImportOrchestrator.ImportBsp(request, AppendStatus, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _status = exception.ToString();
            Debug.LogException(exception);
        }
    }

    private async Task ImportLightsAsync(MapImportExecutionContext context)
    {
        try
        {
            _status = "Ready to import lights.";
            ConstInfo.L2DbRootPath = _dbRootPath;
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath);
            await MapImportOrchestrator.ImportLights(request, AppendStatus, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _status = exception.ToString();
            Debug.LogException(exception);
        }
    }

    private async Task ImportVolumesAsync(MapImportExecutionContext context)
    {
        try
        {
            _status = "Ready to import volumes.";
            ConstInfo.L2DbRootPath = _dbRootPath;
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath);
            await MapImportOrchestrator.ImportVolumes(request, AppendStatus, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _status = exception.ToString();
            Debug.LogException(exception);
        }
    }

    private async Task ImportParticlesAsync(MapImportExecutionContext context)
    {
        try
        {
            _status = "Ready to import particles.";
            ConstInfo.L2DbRootPath = _dbRootPath;
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath);
            await MapImportOrchestrator.ImportParticles(request, AppendStatus, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _status = exception.ToString();
            Debug.LogException(exception);
        }
    }

    private async Task ImportCreaturesAsync(MapImportExecutionContext context)
    {
        try
        {
            _status = "Ready to import creatures.";
            ConstInfo.L2DbRootPath = _dbRootPath;
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath);
            await MapImportOrchestrator.ImportCreatures(request, AppendStatus, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
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
