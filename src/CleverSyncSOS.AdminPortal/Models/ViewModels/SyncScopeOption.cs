namespace CleverSyncSOS.AdminPortal.Models.ViewModels;

/// <summary>
/// Represents a sync scope option in the dropdown selector.
/// Based on manual-sync-feature-plan.md
/// </summary>
public class SyncScopeOption
{
    public string Value { get; set; } = string.Empty; // "school:123", "district:45", "all"
    public string DisplayText { get; set; } = string.Empty; // "Lincoln Elementary", "All Schools in Test District"
    public string? Icon { get; set; } // Optional icon class (e.g., "bi-building", "bi-globe")
    public bool IsDisabled { get; set; } // For School Admin's locked dropdown
    public int? EntityId { get; set; } // School ID or District ID
    public SyncScopeType ScopeType { get; set; }
}

/// <summary>
/// Type of sync scope
/// </summary>
public enum SyncScopeType
{
    School,
    District,
    AllDistricts
}
