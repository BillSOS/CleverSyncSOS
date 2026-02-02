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
/// Orchestrates data synchronization between Clever's Student Information System (SIS) API and local school databases.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> This service is the primary entry point for all sync operations in CleverSyncSOS.
/// It manages the flow of student, teacher, and section data from Clever's cloud API to individual school databases.</para>
/// 
/// <para><b>Architecture:</b> The service implements a dual-database pattern:</para>
/// <list type="bullet">
///   <item><description><b>SessionDb</b> - Central orchestration database storing districts, schools, and sync history</description></item>
///   <item><description><b>Per-School Databases</b> - Isolated databases for each school containing actual student/teacher data</description></item>
/// </list>
/// 
/// <para><b>Sync Modes:</b></para>
/// <list type="bullet">
///   <item><description><b>Full Sync</b> - Fetches all records from Clever, soft-deletes records no longer present (beginning-of-year refresh)</description></item>
///   <item><description><b>Incremental Sync</b> - Uses Clever Events API to process only changes since last sync (daily operations)</description></item>
/// </list>
/// 
/// <para><b>Key Features:</b></para>
/// <list type="bullet">
///   <item><description>Parallel school processing (max 5 concurrent) for performance</description></item>
///   <item><description>Real-time progress reporting via IProgress&lt;SyncProgress&gt;</description></item>
///   <item><description>Workshop-linked section protection with warnings</description></item>
///   <item><description>Automatic session cleanup after sync completion</description></item>
/// </list>
/// 
/// <para><b>Related Specifications:</b></para>
/// <list type="bullet">
///   <item><description>FR-005: Clever Data Synchronization</description></item>
///   <item><description>FR-006: Full Sync Mode</description></item>
///   <item><description>FR-007: Incremental Sync Mode</description></item>
///   <item><description>FR-009: Sync Orchestration</description></item>
/// </list>
/// </remarks>
/// <example>
/// <code>
/// // Inject ISyncService via dependency injection
/// public class SyncController
/// {
///     private readonly ISyncService _syncService;
///     
///     // Sync all districts with progress reporting
///     var progress = new Progress&lt;SyncProgress&gt;(p => 
///         Console.WriteLine($"{p.PercentComplete}%: {p.CurrentOperation}"));
///     var summary = await _syncService.SyncAllDistrictsAsync(forceFullSync: false, progress);
///     
///     // Sync a specific school with forced full refresh
///     var result = await _syncService.SyncSchoolAsync(schoolId: 3, forceFullSync: true);
/// }
/// </code>
/// </example>
/// <seealso cref="SyncService"/>
/// <seealso cref="SyncResult"/>
/// <seealso cref="SyncSummary"/>
public interface ISyncService
{
    /// <summary>
    /// Synchronizes all active districts and their schools from Clever API.
    /// </summary>
    /// <remarks>
    /// <para><b>Workflow:</b></para>
    /// <list type="number">
    ///   <item><description>Queries all districts from SessionDb (includes navigation to Schools)</description></item>
    ///   <item><description>Iterates through each district, calling <see cref="SyncDistrictAsync"/> for each</description></item>
    ///   <item><description>Aggregates results into a single <see cref="SyncSummary"/></description></item>
    ///   <item><description>Reports progress throughout the operation</description></item>
    /// </list>
    /// 
    /// <para><b>Error Handling:</b> Individual district failures are logged but do not stop processing 
    /// of other districts. The summary will show both successful and failed schools.</para>
    /// 
    /// <para><b>Performance:</b> Schools within each district are processed in parallel (max 5 concurrent)
    /// to optimize throughput while respecting Clever API rate limits.</para>
    /// 
    /// <para><b>User Manual Reference:</b> This method is typically triggered by:
    /// <list type="bullet">
    ///   <item><description>Azure Functions timer trigger (daily at 2 AM UTC)</description></item>
    ///   <item><description>Admin Portal "Sync All" button</description></item>
    ///   <item><description>Console application for testing</description></item>
    /// </list>
    /// </para>
    /// </remarks>
    /// <param name="forceFullSync">
    /// When <c>true</c>, forces a full sync for all schools regardless of their <c>RequiresFullSync</c> flag.
    /// Use this for beginning-of-year data refresh or to resolve data integrity issues.
    /// Default is <c>false</c> (incremental sync when possible).
    /// </param>
    /// <param name="progress">
    /// Optional progress reporter for real-time UI updates. Reports <see cref="SyncProgress"/> objects
    /// containing percent complete, current operation description, and record counts.
    /// Pass <c>null</c> if progress reporting is not needed.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token to gracefully stop the operation. The service will complete the current
    /// school's sync before stopping.
    /// </param>
    /// <returns>
    /// A <see cref="SyncSummary"/> containing aggregated results across all districts, including:
    /// total schools processed, success/failure counts, total records, and individual school results.
    /// </returns>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested.</exception>
    Task<SyncSummary> SyncAllDistrictsAsync(bool forceFullSync = false, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes a specific district and all its active schools.
    /// </summary>
    /// <remarks>
    /// <para><b>Workflow:</b></para>
    /// <list type="number">
    ///   <item><description>Loads the district from SessionDb with its associated schools</description></item>
    ///   <item><description>Filters to only active schools (<c>IsActive = true</c>)</description></item>
    ///   <item><description>Processes schools in parallel (max 5 concurrent via SemaphoreSlim)</description></item>
    ///   <item><description>Aggregates results into a <see cref="SyncSummary"/></description></item>
    /// </list>
    /// 
    /// <para><b>Use Cases:</b></para>
    /// <list type="bullet">
    ///   <item><description>Testing sync for a specific district without affecting others</description></item>
    ///   <item><description>Recovering from a district-specific sync failure</description></item>
    ///   <item><description>On-demand refresh after Clever data changes</description></item>
    /// </list>
    /// </remarks>
    /// <param name="districtId">
    /// The <c>DistrictId</c> (primary key) from the SessionDb Districts table.
    /// This is the internal database ID, not the Clever district ID.
    /// </param>
    /// <param name="forceFullSync">
    /// When <c>true</c>, forces a full sync for all schools in this district.
    /// Default is <c>false</c>.
    /// </param>
    /// <param name="progress">
    /// Optional progress reporter for real-time updates.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token for graceful shutdown.
    /// </param>
    /// <returns>
    /// A <see cref="SyncSummary"/> with results for all schools in the district.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the specified district ID is not found in SessionDb.
    /// </exception>
    Task<SyncSummary> SyncDistrictAsync(int districtId, bool forceFullSync = false, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Synchronizes a specific school's data (students, teachers, sections) from Clever API.
    /// </summary>
    /// <remarks>
    /// <para><b>This is the core sync operation</b> that handles the actual data transfer from Clever to the school database.</para>
    /// 
    /// <para><b>Sync Type Determination:</b></para>
    /// <list type="bullet">
    ///   <item><description><b>Full Sync</b> if: <c>forceFullSync=true</c> OR <c>School.RequiresFullSync=true</c> OR no previous successful sync exists</description></item>
    ///   <item><description><b>Incremental Sync</b> otherwise: uses Clever Events API to fetch only changes</description></item>
    /// </list>
    /// 
    /// <para><b>Full Sync Workflow:</b></para>
    /// <list type="number">
    ///   <item><description>Clears EF change tracker to ensure fresh data</description></item>
    ///   <item><description>Fetches all students from Clever API</description></item>
    ///   <item><description>Fetches all teachers from Clever API</description></item>
    ///   <item><description>Fetches all sections with enrollments</description></item>
    ///   <item><description>Soft-deletes records not seen in this sync (orphaned records)</description></item>
    ///   <item><description>Establishes baseline event ID for future incremental syncs</description></item>
    ///   <item><description>Resets the <c>RequiresFullSync</c> flag</description></item>
    /// </list>
    /// 
    /// <para><b>Incremental Sync Workflow:</b></para>
    /// <list type="number">
    ///   <item><description>Retrieves the last processed event ID from SyncHistory</description></item>
    ///   <item><description>Fetches events from Clever Events API since that ID</description></item>
    ///   <item><description>Processes each event (created/updated/deleted) in order</description></item>
    ///   <item><description>Updates the baseline event ID for next sync</description></item>
    /// </list>
    /// 
    /// <para><b>Workshop Integration:</b></para>
    /// <list type="bullet">
    ///   <item><description>Tracks grade changes for workshop enrollment updates</description></item>
    ///   <item><description>Protects workshop-linked sections from accidental deletion</description></item>
    ///   <item><description>Executes workshop sync stored procedure when relevant changes detected</description></item>
    /// </list>
    /// 
    /// <para><b>Post-Sync Cleanup:</b></para>
    /// <list type="bullet">
    ///   <item><description>Cleans up expired ASP.NET sessions from the school database</description></item>
    /// </list>
    /// </remarks>
    /// <param name="schoolId">
    /// The <c>SchoolId</c> (primary key) from the SessionDb Schools table.
    /// </param>
    /// <param name="forceFullSync">
    /// When <c>true</c>, performs a full sync regardless of the school's settings.
    /// Use this to refresh all data or resolve sync issues.
    /// </param>
    /// <param name="progress">
    /// Optional progress reporter providing detailed status updates including:
    /// current operation, percent complete, and counts by entity type.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token for graceful shutdown.
    /// </param>
    /// <returns>
    /// A <see cref="SyncResult"/> containing detailed statistics for this school's sync,
    /// including processed/updated/failed counts for students, teachers, and sections.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when the school is not found in SessionDb.
    /// </exception>
    Task<SyncResult> SyncSchoolAsync(int schoolId, bool forceFullSync = false, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves sync history records for auditing and troubleshooting.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Provides visibility into past sync operations for:</para>
    /// <list type="bullet">
    ///   <item><description>Auditing when syncs occurred and their outcomes</description></item>
    ///   <item><description>Troubleshooting failed or partial syncs</description></item>
    ///   <item><description>Verifying incremental sync baselines (LastEventId)</description></item>
    ///   <item><description>Monitoring sync performance over time</description></item>
    /// </list>
    /// 
    /// <para><b>User Manual Reference:</b> Sync history is displayed in the Admin Portal
    /// under School Details ? Sync History tab.</para>
    /// </remarks>
    /// <param name="schoolId">
    /// The <c>SchoolId</c> to retrieve history for.
    /// </param>
    /// <param name="entityType">
    /// Optional filter by entity type: "Student", "Teacher", "Section", "Event", or "Baseline".
    /// Pass <c>null</c> to retrieve all entity types.
    /// </param>
    /// <param name="limit">
    /// Maximum number of records to return. Default is 10.
    /// </param>
    /// <param name="cancellationToken">
    /// Cancellation token.
    /// </param>
    /// <returns>
    /// An array of <see cref="SyncHistory"/> records ordered by <c>SyncStartTime</c> descending (most recent first).
    /// </returns>
    Task<SyncHistory[]> GetSyncHistoryAsync(
        int schoolId,
        string? entityType = null,
        int limit = 10,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Aggregated results from syncing multiple schools or an entire district.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides a high-level view of sync operations across multiple schools,
/// useful for dashboards and summary reports.</para>
/// 
/// <para><b>User Manual Reference:</b> This data is displayed in the Admin Portal sync status page
/// and in Azure Functions HTTP response when triggering sync operations.</para>
/// </remarks>
public class SyncSummary
{
    /// <summary>
    /// Total number of schools that were processed in this sync operation.
    /// </summary>
    /// <remarks>
    /// Only counts active schools (<c>IsActive = true</c>). Inactive schools are skipped.
    /// </remarks>
    public int TotalSchools { get; set; }

    /// <summary>
    /// Number of schools that completed sync without errors.
    /// </summary>
    public int SuccessfulSchools { get; set; }

    /// <summary>
    /// Number of schools where sync failed or encountered critical errors.
    /// </summary>
    /// <remarks>
    /// A failed school will have <see cref="SyncResult.Success"/> = <c>false</c>
    /// and <see cref="SyncResult.ErrorMessage"/> populated with the error details.
    /// </remarks>
    public int FailedSchools { get; set; }

    /// <summary>
    /// Total number of records (students + teachers) processed across all schools.
    /// </summary>
    /// <remarks>
    /// "Processed" means the record was examined - it may or may not have had changes.
    /// </remarks>
    public int TotalRecordsProcessed { get; set; }

    /// <summary>
    /// Total number of records that failed to sync across all schools.
    /// </summary>
    public int TotalRecordsFailed { get; set; }

    /// <summary>
    /// Individual sync results for each school.
    /// </summary>
    /// <remarks>
    /// Useful for drilling down into specific school results when troubleshooting.
    /// </remarks>
    public List<SyncResult> SchoolResults { get; set; } = new();

    /// <summary>
    /// UTC timestamp when the sync operation started.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// UTC timestamp when the sync operation completed.
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Total elapsed time for the sync operation.
    /// </summary>
    /// <remarks>
    /// Calculated as <c>EndTime - StartTime</c>.
    /// </remarks>
    public TimeSpan Duration => EndTime - StartTime;
}

/// <summary>
/// Detailed result of syncing a single school, including per-entity statistics.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides granular visibility into what happened during a school's sync,
/// essential for troubleshooting and verifying data integrity.</para>
/// 
/// <para><b>Key Metrics Explained:</b></para>
/// <list type="bullet">
///   <item><description><b>Processed</b> - Total records examined from Clever API</description></item>
///   <item><description><b>Updated</b> - Records with actual data changes persisted to database</description></item>
///   <item><description><b>Failed</b> - Records that encountered errors during sync</description></item>
///   <item><description><b>Deleted</b> - Records soft-deleted during full sync (no longer in Clever)</description></item>
/// </list>
/// </remarks>
public class SyncResult
{
    /// <summary>
    /// The school's internal database ID.
    /// </summary>
    public int SchoolId { get; set; }

    /// <summary>
    /// Display name of the school for reporting purposes.
    /// </summary>
    public string SchoolName { get; set; } = string.Empty;

    /// <summary>
    /// Indicates whether the sync completed without critical errors.
    /// </summary>
    /// <remarks>
    /// <c>true</c> if sync completed successfully (even with warnings).
    /// <c>false</c> if a critical error occurred that stopped the sync.
    /// </remarks>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if sync failed, otherwise <c>null</c>.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The type of sync that was performed (Full or Incremental).
    /// </summary>
    /// <seealso cref="SyncType"/>
    public SyncType SyncType { get; set; }

    // ========== Student Statistics ==========

    /// <summary>
    /// Total number of students examined during sync.
    /// </summary>
    public int StudentsProcessed { get; set; }

    /// <summary>
    /// Number of students with actual data changes saved to database.
    /// </summary>
    /// <remarks>
    /// This count excludes records where Clever data matched existing database values.
    /// </remarks>
    public int StudentsUpdated { get; set; }

    /// <summary>
    /// Number of students that failed to sync due to errors.
    /// </summary>
    public int StudentsFailed { get; set; }

    /// <summary>
    /// Number of students soft-deleted during full sync (no longer in Clever).
    /// </summary>
    public int StudentsDeleted { get; set; }

    // ========== Teacher Statistics ==========

    /// <summary>
    /// Total number of teachers examined during sync.
    /// </summary>
    public int TeachersProcessed { get; set; }

    /// <summary>
    /// Number of teachers with actual data changes saved to database.
    /// </summary>
    public int TeachersUpdated { get; set; }

    /// <summary>
    /// Number of teachers that failed to sync due to errors.
    /// </summary>
    public int TeachersFailed { get; set; }

    /// <summary>
    /// Number of teachers soft-deleted during full sync.
    /// </summary>
    public int TeachersDeleted { get; set; }

    // ========== Course Statistics ==========

    /// <summary>
    /// Total number of courses examined during sync.
    /// </summary>
    public int CoursesProcessed { get; set; }

    /// <summary>
    /// Number of courses with actual data changes.
    /// </summary>
    public int CoursesUpdated { get; set; }

    /// <summary>
    /// Number of courses that failed to sync.
    /// </summary>
    public int CoursesFailed { get; set; }

    // ========== Term Statistics ==========

    /// <summary>
    /// Total number of terms examined during sync.
    /// </summary>
    public int TermsProcessed { get; set; }

    /// <summary>
    /// Number of terms with actual data changes.
    /// </summary>
    public int TermsUpdated { get; set; }

    /// <summary>
    /// Number of terms that failed to sync.
    /// </summary>
    public int TermsFailed { get; set; }

    /// <summary>
    /// Number of terms soft-deleted during full sync (no longer in Clever).
    /// </summary>
    public int TermsDeleted { get; set; }

    // ========== Section Statistics ==========

    /// <summary>
    /// Total number of sections examined during sync.
    /// </summary>
    public int SectionsProcessed { get; set; }

    /// <summary>
    /// Number of sections with actual data changes.
    /// </summary>
    public int SectionsUpdated { get; set; }

    /// <summary>
    /// Number of sections that failed to sync.
    /// </summary>
    public int SectionsFailed { get; set; }

    /// <summary>
    /// Number of sections skipped because they are linked to workshops.
    /// </summary>
    /// <remarks>
    /// Workshop-linked sections are protected from automatic deletion.
    /// Manual review is required for these sections.
    /// </remarks>
    public int SectionsSkippedWorkshopLinked { get; set; }

    // ========== Warning Statistics ==========

    /// <summary>
    /// Total number of warnings generated during sync.
    /// </summary>
    /// <remarks>
    /// Warnings indicate potential issues that need admin attention but didn't stop the sync.
    /// </remarks>
    public int WarningsGenerated { get; set; }

    /// <summary>
    /// Detailed information about each warning.
    /// </summary>
    public List<SyncWarningInfo> Warnings { get; set; } = new();

    // ========== Events API Statistics ==========

    /// <summary>
    /// Summary of events processed during incremental sync.
    /// </summary>
    /// <remarks>
    /// Only populated for incremental syncs using Clever Events API.
    /// <c>null</c> for full syncs or when Events API is not available.
    /// </remarks>
    public EventsSummary? EventsSummary { get; set; }

    // ========== Timing ==========

    /// <summary>
    /// UTC timestamp when school sync started.
    /// </summary>
    public DateTime StartTime { get; set; }

    /// <summary>
    /// UTC timestamp when school sync completed.
    /// </summary>
    public DateTime EndTime { get; set; }

    /// <summary>
    /// Total elapsed time for the school sync.
    /// </summary>
    public TimeSpan Duration => EndTime - StartTime;
}

/// <summary>
/// Real-time progress information for sync operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Enables live UI updates during sync operations.
/// Used with <see cref="IProgress{T}"/> to broadcast status to Admin Portal via SignalR.</para>
/// 
/// <para><b>User Manual Reference:</b> This data powers the real-time progress bar
/// and status display in the Admin Portal during manual sync operations.</para>
/// </remarks>
public class SyncProgress
{
    /// <summary>
    /// Overall completion percentage (0-100).
    /// </summary>
    public int PercentComplete { get; set; }

    /// <summary>
    /// Human-readable description of current operation.
    /// </summary>
    /// <example>"Processing 150/500 students, 45 updated"</example>
    public string CurrentOperation { get; set; } = string.Empty;

    /// <summary>Total students examined so far.</summary>
    public int StudentsProcessed { get; set; }

    /// <summary>Students with actual changes so far.</summary>
    public int StudentsUpdated { get; set; }

    /// <summary>Students that failed so far.</summary>
    public int StudentsFailed { get; set; }

    /// <summary>Total teachers examined so far.</summary>
    public int TeachersProcessed { get; set; }

    /// <summary>Teachers with actual changes so far.</summary>
    public int TeachersUpdated { get; set; }

    /// <summary>Teachers that failed so far.</summary>
    public int TeachersFailed { get; set; }

    /// <summary>Total courses examined so far.</summary>
    public int CoursesProcessed { get; set; }

    /// <summary>Courses with actual changes so far.</summary>
    public int CoursesUpdated { get; set; }

    /// <summary>Courses that failed so far.</summary>
    public int CoursesFailed { get; set; }

    /// <summary>Total terms examined so far.</summary>
    public int TermsProcessed { get; set; }

    /// <summary>Terms with actual changes so far.</summary>
    public int TermsUpdated { get; set; }

    /// <summary>Terms that failed so far.</summary>
    public int TermsFailed { get; set; }

    /// <summary>New terms created during sync.</summary>
    public int TermsCreated { get; set; }

    /// <summary>Terms soft-deleted during sync.</summary>
    public int TermsDeleted { get; set; }

    /// <summary>Total sections examined so far.</summary>
    public int SectionsProcessed { get; set; }

    /// <summary>Sections with actual changes so far.</summary>
    public int SectionsUpdated { get; set; }

    /// <summary>Sections that failed so far.</summary>
    public int SectionsFailed { get; set; }

    /// <summary>Warnings generated so far.</summary>
    public int WarningsGenerated { get; set; }

    /// <summary>
    /// Estimated time remaining for the sync operation.
    /// </summary>
    /// <remarks>
    /// May be <c>null</c> if estimation is not available (e.g., early in the sync).
    /// </remarks>
    public TimeSpan? EstimatedTimeRemaining { get; set; }

    // ========== Incremental Sync Breakdown ==========

    /// <summary>New students created during incremental sync.</summary>
    public int StudentsCreated { get; set; }

    /// <summary>Students soft-deleted during sync.</summary>
    public int StudentsDeleted { get; set; }

    /// <summary>New teachers created during incremental sync.</summary>
    public int TeachersCreated { get; set; }

    /// <summary>Teachers soft-deleted during sync.</summary>
    public int TeachersDeleted { get; set; }

    /// <summary>New sections created during incremental sync.</summary>
    public int SectionsCreated { get; set; }

    /// <summary>Sections soft-deleted during sync.</summary>
    public int SectionsDeleted { get; set; }

    /// <summary>Events processed from Clever Events API.</summary>
    public int EventsProcessed { get; set; }

    /// <summary>Events skipped (unsupported types).</summary>
    public int EventsSkipped { get; set; }

    /// <summary>
    /// Indicates whether this is an incremental sync (vs full sync).
    /// </summary>
    public bool IsIncrementalSync { get; set; }
}

/// <summary>
/// Information about a sync warning that requires admin attention.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides detailed context about potential issues detected during sync
/// that didn't stop the operation but should be reviewed by an administrator.</para>
/// 
/// <para><b>Common Warning Types:</b></para>
/// <list type="bullet">
///   <item><description><b>SectionModified</b> - A workshop-linked section was modified</description></item>
///   <item><description><b>SectionDeleted</b> - A workshop-linked section would have been deleted</description></item>
///   <item><description><b>WorkshopSyncFailed</b> - Workshop sync stored procedure failed</description></item>
/// </list>
/// </remarks>
public class SyncWarningInfo
{
    /// <summary>
    /// Classification of the warning (e.g., "SectionModified", "SectionDeleted").
    /// </summary>
    public string WarningType { get; set; } = string.Empty;

    /// <summary>
    /// Type of entity affected (e.g., "Section", "Student", "Workshop").
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Database ID of the affected entity.
    /// </summary>
    public int EntityId { get; set; }

    /// <summary>
    /// Display name of the affected entity.
    /// </summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>
    /// Human-readable description of the warning.
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// Names of workshops affected by this warning.
    /// </summary>
    /// <remarks>
    /// Only populated for section-related warnings that impact workshops.
    /// </remarks>
    public List<string> AffectedWorkshopNames { get; set; } = new();
}

/// <summary>
/// Summary of events processed during an incremental sync using Clever Events API.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Provides visibility into what types of changes were processed
/// during an events-based incremental sync.</para>
/// 
/// <para><b>Developer Guide Note:</b> Events are processed in chronological order.
/// The Clever Events API returns events sorted oldest-to-newest to ensure
/// proper sequencing of create/update/delete operations.</para>
/// </remarks>
public class EventsSummary
{
    /// <summary>
    /// Total number of events processed from Clever Events API.
    /// </summary>
    public int TotalEventsProcessed { get; set; }

    /// <summary>Number of "students.created" events processed.</summary>
    public int StudentCreated { get; set; }

    /// <summary>Number of "students.updated" events processed.</summary>
    public int StudentUpdated { get; set; }

    /// <summary>Number of "students.deleted" events processed.</summary>
    public int StudentDeleted { get; set; }

    /// <summary>Number of "teachers.created" events processed.</summary>
    public int TeacherCreated { get; set; }

    /// <summary>Number of "teachers.updated" events processed.</summary>
    public int TeacherUpdated { get; set; }

    /// <summary>Number of "teachers.deleted" events processed.</summary>
    public int TeacherDeleted { get; set; }

    /// <summary>Number of "sections.created" events processed.</summary>
    public int SectionCreated { get; set; }

    /// <summary>Number of "sections.updated" events processed.</summary>
    public int SectionUpdated { get; set; }

    /// <summary>Number of "sections.deleted" events processed.</summary>
    public int SectionDeleted { get; set; }

    /// <summary>Number of "terms.created" events processed.</summary>
    public int TermCreated { get; set; }

    /// <summary>Number of "terms.updated" events processed.</summary>
    public int TermUpdated { get; set; }

    /// <summary>Number of "terms.deleted" events processed.</summary>
    public int TermDeleted { get; set; }

    /// <summary>
    /// Number of events skipped (unsupported types like courses, districts).
    /// </summary>
    public int EventsSkipped { get; set; }

    /// <summary>
    /// Returns a human-readable summary of events processed.
    /// </summary>
    /// <returns>
    /// A formatted string like "Processed 25 events: 10 student updated, 5 section created, 10 skipped"
    /// </returns>
    /// <example>
    /// <code>
    /// var summary = result.EventsSummary;
    /// Console.WriteLine(summary.ToDisplayString());
    /// // Output: "Processed 25 events: 10 student updated, 5 section created, 10 skipped"
    /// </code>
    /// </example>
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
        if (TermCreated > 0) parts.Add($"{TermCreated} term created");
        if (TermUpdated > 0) parts.Add($"{TermUpdated} term updated");
        if (TermDeleted > 0) parts.Add($"{TermDeleted} term deleted");
        if (EventsSkipped > 0) parts.Add($"{EventsSkipped} skipped");

        if (parts.Count == 0) return "No events processed";
        return $"Processed {TotalEventsProcessed} events: {string.Join(", ", parts)}";
    }
}
