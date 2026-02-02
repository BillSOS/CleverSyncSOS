namespace CleverSyncSOS.AdminPortal.Models.ViewModels;

/// <summary>
/// View model for the Manual Sync page state and data binding.
/// Based on manual-sync-feature-plan.md
/// </summary>
public class ManualSyncViewModel
{
    // User context (injected from authentication)
    public string UserRole { get; set; } = string.Empty; // SchoolAdmin, DistrictAdmin, SuperAdmin
    public int? UserSchoolId { get; set; }
    public string? UserDistrictId { get; set; } // Clever District ID (string)

    // Scope selection
    public List<SyncScopeOption> AvailableScopes { get; set; } = new();
    public string SelectedScope { get; set; } = string.Empty; // Format: "school:{id}", "district:{id}", "all"

    // Sync mode
    public SyncMode SyncMode { get; set; } = SyncMode.Incremental;

    // Progress tracking
    public bool IsSyncInProgress { get; set; }
    public SyncProgressUpdate? CurrentProgress { get; set; }

    // Results
    public SyncResultViewModel? LastSyncResult { get; set; }

    // UI state
    public bool ShowFullSyncConfirmation { get; set; }
    public bool IsStartSyncButtonEnabled => !IsSyncInProgress && !string.IsNullOrEmpty(SelectedScope);
}

/// <summary>
/// Sync mode enumeration
/// </summary>
public enum SyncMode
{
    Incremental,
    Full
}
