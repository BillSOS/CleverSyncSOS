// ---
// speckit:
//   type: implementation
//   source: SpecKit/Plans/001-clever-api-auth/plan.md
//   section: Stage 2 - Database Synchronization
//   constitution: SpecKit/Constitution/constitution.md
//   phase: Database Sync
//   version: 1.0.0
// ---

using CleverSyncSOS.Core.Database.SessionDb.Entities;

namespace CleverSyncSOS.Core.Sync;

/// <summary>
/// Interface for synchronization service that orchestrates data sync from Clever API to databases.
/// Source: SpecKit/Plans/001-clever-api-auth/plan.md (Stage 2)
/// Constitution: Use dependency injection for all services.
/// </summary>
public interface ISyncService
{
    /// <summary>
    /// Synchronizes all active districts and their schools.
    /// Source: DataModel.md - Sync Orchestration Flow (Timer/Manual Trigger entry point)
    /// </summary>
    /// <param name="forceFullSync">If true, forces full sync for all schools regardless of RequiresFullSync flag</param>
    /// <param name="progress">Optional progress reporter for real-time updates</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Summary of sync results across all districts</returns>
    Task<SyncSummary> SyncAllDistrictsAsync(bool forceFullSync = false, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes a specific district and all its active schools.
    /// Source: FR-012 through FR-022 - District-level sync orchestration
    /// </summary>
    /// <param name="districtId">District ID from SessionDb</param>
    /// <param name="forceFullSync">If true, forces full sync for all schools in this district</param>
    /// <param name="progress">Optional progress reporter for real-time updates</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Summary of sync results for the district</returns>
    Task<SyncSummary> SyncDistrictAsync(int districtId, bool forceFullSync = false, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes a specific school (students and teachers).
    /// Source: DataModel.md - Sync Orchestration Flow (Step 4)
    /// FR-023: Full Sync Support for new schools or beginning-of-year
    /// FR-024: Incremental Sync with lastModified filter
    /// FR-025: Hard-delete inactive records during full sync
    /// </summary>
    /// <param name="schoolId">School ID from SessionDb</param>
    /// <param name="forceFullSync">If true, forces full sync regardless of RequiresFullSync flag</param>
    /// <param name="progress">Optional progress reporter for real-time updates</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Summary of sync results for the school</returns>
    Task<SyncResult> SyncSchoolAsync(int schoolId, bool forceFullSync = false, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves sync history for a school.
    /// Source: FR-020 - Sync history tracking for auditing and incremental sync
    /// </summary>
    /// <param name="schoolId">School ID from SessionDb</param>
    /// <param name="entityType">Optional filter by entity type (Student, Teacher)</param>
    /// <param name="limit">Maximum number of records to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Sync history records</returns>
    Task<SyncHistory[]> GetSyncHistoryAsync(
        int schoolId,
        string? entityType = null,
        int limit = 10,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Summary of sync results for multiple schools or districts.
/// </summary>
public class SyncSummary
{
    public int TotalSchools { get; set; }
    public int SuccessfulSchools { get; set; }
    public int FailedSchools { get; set; }
    public int TotalRecordsProcessed { get; set; }
    public int TotalRecordsFailed { get; set; }
    public List<SyncResult> SchoolResults { get; set; } = new();
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
}

/// <summary>
/// Result of syncing a single school.
/// </summary>
public class SyncResult
{
    public int SchoolId { get; set; }
    public string SchoolName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public SyncType SyncType { get; set; }

    // Student sync stats
    public int StudentsProcessed { get; set; } // Total students examined
    public int StudentsUpdated { get; set; } // Students that actually changed
    public int StudentsFailed { get; set; }
    public int StudentsDeleted { get; set; } // For full sync hard-delete

    // Teacher sync stats
    public int TeachersProcessed { get; set; } // Total teachers examined
    public int TeachersUpdated { get; set; } // Teachers that actually changed
    public int TeachersFailed { get; set; }
    public int TeachersDeleted { get; set; } // For full sync hard-delete

    // Course sync stats
    public int CoursesProcessed { get; set; } // Total courses examined
    public int CoursesUpdated { get; set; } // Courses that actually changed
    public int CoursesFailed { get; set; }

    // Section sync stats
    public int SectionsProcessed { get; set; } // Total sections examined
    public int SectionsUpdated { get; set; } // Sections that actually changed
    public int SectionsFailed { get; set; }
    public int SectionsSkippedWorkshopLinked { get; set; } // Sections skipped because linked to workshops

    // Warning stats
    public int WarningsGenerated { get; set; } // Total warnings generated (e.g., workshop-linked sections modified)
    public List<SyncWarningInfo> Warnings { get; set; } = new(); // Details of warnings

    // Events processing summary (for events-based sync)
    public EventsSummary? EventsSummary { get; set; }

    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
}

/// <summary>
/// Real-time progress update for sync operations.
/// Used with IProgress<SyncProgress> for SignalR broadcasting.
/// </summary>
public class SyncProgress
{
    public int PercentComplete { get; set; } // 0-100
    public string CurrentOperation { get; set; } = string.Empty; // e.g., "Fetching students..."
    public int StudentsProcessed { get; set; } // Total students examined
    public int StudentsUpdated { get; set; } // Students that actually changed
    public int StudentsFailed { get; set; }
    public int TeachersProcessed { get; set; } // Total teachers examined
    public int TeachersUpdated { get; set; } // Teachers that actually changed
    public int TeachersFailed { get; set; }
    public int CoursesProcessed { get; set; } // Total courses examined
    public int CoursesUpdated { get; set; } // Courses that actually changed
    public int CoursesFailed { get; set; }
    public int SectionsProcessed { get; set; } // Total sections examined
    public int SectionsUpdated { get; set; } // Sections that actually changed
    public int SectionsFailed { get; set; }
    public int WarningsGenerated { get; set; } // Total warnings generated
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    // Incremental sync breakdown (Created/Updated/Deleted)
    public int StudentsCreated { get; set; }
    public int StudentsDeleted { get; set; }
    public int TeachersCreated { get; set; }
    public int TeachersDeleted { get; set; }
    public int SectionsCreated { get; set; }
    public int SectionsDeleted { get; set; }
    public int EventsProcessed { get; set; }
    public int EventsSkipped { get; set; }
    public bool IsIncrementalSync { get; set; }
}

/// <summary>
/// Information about a sync warning for display purposes.
/// </summary>
public class SyncWarningInfo
{
    public string WarningType { get; set; } = string.Empty;
    public string EntityType { get; set; } = string.Empty;
    public int EntityId { get; set; }
    public string EntityName { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public List<string> AffectedWorkshopNames { get; set; } = new();
}

/// <summary>
/// Summary of events processed during an events-based sync.
/// </summary>
public class EventsSummary
{
    /// <summary>
    /// Total number of events processed.
    /// </summary>
    public int TotalEventsProcessed { get; set; }

    /// <summary>
    /// Number of student created events.
    /// </summary>
    public int StudentCreated { get; set; }

    /// <summary>
    /// Number of student updated events.
    /// </summary>
    public int StudentUpdated { get; set; }

    /// <summary>
    /// Number of student deleted events.
    /// </summary>
    public int StudentDeleted { get; set; }

    /// <summary>
    /// Number of teacher created events.
    /// </summary>
    public int TeacherCreated { get; set; }

    /// <summary>
    /// Number of teacher updated events.
    /// </summary>
    public int TeacherUpdated { get; set; }

    /// <summary>
    /// Number of teacher deleted events.
    /// </summary>
    public int TeacherDeleted { get; set; }

    /// <summary>
    /// Number of section created events.
    /// </summary>
    public int SectionCreated { get; set; }

    /// <summary>
    /// Number of section updated events.
    /// </summary>
    public int SectionUpdated { get; set; }

    /// <summary>
    /// Number of section deleted events.
    /// </summary>
    public int SectionDeleted { get; set; }

    /// <summary>
    /// Number of events skipped (e.g., unsupported types).
    /// </summary>
    public int EventsSkipped { get; set; }

    /// <summary>
    /// Returns a human-readable summary of events processed.
    /// </summary>
    public string ToDisplayString()
    {
        var parts = new List<string>();

        if (StudentCreated > 0) parts.Add($"{StudentCreated} student created");
        if (StudentUpdated > 0) parts.Add($"{StudentUpdated} student updated");
        if (StudentDeleted > 0) parts.Add($"{StudentDeleted} student deleted");
        if (TeacherCreated > 0) parts.Add($"{TeacherCreated} teacher created");
        if (TeacherUpdated > 0) parts.Add($"{TeacherUpdated} teacher updated");
        if (TeacherDeleted > 0) parts.Add($"{TeacherDeleted} teacher deleted");
        if (SectionCreated > 0) parts.Add($"{SectionCreated} section created");
        if (SectionUpdated > 0) parts.Add($"{SectionUpdated} section updated");
        if (SectionDeleted > 0) parts.Add($"{SectionDeleted} section deleted");
        if (EventsSkipped > 0) parts.Add($"{EventsSkipped} skipped");

        if (parts.Count == 0) return "No events processed";
        return $"Processed {TotalEventsProcessed} events: {string.Join(", ", parts)}";
    }
}
