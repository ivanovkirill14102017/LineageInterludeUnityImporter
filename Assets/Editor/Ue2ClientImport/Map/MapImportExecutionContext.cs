using System;
using UnityEditor;

internal sealed class MapImportExecutionContext : IDisposable
{
    private readonly string _title;
    private bool _isDisposed;
    private bool _isCancelled;

    public MapImportExecutionContext(string title)
    {
        _title = string.IsNullOrWhiteSpace(title) ? "Map Import" : title;
    }

    public bool IsCancellationRequested => _isCancelled;

    public void Report(string phase, string detail, float progress)
    {
        ThrowIfCancellationRequested();

        var message = string.IsNullOrWhiteSpace(detail)
            ? (phase ?? string.Empty)
            : $"{phase}\n{detail}";
        var normalizedProgress = progress < 0f ? 0f : (progress > 1f ? 1f : progress);
        if (EditorUtility.DisplayCancelableProgressBar(_title, message, normalizedProgress))
        {
            _isCancelled = true;
            throw new OperationCanceledException($"Operation '{_title}' was cancelled by the user.");
        }
    }

    public void ThrowIfCancellationRequested()
    {
        if (_isCancelled)
        {
            throw new OperationCanceledException($"Operation '{_title}' was cancelled by the user.");
        }
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        EditorUtility.ClearProgressBar();
    }
}
