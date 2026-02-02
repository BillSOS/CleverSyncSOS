namespace CleverSyncSOS.AdminPortal.Models.ViewModels;

/// <summary>
/// View model for displaying sync operation results.
/// Based on manual-sync-feature-plan.md
/// </summary>
public class SyncResultViewModel
{
    public bool Success { get; set; }
    public string Scope { get; set; } = string.Empty; // "school", "district", "all"
    public int? SchoolId { get; set; }
    public string? SchoolName { get; set; }
    public string? DistrictId { get; set; } // Clever District ID (string)
    public SyncMode SyncMode { get; set; }
    public TimeSpan Duration { get; set; }

    // Single school stats (full sync)
    public int StudentsProcessed { get; set; }
    public int StudentsFailed { get; set; }
    public int StudentsDeleted { get; set; } // Full sync only
    public int TeachersProcessed { get; set; }
    public int TeachersFailed { get; set; }
    public int TeachersDeleted { get; set; } // Full sync only

    // Incremental sync stats (Created/Updated/Deleted breakdown)
    public int StudentsCreated { get; set; }
    public int StudentsUpdated { get; set; }
    public int TeachersCreated { get; set; }
    public int TeachersUpdated { get; set; }
    public int SectionsCreated { get; set; }
    public int SectionsUpdated { get; set; }
    public int SectionsDeleted { get; set; }
    public int EventsProcessed { get; set; }
    public int EventsSkipped { get; set; }
    public bool IsIncrementalSync { get; set; }

    // Multi-school stats (district/all scope)
    public int TotalSchools { get; set; }
    public int SuccessfulSchools { get; set; }
    public int FailedSchools { get; set; }
    public List<SchoolSyncResult> SchoolResults { get; set; } = new();

    public string? ErrorMessage { get; set; }
    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Individual school sync result for multi-school operations.
/// </summary>
public class SchoolSyncResult
{
    public int SchoolId { get; set; }
    public string SchoolName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public int StudentsProcessed { get; set; }
    public int TeachersProcessed { get; set; }
    public string? ErrorMessage { get; set; }
}
