using System.Collections.Concurrent;
using CleverSyncSOS.AdminPortal.Models.ViewModels;

namespace CleverSyncSOS.AdminPortal.Services;

/// <summary>
/// Singleton service for tracking active sync operations across the application.
/// This allows any component to check if syncs are in progress.
/// </summary>
public class ActiveSyncTracker
{
    private readonly ConcurrentDictionary<string, SyncProgressUpdate> _activeSyncs = new();

    /// <summary>
    /// Event fired when active syncs change (sync started or completed).
    /// </summary>
    public event Action? OnActiveSyncsChanged;

    public bool TryAdd(string scope, SyncProgressUpdate progress)
    {
        var result = _activeSyncs.TryAdd(scope, progress);
        if (result)
        {
            OnActiveSyncsChanged?.Invoke();
        }
        return result;
    }

    public bool TryRemove(string scope)
    {
        var result = _activeSyncs.TryRemove(scope, out _);
        if (result)
        {
            OnActiveSyncsChanged?.Invoke();
        }
        return result;
    }

    public bool ContainsKey(string scope) => _activeSyncs.ContainsKey(scope);

    public bool TryGetValue(string scope, out SyncProgressUpdate? progress)
    {
        var result = _activeSyncs.TryGetValue(scope, out var p);
        progress = p;
        return result;
    }

    public void UpdateProgress(string scope, SyncProgressUpdate progress)
    {
        _activeSyncs[scope] = progress;
        OnActiveSyncsChanged?.Invoke();
    }

    public IReadOnlyDictionary<string, SyncProgressUpdate> GetAllActiveSyncs()
    {
        return new Dictionary<string, SyncProgressUpdate>(_activeSyncs);
    }

    public int ActiveSyncCount => _activeSyncs.Count;

    public bool HasActiveSyncs => !_activeSyncs.IsEmpty;
}
