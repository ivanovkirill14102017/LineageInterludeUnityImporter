using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

public sealed class MapImporterWindow : EditorWindow
{
    private MapImporterPanel _panel;
    private ImportDashboardStatus _status;

    [MenuItem("L2/Import Terrain")]
    private static void OpenWindow()
    {
        var window = GetWindow<MapImporterWindow>("L2 Map Import");
        window.minSize = new Vector2(560f, 320f);
        window.Show();
    }

    private void OnEnable()
    {
        _status = new ImportDashboardStatus(Repaint);
        _panel = new MapImporterPanel(Repaint, _status);
    }

    private void OnDisable()
    {
        _panel?.Dispose();
        _panel = null;
    }

    private void OnGUI()
    {
        _status ??= new ImportDashboardStatus(Repaint);
        _panel ??= new MapImporterPanel(Repaint, _status);
        ImportDashboardFields.DrawPaths();
        _panel.OnGUI();
        _status.Draw();
    }
}

internal sealed class MapImporterPanel : IDisposable
{
    private const string DefaultMapRelativePath = @"Maps\20_20.unr";

    private readonly Action _repaint;
    private readonly ImportDashboardStatus _status;
    private readonly ConcurrentQueue<string> _logQueue = new ConcurrentQueue<string>();
    private string _mapRelativePath = DefaultMapRelativePath;
    private bool _isImportRunning;

    public MapImporterPanel(Action repaint, ImportDashboardStatus status)
    {
        _repaint = repaint;
        _status = status;
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
            _mapRelativePath = EditorGUILayout.TextField("Map", _mapRelativePath);
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

    }

    private void OnEditorUpdate()
    {
        var hasMessages = false;
        while (_logQueue.TryDequeue(out var message))
        {
            _status.Append(message);
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
                _status.Append("Import cancelled by user.");
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
            _status.Set("Ready to import all map content.");
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath);
            await MapImportOrchestrator.ImportAll(request, AppendStatus, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _status.Set(exception.ToString());
            Debug.LogException(exception);
        }
    }

    private async Task ImportTerrainAsync(MapImportExecutionContext context)
    {
        try
        {
            _status.Set("Ready to import terrain.");
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath);
            await MapImportOrchestrator.ImportTerrain(request, AppendStatus, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _status.Set(exception.ToString());
            Debug.LogException(exception);
        }
    }

    private async Task ImportMeshesAsync(MapImportExecutionContext context)
    {
        try
        {
            _status.Set("Ready to import meshes.");
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath);
            await MapImportOrchestrator.ImportMeshes(request, AppendStatus, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _status.Set(exception.ToString());
            Debug.LogException(exception);
        }
    }

    private async Task ImportBspAsync(MapImportExecutionContext context)
    {
        try
        {
            _status.Set("Ready to import room-grouped BSP.");
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath);
            await MapImportOrchestrator.ImportBsp(request, AppendStatus, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _status.Set(exception.ToString());
            Debug.LogException(exception);
        }
    }

    private async Task ImportLightsAsync(MapImportExecutionContext context)
    {
        try
        {
            _status.Set("Ready to import lights.");
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath);
            await MapImportOrchestrator.ImportLights(request, AppendStatus, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _status.Set(exception.ToString());
            Debug.LogException(exception);
        }
    }

    private async Task ImportVolumesAsync(MapImportExecutionContext context)
    {
        try
        {
            _status.Set("Ready to import volumes.");
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath);
            await MapImportOrchestrator.ImportVolumes(request, AppendStatus, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _status.Set(exception.ToString());
            Debug.LogException(exception);
        }
    }

    private async Task ImportParticlesAsync(MapImportExecutionContext context)
    {
        try
        {
            _status.Set("Ready to import particles.");
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath);
            await MapImportOrchestrator.ImportParticles(request, AppendStatus, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _status.Set(exception.ToString());
            Debug.LogException(exception);
        }
    }

    private async Task ImportCreaturesAsync(MapImportExecutionContext context)
    {
        try
        {
            _status.Set("Ready to import creatures.");
            var request = MapImportRequest.FromMapRelativePath(_mapRelativePath);
            await MapImportOrchestrator.ImportCreatures(request, AppendStatus, context);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _status.Set(exception.ToString());
            Debug.LogException(exception);
        }
    }

    private void AppendStatus(string message)
    {
        _logQueue.Enqueue(message);
    }
}
