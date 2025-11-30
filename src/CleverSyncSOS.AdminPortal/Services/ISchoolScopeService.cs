using CleverSyncSOS.AdminPortal.Models.ViewModels;

namespace CleverSyncSOS.AdminPortal.Services;

/// <summary>
/// Service for retrieving available sync scopes based on user role.
/// Based on manual-sync-feature-plan.md
/// </summary>
public interface ISchoolScopeService
{
    /// <summary>
    /// Gets available sync scopes for the current authenticated user.
    /// </summary>
    /// <param name="userId">Current user ID</param>
    /// <returns>List of available scope options</returns>
    Task<List<SyncScopeOption>> GetAvailableScopesAsync(int userId);

    /// <summary>
    /// Validates that user has permission to sync the requested scope.
    /// </summary>
    /// <param name="userId">Current user ID</param>
    /// <param name="scope">Requested scope (e.g., "school:123")</param>
    /// <returns>True if authorized, false otherwise</returns>
    Task<bool> ValidateScopeAccessAsync(int userId, string scope);
}
