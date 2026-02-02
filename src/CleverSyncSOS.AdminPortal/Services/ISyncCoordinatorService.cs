using CleverSyncSOS.AdminPortal.Models.ViewModels;

namespace CleverSyncSOS.AdminPortal.Services;

/// <summary>
/// Service for coordinating manual sync operations with SignalR progress updates.
/// Based on manual-sync-feature-plan.md
/// </summary>
public interface ISyncCoordinatorService
{
    /// <summary>
    /// Checks if a sync is currently in progress for the given scope.
    /// </summary>
    Task<bool> IsSyncInProgressAsync(string scope);

    /// <summary>
    /// Starts a manual sync operation with real-time progress updates.
    /// </summary>
    /// <param name="userId">User initiating sync</param>
    /// <param name="scope">Sync scope (school:123, district:45, all)</param>
    /// <param name="syncMode">Incremental or Full</param>
    /// <param name="connectionId">SignalR connection ID for progress updates</param>
    /// <returns>Sync result</returns>
    Task<SyncResultViewModel> StartSyncAsync(
        int userId,
        string scope,
        SyncMode syncMode,
        string connectionId);

    /// <summary>
    /// Gets the current progress of a running sync operation.
    /// </summary>
    Task<SyncProgressUpdate?> GetCurrentProgressAsync(string scope);

    /// <summary>
    /// Gets all currently active sync operations.
    /// </summary>
    Task<IReadOnlyDictionary<string, SyncProgressUpdate>> GetAllActiveSyncsAsync();
}
