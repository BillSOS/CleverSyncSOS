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
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Summary of sync results across all districts</returns>
    Task<SyncSummary> SyncAllDistrictsAsync(bool forceFullSync = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes a specific district and all its active schools.
    /// Source: FR-012 through FR-022 - District-level sync orchestration
    /// </summary>
    /// <param name="districtId">District ID from SessionDb</param>
    /// <param name="forceFullSync">If true, forces full sync for all schools in this district</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Summary of sync results for the district</returns>
    Task<SyncSummary> SyncDistrictAsync(int districtId, bool forceFullSync = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes a specific school (students and teachers).
    /// Source: DataModel.md - Sync Orchestration Flow (Step 4)
    /// FR-023: Full Sync Support for new schools or beginning-of-year
    /// FR-024: Incremental Sync with lastModified filter
    /// FR-025: Hard-delete inactive records during full sync
    /// </summary>
    /// <param name="schoolId">School ID from SessionDb</param>
    /// <param name="forceFullSync">If true, forces full sync regardless of RequiresFullSync flag</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Summary of sync results for the school</returns>
    Task<SyncResult> SyncSchoolAsync(int schoolId, bool forceFullSync = false, CancellationToken cancellationToken = default);

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
    public int StudentsProcessed { get; set; }
    public int StudentsFailed { get; set; }
    public int StudentsDeleted { get; set; } // For full sync hard-delete

    // Teacher sync stats
    public int TeachersProcessed { get; set; }
    public int TeachersFailed { get; set; }
    public int TeachersDeleted { get; set; } // For full sync hard-delete

    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public TimeSpan Duration => EndTime - StartTime;
}
