// ---
// speckit:
//   type: implementation
//   source: SpecKit/Plans/001-clever-api-auth/plan.md
//   section: Stage 2 - Database Synchronization
//   constitution: SpecKit/Constitution/constitution.md
//   phase: Database Sync
//   version: 1.0.0
// ---

using CleverSyncSOS.Core.CleverApi;
using CleverSyncSOS.Core.CleverApi.Models;
using CleverSyncSOS.Core.Database.SchoolDb;
using CleverSyncSOS.Core.Database.SchoolDb.Entities;
using CleverSyncSOS.Core.Database.SessionDb;
using CleverSyncSOS.Core.Database.SessionDb.Entities;
using CleverSyncSOS.Core.Services;
using CleverSyncSOS.Core.Sync.Workshop;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;

namespace CleverSyncSOS.Core.Sync;

/// <summary>
/// Production implementation of <see cref="ISyncService"/> that orchestrates data synchronization
/// from Clever's Student Information System API to school databases.
/// </summary>
/// <remarks>
/// <para><b>Architecture Overview:</b></para>
/// <para>This service is the heart of CleverSyncSOS, managing the complex workflow of:</para>
/// <list type="number">
///   <item><description>Authenticating with Clever API (via <see cref="ICleverApiClient"/>)</description></item>
///   <item><description>Fetching student, teacher, and section data</description></item>
///   <item><description>Transforming Clever DTOs to local entity models</description></item>
///   <item><description>Persisting changes to per-school databases</description></item>
///   <item><description>Recording sync history for auditing and incremental sync</description></item>
/// </list>
/// 
/// <para><b>Database Architecture:</b></para>
/// <para>CleverSyncSOS uses a dual-database pattern for data isolation:</para>
/// <list type="bullet">
///   <item><description><b>SessionDb</b> (<see cref="SessionDbContext"/>) - Central metadata database containing:
///     Districts, Schools, SyncHistory, Users, AuditLogs</description></item>
///   <item><description><b>Per-School Databases</b> (<see cref="SchoolDbContext"/>) - Isolated databases for each school
///     containing: Students, Teachers, Sections, Enrollments, Workshops</description></item>
/// </list>
/// 
/// <para><b>Sync Modes Explained:</b></para>
/// <para><b>Full Sync (Beginning of Year / Recovery):</b></para>
/// <list type="bullet">
///   <item><description>Fetches ALL records from Clever API without date filters</description></item>
///   <item><description>Uses <c>LastSyncedAt</c> timestamp to detect orphaned records</description></item>
///   <item><description>Soft-deletes records not seen in Clever (graduated students, transferred teachers)</description></item>
///   <item><description>Establishes baseline event ID for future incremental syncs</description></item>
///   <item><description>Triggered by: <c>forceFullSync=true</c>, <c>School.RequiresFullSync=true</c>, or no prior sync</description></item>
/// </list>
/// 
/// <para><b>Incremental Sync (Daily Operations):</b></para>
/// <list type="bullet">
///   <item><description>Uses Clever Events API to fetch only changes since last sync</description></item>
///   <item><description>Processes events in chronological order (created → updated → deleted)</description></item>
///   <item><description>Falls back to data API with <c>last_modified</c> filter if Events API unavailable</description></item>
///   <item><description>Much faster than full sync for routine daily operations</description></item>
/// </list>
/// 
/// <para><b>Workshop Integration:</b></para>
/// <para>The service integrates with the Workshop system to:</para>
/// <list type="bullet">
///   <item><description>Track grade changes that may affect workshop eligibility</description></item>
///   <item><description>Protect workshop-linked sections from accidental deletion</description></item>
///   <item><description>Generate warnings when workshop-relevant changes are detected</description></item>
///   <item><description>Execute workshop sync stored procedures when needed</description></item>
/// </list>
/// 
/// <para><b>Concurrency and Performance:</b></para>
/// <list type="bullet">
///   <item><description>Schools are processed in parallel (max 5 concurrent via SemaphoreSlim)</description></item>
///   <item><description>Progress is reported incrementally for UI responsiveness</description></item>
///   <item><description>EF Core change tracker is cleared before full sync to prevent stale cache</description></item>
///   <item><description>Batched saves for efficiency during bulk operations</description></item>
/// </list>
/// 
/// <para><b>Error Handling Strategy:</b></para>
/// <list type="bullet">
///   <item><description>Individual record failures don't stop the sync (logged and counted)</description></item>
///   <item><description>Individual school failures don't stop other schools</description></item>
///   <item><description>Critical errors (e.g., database connection) propagate up with detailed logging</description></item>
///   <item><description>All operations are wrapped in try/catch with structured logging</description></item>
/// </list>
/// 
/// <para><b>Specification References:</b></para>
/// <list type="bullet">
///   <item><description>FR-005: Clever Data Synchronization</description></item>
///   <item><description>FR-006: Full Sync Mode</description></item>
///   <item><description>FR-007: Incremental Sync Mode (Events API primary, data API fallback)</description></item>
///   <item><description>FR-009: Sync Orchestration</description></item>
///   <item><description>FR-010: Retry Logic and Error Handling</description></item>
/// </list>
/// </remarks>
/// <seealso cref="ISyncService"/>
/// <seealso cref="ICleverApiClient"/>
/// <seealso cref="SchoolDatabaseConnectionFactory"/>
/// <seealso cref="IWorkshopSyncService"/>
public class SyncService : ISyncService
{
    private readonly ICleverApiClient _cleverClient;
    private readonly SessionDbContext _sessionDb;
    private readonly SchoolDatabaseConnectionFactory _schoolDbFactory;
    private readonly ILocalTimeService _localTimeService;
    private readonly IWorkshopSyncService _workshopSyncService;
    private readonly ISyncValidationService _validationService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<SyncService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="SyncService"/> class.
    /// </summary>
    /// <remarks>
    /// <para>All dependencies are injected via the DI container configured in 
    /// <c>ServiceCollectionExtensions.AddCleverSync()</c>.</para>
    /// 
    /// <para><b>Dependency Responsibilities:</b></para>
    /// <list type="bullet">
    ///   <item><description><b>cleverClient</b>: Handles all Clever API communication (auth, pagination, rate limiting)</description></item>
    ///   <item><description><b>sessionDb</b>: Central database for districts, schools, sync history</description></item>
    ///   <item><description><b>schoolDbFactory</b>: Creates per-school database contexts dynamically from Key Vault connection strings</description></item>
    ///   <item><description><b>localTimeService</b>: Provides timezone-aware timestamps for each school</description></item>
    ///   <item><description><b>workshopSyncService</b>: Manages workshop-related sync operations</description></item>
    ///   <item><description><b>validationService</b>: Handles data validation and normalization (grade parsing, string comparison)</description></item>
    ///   <item><description><b>serviceProvider</b>: Resolves optional services like session cleanup</description></item>
    ///   <item><description><b>logger</b>: Structured logging for operations and diagnostics</description></item>
    /// </list>
    /// </remarks>
    /// <param name="cleverClient">Client for Clever API communication.</param>
    /// <param name="sessionDb">SessionDb database context for orchestration data.</param>
    /// <param name="schoolDbFactory">Factory for creating per-school database connections.</param>
    /// <param name="localTimeService">Service for timezone-aware time operations.</param>
    /// <param name="workshopSyncService">Service for workshop synchronization.</param>
    /// <param name="validationService">Service for data validation and normalization.</param>
    /// <param name="serviceProvider">DI service provider for optional services.</param>
    /// <param name="logger">Logger for structured logging.</param>
    public SyncService(
        ICleverApiClient cleverClient,
        SessionDbContext sessionDb,
        SchoolDatabaseConnectionFactory schoolDbFactory,
        ILocalTimeService localTimeService,
        IWorkshopSyncService workshopSyncService,
        ISyncValidationService validationService,
        IServiceProvider serviceProvider,
        ILogger<SyncService> logger)
    {
        _cleverClient = cleverClient;
        _sessionDb = sessionDb;
        _schoolDbFactory = schoolDbFactory;
        _localTimeService = localTimeService;
        _workshopSyncService = workshopSyncService;
        _validationService = validationService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<SyncSummary> SyncAllDistrictsAsync(bool forceFullSync = false, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Starting sync for all districts (forceFullSync: {ForceFullSync})", forceFullSync);

        var summary = new SyncSummary
        {
            StartTime = DateTime.UtcNow
        };

        try
        {
            // Step 1: Query all districts from SessionDb
            var districts = await _sessionDb.Districts
                .Include(d => d.Schools)
                .ToListAsync(cancellationToken);

            _logger.LogInformation("Found {Count} districts to sync", districts.Count);

            // Step 2: Sync each district
            int districtsCompleted = 0;
            foreach (var district in districts)
            {
                try
                {
                    progress?.Report(new SyncProgress
                    {
                        PercentComplete = 10 + (80 * districtsCompleted / districts.Count),
                        CurrentOperation = $"Syncing district: {district.Name}..."
                    });

                    var districtResult = await SyncDistrictAsync(district.DistrictId, forceFullSync, progress, cancellationToken);
                    summary.TotalSchools += districtResult.TotalSchools;
                    summary.SuccessfulSchools += districtResult.SuccessfulSchools;
                    summary.FailedSchools += districtResult.FailedSchools;
                    summary.TotalRecordsProcessed += districtResult.TotalRecordsProcessed;
                    summary.TotalRecordsFailed += districtResult.TotalRecordsFailed;
                    summary.SchoolResults.AddRange(districtResult.SchoolResults);
                    districtsCompleted++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to sync district {DistrictId} ({DistrictName})",
                        district.DistrictId, district.Name);
                    districtsCompleted++;
                }
            }

            summary.EndTime = DateTime.UtcNow;
            _logger.LogInformation(
                "Completed sync for all districts: {SuccessfulSchools}/{TotalSchools} schools successful, {TotalRecords} records processed in {Duration}",
                summary.SuccessfulSchools, summary.TotalSchools, summary.TotalRecordsProcessed, summary.Duration);

            return summary;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Critical error during sync of all districts");
            summary.EndTime = DateTime.UtcNow;
            throw;
        }
    }

    /// <inheritdoc />
    public async Task<SyncSummary> SyncDistrictAsync(int districtId, bool forceFullSync = false, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var district = await _sessionDb.Districts
            .Include(d => d.Schools)
            .FirstOrDefaultAsync(d => d.DistrictId == districtId, cancellationToken);

        if (district == null)
        {
            throw new InvalidOperationException($"District {districtId} not found in SessionDb");
        }

        _logger.LogInformation("Starting sync for district {DistrictId} ({DistrictName}) with {SchoolCount} schools",
            district.DistrictId, district.Name, district.Schools.Count);

        var summary = new SyncSummary
        {
            StartTime = DateTime.UtcNow,
            TotalSchools = district.Schools.Count(s => s.IsActive)
        };

        // Step 3: Sync active schools in parallel (max 5 concurrent)
        var activeSchools = district.Schools.Where(s => s.IsActive).ToList();

        progress?.Report(new SyncProgress
        {
            PercentComplete = 10,
            CurrentOperation = $"Starting sync for {activeSchools.Count} schools in {district.Name}..."
        });

        var semaphore = new SemaphoreSlim(5, 5); // Max 5 concurrent school syncs
        int schoolsCompleted = 0;
        var syncTasks = activeSchools.Select(async school =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                // Create a nested progress reporter that scales to this school's portion
                var schoolProgress = new Progress<SyncProgress>(p =>
                {
                    var overallPercent = 10 + (80 * schoolsCompleted / activeSchools.Count);
                    progress?.Report(new SyncProgress
                    {
                        PercentComplete = overallPercent,
                        CurrentOperation = $"{school.Name}: {p.CurrentOperation}",
                        StudentsProcessed = p.StudentsProcessed,
                        StudentsUpdated = p.StudentsUpdated,
                        StudentsFailed = p.StudentsFailed,
                        TeachersProcessed = p.TeachersProcessed,
                        TeachersUpdated = p.TeachersUpdated,
                        TeachersFailed = p.TeachersFailed
                    });
                });

                var result = await SyncSchoolAsync(school.SchoolId, forceFullSync, schoolProgress, cancellationToken);
                Interlocked.Increment(ref schoolsCompleted);
                return result;
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(syncTasks);

        summary.SchoolResults.AddRange(results);
        summary.SuccessfulSchools = results.Count(r => r.Success);
        summary.FailedSchools = results.Count(r => !r.Success);
        summary.TotalRecordsProcessed = results.Sum(r => r.StudentsProcessed + r.TeachersProcessed);
        summary.TotalRecordsFailed = results.Sum(r => r.StudentsFailed + r.TeachersFailed);
        summary.EndTime = DateTime.UtcNow;

        _logger.LogInformation(
            "Completed sync for district {DistrictId}: {SuccessfulSchools}/{TotalSchools} schools successful",
            districtId, summary.SuccessfulSchools, summary.TotalSchools);

        return summary;
    }

    /// <inheritdoc />
    public async Task<SyncResult> SyncSchoolAsync(int schoolId, bool forceFullSync = false, IProgress<SyncProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = new SyncResult
        {
            SchoolId = schoolId,
            StartTime = DateTime.UtcNow
        };

        try
        {
            // Step 4a: Read school from SessionDb
            var school = await _sessionDb.Schools
                .FirstOrDefaultAsync(s => s.SchoolId == schoolId, cancellationToken);

            if (school == null)
            {
                throw new InvalidOperationException($"School {schoolId} not found in SessionDb");
            }

            result.SchoolName = school.Name;

            _logger.LogInformation("Starting sync for school {SchoolId} ({SchoolName})", schoolId, school.Name);

            // Report initial progress
            progress?.Report(new SyncProgress
            {
                PercentComplete = 5,
                CurrentOperation = $"Connecting to {school.Name} database..."
            });

            // Step 4b: Connect to school's dedicated database
            await using var schoolDb = await _schoolDbFactory.CreateSchoolContextAsync(school);

            // Create cached time context for this school to avoid repeated timezone lookups
            var timeContext = await _localTimeService.CreateSchoolTimeContextAsync(schoolId, cancellationToken);
            _logger.LogDebug("Using timezone {TimeZone} for school {SchoolId}", timeContext.TimeZoneId, schoolId);

            // Step 4d: Determine sync type (Full or Incremental)
            var lastSync = await _sessionDb.SyncHistory
                .Where(h => h.SchoolId == schoolId && h.Status == "Success")
                .OrderByDescending(h => h.SyncEndTime)
                .FirstOrDefaultAsync(cancellationToken);

            bool isFullSync = forceFullSync || school.RequiresFullSync || lastSync == null;
            result.SyncType = isFullSync ? SyncType.Full : SyncType.Incremental;

            _logger.LogInformation("Sync type for school {SchoolId}: {SyncType}", schoolId, result.SyncType);

            progress?.Report(new SyncProgress
            {
                PercentComplete = 10,
                CurrentOperation = $"Starting {result.SyncType} sync for {school.Name}..."
            });

            // Create workshop tracker to detect workshop-relevant changes
            var workshopTracker = new WorkshopSyncTracker();

            // Get the Section SyncId for workshop sync (will be populated during sync)
            int sectionSyncId = 0;

            // Step 4e-4g: Sync students and teachers
            if (isFullSync)
            {
                sectionSyncId = await PerformFullSyncAsync(school, schoolDb, result, timeContext, progress, workshopTracker, cancellationToken);
            }
            else
            {
                sectionSyncId = await PerformIncrementalSyncAsync(school, schoolDb, result, lastSync!.LastSyncTimestamp, timeContext, progress, workshopTracker, cancellationToken);
            }

            // Execute workshop sync if there were workshop-relevant changes
            if (sectionSyncId > 0)
            {
                progress?.Report(new SyncProgress
                {
                    CurrentOperation = "Syncing Workshops linked to Sections or Grades"
                });

                var workshopResult = await _workshopSyncService.ExecuteWorkshopSyncAsync(
                    schoolDb, sectionSyncId, workshopTracker, cancellationToken);

                if (!workshopResult.Success && !workshopResult.Skipped)
                {
                    result.WarningsGenerated++;
                    result.Warnings.Add(new SyncWarningInfo
                    {
                        WarningType = "WorkshopSyncFailed",
                        EntityType = "Workshop",
                        EntityId = 0,
                        EntityName = "Workshop Sync",
                        Message = $"Workshop sync stored procedure failed: {workshopResult.ErrorMessage}",
                        AffectedWorkshopNames = new List<string>()
                    });
                }
            }

            // Reset RequiresFullSync flag after successful full sync
            if (isFullSync && school.RequiresFullSync)
            {
                school.RequiresFullSync = false;
                school.UpdatedAt = DateTime.UtcNow;
                await _sessionDb.SaveChangesAsync(cancellationToken);
            }

            // Clean up expired ASP.NET sessions from the school database
            progress?.Report(new SyncProgress
            {
                CurrentOperation = "Cleaning up expired sessions..."
            });

            try
            {
                var sessionCleanupService = _serviceProvider.GetService<ISessionCleanupService>();
                if (sessionCleanupService != null)
                {
                    var cleanupResult = await sessionCleanupService.CleanupSchoolSessionsAsync(schoolId, cancellationToken);
                    if (cleanupResult.Success && cleanupResult.SessionsDeleted > 0)
                    {
                        _logger.LogInformation(
                            "Session cleanup for school {SchoolId}: {SessionsDeleted} expired sessions deleted",
                            schoolId, cleanupResult.SessionsDeleted);
                    }
                }
            }
            catch (Exception cleanupEx)
            {
                // Session cleanup failure should not fail the overall sync
                _logger.LogWarning(cleanupEx,
                    "Session cleanup failed for school {SchoolId}, but sync completed successfully",
                    schoolId);
            }

            result.Success = true;
            result.EndTime = DateTime.UtcNow;

            _logger.LogInformation(
                "Completed sync for school {SchoolId}: {StudentsProcessed} students, {TeachersProcessed} teachers processed in {Duration}",
                schoolId, result.StudentsProcessed, result.TeachersProcessed, result.Duration);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync school {SchoolId}", schoolId);
            result.Success = false;
            result.ErrorMessage = ex.Message;
            result.EndTime = DateTime.UtcNow;
            return result;
        }
    }

    /// <summary>
    /// Performs a full sync with hard-delete for inactive records.
    /// Source: FR-025 - Beginning-of-year sync with hard-delete behavior
    /// </summary>
    /// <returns>The SyncId to use for workshop sync (Student SyncId)</returns>
    private async Task<int> PerformFullSyncAsync(
        School school,
        SchoolDbContext schoolDb,
        SyncResult result,
        ISchoolTimeContext timeContext,
        IProgress<SyncProgress>? progress,
        WorkshopSyncTracker workshopTracker,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Performing FULL sync for school {SchoolId}", school.SchoolId);

        // Capture the sync start time in local time for LastSyncedAt timestamps
        var syncStartTime = timeContext.Now;

        // Clear EF change tracker so UpsertStudentAsync fetches fresh data from DB
        // This prevents the inactive entities from being cached in memory
        schoolDb.ChangeTracker.Clear();

        progress?.Report(new SyncProgress
        {
            PercentComplete = 20,
            CurrentOperation = "Fetching students from Clever API..."
        });

        // Step 2: Fetch all students and teachers from Clever API
        // Pass syncStartTime (local time) so next incremental sync has a valid timestamp
        // Also pass workshopTracker to track grade changes
        int studentSyncId = await SyncStudentsAsync(school, schoolDb, result, syncStartTime, timeContext, progress, 20, 60, workshopTracker, cancellationToken);

        progress?.Report(new SyncProgress
        {
            PercentComplete = 60,
            CurrentOperation = "Fetching teachers from Clever API...",
            StudentsProcessed = result.StudentsProcessed,
            StudentsFailed = result.StudentsFailed
        });

        // Pass syncStartTime (local time) so next incremental sync has a valid timestamp
        await SyncTeachersAsync(school, schoolDb, result, syncStartTime, timeContext, progress, 60, 80, cancellationToken);

        progress?.Report(new SyncProgress
        {
            PercentComplete = 80,
            CurrentOperation = "Syncing sections from Clever API...",
            StudentsProcessed = result.StudentsProcessed,
            TeachersProcessed = result.TeachersProcessed
        });

        // Sync sections (includes student enrollments which may trigger workshop sync)
        int sectionSyncId = await SyncSectionsAsync(school, schoolDb, result, timeContext, progress, 80, 88, workshopTracker, cancellationToken);

        // Sync terms (district-level entities stored per-school)
        progress?.Report(new SyncProgress
        {
            PercentComplete = 88,
            CurrentOperation = "Syncing terms from Clever API...",
            StudentsProcessed = result.StudentsProcessed,
            TeachersProcessed = result.TeachersProcessed,
            SectionsProcessed = result.SectionsProcessed
        });

        await SyncTermsAsync(school, schoolDb, result, syncStartTime, timeContext, progress, cancellationToken);

        // Step 3: Soft-delete students, teachers, and terms not seen in this sync
        // Records not seen will have LastSyncedAt < syncStartTime
        var orphanedStudents = await schoolDb.Students
            .Where(s => s.LastSyncedAt < syncStartTime && s.DeletedAt == null)
            .ToListAsync(cancellationToken);
        var orphanedTeachers = await schoolDb.Teachers
            .Where(t => t.LastSyncedAt < syncStartTime && t.DeletedAt == null)
            .ToListAsync(cancellationToken);
        var orphanedTerms = await schoolDb.Terms
            .Where(t => t.LastSyncedAt < syncStartTime && t.DeletedAt == null)
            .ToListAsync(cancellationToken);

        foreach (var student in orphanedStudents)
        {
            student.DeletedAt = syncStartTime;
            student.UpdatedAt = syncStartTime;
        }
        foreach (var teacher in orphanedTeachers)
        {
            teacher.DeletedAt = syncStartTime;
            teacher.UpdatedAt = syncStartTime;
        }
        foreach (var term in orphanedTerms)
        {
            term.DeletedAt = syncStartTime;
            term.UpdatedAt = syncStartTime;
        }

        result.StudentsDeleted = orphanedStudents.Count;
        result.TeachersDeleted = orphanedTeachers.Count;
        result.TermsDeleted = orphanedTerms.Count;

        await schoolDb.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Full sync complete for school {SchoolId}: Soft-deleted {StudentsDeleted} students, {TeachersDeleted} teachers, {TermsDeleted} terms",
            school.SchoolId, result.StudentsDeleted, result.TeachersDeleted, result.TermsDeleted);

        // Establish baseline for future incremental syncs
        // IMPORTANT: Filter by school when getting the baseline event ID
        // because school-filtered events exclude district-level events
        // This ensures the baseline matches what incremental sync will see
        try
        {
            var latestEventId = await _cleverClient.GetLatestEventIdAsync(school.CleverSchoolId, cancellationToken);

            // Create baseline entry regardless of whether Events API has data yet
            // If no events exist yet (Events API just enabled), LastEventId will be null
            // and incremental syncs will fall back to timestamp-based change detection
            var baselineSyncHistory = new SyncHistory
            {
                SchoolId = school.SchoolId,
                EntityType = "Baseline",
                SyncType = SyncType.Full,
                SyncStartTime = DateTime.UtcNow,
                SyncEndTime = DateTime.UtcNow,
                Status = "Success",
                RecordsProcessed = 0,
                RecordsUpdated = 0,
                RecordsFailed = 0,
                LastEventId = latestEventId, // May be null if Events API has no data yet
                LastSyncTimestamp = syncStartTime
            };
            _sessionDb.SyncHistory.Add(baselineSyncHistory);
            await _sessionDb.SaveChangesAsync(cancellationToken);

            if (!string.IsNullOrEmpty(latestEventId))
            {
                _logger.LogInformation("Established baseline event ID for future incremental syncs: {EventId}", latestEventId);
            }
            else
            {
                _logger.LogInformation("No events available yet - incremental syncs will use timestamp-based change detection until events are generated");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to establish baseline for school {SchoolId}", school.SchoolId);
        }

        // Return the Section SyncId for workshop sync (section sync includes student enrollments)
        return sectionSyncId;
    }

    /// <summary>
    /// Performs an incremental sync using Clever's Events API.
    /// Source: FR-024 - Incremental sync using Events API
    /// Documentation: https://dev.clever.com/docs/events-api
    /// </summary>
    /// <returns>The SyncId to use for workshop sync (Event SyncId)</returns>
    private async Task<int> PerformIncrementalSyncAsync(
        School school,
        SchoolDbContext schoolDb,
        SyncResult result,
        DateTime? lastModified,
        ISchoolTimeContext timeContext,
        IProgress<SyncProgress>? progress,
        WorkshopSyncTracker workshopTracker,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Performing INCREMENTAL sync for school {SchoolId} using Events API",
            school.SchoolId);

        // Get the last event ID from the most recent successful sync (with diagnostic info)
        // IMPORTANT: Must check for both null AND empty string to match EventsCheckService behavior
        var lastSyncInfo = await _sessionDb.SyncHistory
            .Where(h => h.SchoolId == school.SchoolId && h.Status == "Success" && h.LastEventId != null && h.LastEventId != "")
            .OrderByDescending(h => h.SyncEndTime)
            .Select(h => new { h.LastEventId, h.SyncEndTime, h.LastEventTimestamp, h.SyncId, h.EntityType })
            .FirstOrDefaultAsync(cancellationToken);

        var lastEventId = lastSyncInfo?.LastEventId;

        // DIAGNOSTIC: Log the baseline being used for incremental sync
        _logger.LogInformation(
            "SYNC BASELINE DIAGNOSTIC [School {SchoolId}]: LastEventId={LastEventId}, SyncId={SyncId}, EntityType={EntityType}, SyncEndTime={SyncEndTime}, LastEventTimestamp={LastEventTimestamp}",
            school.SchoolId,
            lastEventId ?? "NULL",
            lastSyncInfo?.SyncId,
            lastSyncInfo?.EntityType ?? "N/A",
            lastSyncInfo?.SyncEndTime,
            lastSyncInfo?.LastEventTimestamp);

        if (string.IsNullOrEmpty(lastEventId))
        {
            // Events API not available - use data API with change detection
            _logger.LogInformation("Events API not available for school {SchoolId}. Using data API with change detection.", school.SchoolId);

            // Fetch all students and teachers without marking them inactive
            // The UpsertStudentAsync method will detect actual changes
            // Pass workshopTracker to track grade changes
            int studentSyncId = await SyncStudentsAsync(school, schoolDb, result, lastModified, timeContext, progress, 10, 60, workshopTracker, cancellationToken);
            await SyncTeachersAsync(school, schoolDb, result, lastModified, timeContext, progress, 60, 100, cancellationToken);

            _logger.LogInformation("Incremental sync complete using data API. Students: {Processed} processed, {Updated} updated",
                result.StudentsProcessed, result.StudentsUpdated);
            return studentSyncId;
        }

        _logger.LogInformation("Fetching events after event ID: {EventId} for CleverSchoolId: {CleverSchoolId}",
            lastEventId, school.CleverSchoolId);

        // Fetch ALL events for this school since the last event ID
        // Don't filter by recordType - we need users (students/teachers) and sections
        // The ProcessEventAsync method handles each event type appropriately
        var events = await _cleverClient.GetEventsAsync(
            startingAfter: lastEventId,
            schoolId: school.CleverSchoolId,
            recordType: null, // Get all event types (users, sections, etc.)
            limit: 1000,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Retrieved {Count} events for school {SchoolId} (CleverSchoolId: {CleverSchoolId})",
            events.Length, school.SchoolId, school.CleverSchoolId);

        // DIAGNOSTIC: Log event range details
        if (events.Length > 0)
        {
            var firstEvent = events.First();
            var lastEvent = events.Last();
            _logger.LogInformation(
                "SYNC EVENTS DIAGNOSTIC [School {SchoolId}]: FirstEventId={FirstEventId}, FirstEventTime={FirstEventTime}, FirstEventType={FirstEventType}, LastEventId={LastEventId}, LastEventTime={LastEventTime}",
                school.SchoolId,
                firstEvent.Id,
                firstEvent.Created,
                firstEvent.Type,
                lastEvent.Id,
                lastEvent.Created);
        }

        // Initialize EventsSummary for tracking
        result.EventsSummary = new EventsSummary();

        if (events.Length == 0)
        {
            _logger.LogWarning("DIAGNOSTIC: No events returned from Clever API for school {SchoolId}. " +
                "This could mean: 1) No changes since last sync, 2) Wrong LastEventId baseline, or 3) School filter mismatch. " +
                "LastEventId used: {LastEventId}, CleverSchoolId: {CleverSchoolId}",
                school.SchoolId, lastEventId, school.CleverSchoolId);

            // Still create a SyncHistory record to show the sync ran successfully
            var noEventsSyncHistory = new SyncHistory
            {
                SchoolId = school.SchoolId,
                EntityType = "Event",
                SyncType = SyncType.Incremental,
                SyncStartTime = DateTime.UtcNow,
                SyncEndTime = DateTime.UtcNow,
                Status = "Success",
                RecordsProcessed = 0,
                RecordsUpdated = 0,
                RecordsFailed = 0,
                LastEventId = lastEventId,
                LastSyncTimestamp = timeContext.Now
            };
            _sessionDb.SyncHistory.Add(noEventsSyncHistory);
            await _sessionDb.SaveChangesAsync(cancellationToken);

            // Return 0 - no SyncId to use for workshop sync when no events
            return 0;
        }

        // Create SyncHistory record first to get the SyncId for change tracking
        var eventSyncHistory = new SyncHistory
        {
            SchoolId = school.SchoolId,
            EntityType = "Event",
            SyncType = SyncType.Incremental,
            SyncStartTime = DateTime.UtcNow,
            Status = "InProgress",
            RecordsProcessed = 0,
            RecordsUpdated = 0,
            RecordsFailed = 0,
            LastEventId = lastEventId
        };
        _sessionDb.SyncHistory.Add(eventSyncHistory);
        await _sessionDb.SaveChangesAsync(cancellationToken);

        // Create change tracker for detailed change logging
        var changeTracker = new ChangeTracker(_sessionDb, _logger);

        // Pre-load workshop-linked sections for efficient lookup during section event processing
        var workshopLinkedSectionIds = await _workshopSyncService.GetWorkshopLinkedSectionIdsAsync(schoolDb, cancellationToken);

        // Process events in chronological order (they're already sorted oldest to newest)
        string? latestEventId = null;
        DateTime? latestEventTimestamp = null;
        int eventsProcessed = 0;
        foreach (var evt in events)
        {
            try
            {
                await ProcessEventAsync(school, schoolDb, evt, result, eventSyncHistory.SyncId, changeTracker, timeContext, workshopTracker, workshopLinkedSectionIds, cancellationToken);
                latestEventId = evt.Id; // Track the latest event ID we've processed
                if (evt.Created.HasValue)
                {
                    latestEventTimestamp = evt.Created.Value; // Track timestamp of the latest event
                }
                eventsProcessed++;

                // Report progress with EventsSummary breakdown
                if (eventsProcessed % 10 == 0 || eventsProcessed == events.Length)
                {
                    progress?.Report(new SyncProgress
                    {
                        PercentComplete = 10 + (80 * eventsProcessed / events.Length),
                        CurrentOperation = $"Processing {eventsProcessed}/{events.Length} events...",
                        IsIncrementalSync = true,
                        EventsProcessed = eventsProcessed,
                        EventsSkipped = result.EventsSummary?.EventsSkipped ?? 0,
                        StudentsCreated = result.EventsSummary?.StudentCreated ?? 0,
                        StudentsUpdated = result.EventsSummary?.StudentUpdated ?? 0,
                        StudentsDeleted = result.EventsSummary?.StudentDeleted ?? 0,
                        TeachersCreated = result.EventsSummary?.TeacherCreated ?? 0,
                        TeachersUpdated = result.EventsSummary?.TeacherUpdated ?? 0,
                        TeachersDeleted = result.EventsSummary?.TeacherDeleted ?? 0,
                        SectionsCreated = result.EventsSummary?.SectionCreated ?? 0,
                        SectionsUpdated = result.EventsSummary?.SectionUpdated ?? 0,
                        SectionsDeleted = result.EventsSummary?.SectionDeleted ?? 0
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process event {EventId} (type: {EventType}) for school {SchoolId}",
                    evt.Id, evt.Type, school.SchoolId);
                result.StudentsFailed++; // Count as failed (could be student or teacher)
            }
        }

        // Save all pending change details
        await changeTracker.SaveChangesAsync(cancellationToken);

        // Update sync history with the latest event ID and final status
        // Always update status - even if all events failed processing, we need to mark the sync as complete
        eventSyncHistory.SyncEndTime = DateTime.UtcNow;
        eventSyncHistory.RecordsProcessed = events.Length;
        eventSyncHistory.RecordsUpdated = result.StudentsUpdated + result.TeachersUpdated + result.SectionsUpdated;

        if (!string.IsNullOrEmpty(latestEventId))
        {
            // At least one event was successfully processed
            eventSyncHistory.Status = "Success";
            eventSyncHistory.LastEventId = latestEventId;
            eventSyncHistory.LastEventTimestamp = latestEventTimestamp;
        }
        else
        {
            // All events failed to process - mark as partial/failed but still close the sync
            // Use the last event ID from the fetched events to advance the baseline
            // This prevents re-processing the same failed events indefinitely
            var lastFetchedEventId = events.LastOrDefault()?.Id;
            if (!string.IsNullOrEmpty(lastFetchedEventId))
            {
                eventSyncHistory.Status = "Partial";
                eventSyncHistory.LastEventId = lastFetchedEventId;
                eventSyncHistory.LastEventTimestamp = events.LastOrDefault()?.Created;
                eventSyncHistory.ErrorMessage = $"All {events.Length} events failed to process. Check logs for details.";
                _logger.LogWarning("Incremental sync completed with all events failing. Advanced baseline to {EventId} to prevent infinite retry loop.", lastFetchedEventId);
            }
            else
            {
                eventSyncHistory.Status = "Failed";
                eventSyncHistory.ErrorMessage = "Events were returned but none could be processed.";
            }
        }

        // Serialize and save EventsSummary
        if (result.EventsSummary != null)
        {
            eventSyncHistory.EventsSummaryJson = System.Text.Json.JsonSerializer.Serialize(result.EventsSummary);
        }

        await _sessionDb.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Incremental sync complete. Status: {Status}, Last event ID: {EventId}, Last event time: {EventTime}. {EventsSummary}",
            eventSyncHistory.Status, eventSyncHistory.LastEventId, eventSyncHistory.LastEventTimestamp, result.EventsSummary?.ToDisplayString() ?? "No events");

        // Return the Event SyncId for workshop sync
        return eventSyncHistory.SyncId;
    }

    /// <summary>
    /// Processes a single event from Clever's Events API.
    /// Handles created, updated, and deleted events for students, teachers, and sections.
    /// </summary>
    private async Task ProcessEventAsync(
        School school,
        SchoolDbContext schoolDb,
        CleverEvent evt,
        SyncResult result,
        int syncId,
        ChangeTracker changeTracker,
        ISchoolTimeContext timeContext,
        WorkshopSyncTracker workshopTracker,
        HashSet<int> workshopLinkedSectionIds,
        CancellationToken cancellationToken)
    {
        // Get object type from event data, falling back to extracting from event Type field
        // evt.Data.Object reads from the "object" field in the data wrapper (e.g., "user", "section")
        // evt.ObjectType extracts from the Type field (e.g., "sections" from "sections.updated")
        var objectType = evt.Data.Object;
        if (string.IsNullOrEmpty(objectType))
        {
            // Fall back to extracting from event Type and normalize to singular form
            objectType = evt.ObjectType?.TrimEnd('s') ?? string.Empty;
        }

        // Extract action type from event Type field (e.g., "created" from "sections.created")
        var eventType = evt.ActionType;
        var eventsSummary = result.EventsSummary;

        // Track total events processed
        if (eventsSummary != null)
        {
            eventsSummary.TotalEventsProcessed++;
        }

        // DIAGNOSTIC: Log every event being processed (not just debug level)
        _logger.LogInformation("Processing event {EventId}: Type={EventType}, ObjectType={ObjectType}, ObjectId={ObjectId}",
            evt.Id, evt.Type, objectType, evt.Data.Id);

        // DIAGNOSTIC: Log raw data presence
        var hasRawData = evt.Data.RawData != null && evt.Data.RawData.Value.ValueKind != System.Text.Json.JsonValueKind.Undefined;
        _logger.LogInformation("DIAGNOSTIC: Event {EventId} RawData present={HasRawData}, ValueKind={ValueKind}",
            evt.Id, hasRawData,
            evt.Data.RawData?.ValueKind.ToString() ?? "null");

        // Check for raw data presence
        if (!hasRawData)
        {
            _logger.LogWarning("Event {EventId} has no data (RawData is null or undefined). This may indicate a model mismatch with Clever API. Skipping.", evt.Id);
            return;
        }

        // Get the raw data element directly - after CleverEvent model fix, RawData IS the data (no wrapper)
        // In Clever Events API v3.0, the "object" field contains the actual user/section data directly
        var dataElement = evt.Data.RawData.Value;
        var rawDataJson = dataElement.GetRawText();

        _logger.LogInformation("DIAGNOSTIC: Event {EventId} data JSON (first 500 chars): {Json}",
            evt.Id, rawDataJson.Length > 500 ? rawDataJson.Substring(0, 500) + "..." : rawDataJson);

        if (dataElement.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            _logger.LogWarning("Event {EventId} has invalid data structure (not an object). Skipping.", evt.Id);
            return;
        }

        // Handle different object types
        // District events are logged but not processed (they don't map to school-level data)
        // Course events are also skipped - we only sync users and sections
        if (objectType == "course" || objectType == "district")
        {
            _logger.LogDebug("Event {EventId} is a {ObjectType} event - skipping (not synced)", evt.Id, objectType);
            if (eventsSummary != null) eventsSummary.EventsSkipped++;
            return;
        }

        // Section events don't have roles - handle them in the section block below
        // User events need role validation
        string? role = null;
        if (objectType == "user")
        {
            _logger.LogInformation("DIAGNOSTIC: User event detected for {EventId}. Checking roles...", evt.Id);

            // For user events, determine if this is a student or teacher based on roles object
            // Clever v3.0 returns roles as an object: { "student": {...} } or { "teacher": {...} }
            // NOT as an array like older versions
            if (dataElement.TryGetProperty("roles", out var rolesElement))
            {
                // Check if roles contains a "student" property
                if (rolesElement.TryGetProperty("student", out _))
                {
                    role = "student";
                }
                // Check if roles contains a "teacher" property
                else if (rolesElement.TryGetProperty("teacher", out _))
                {
                    role = "teacher";
                }
                // Fallback: check if roles is an array (older format)
                else if (rolesElement.ValueKind == System.Text.Json.JsonValueKind.Array && rolesElement.GetArrayLength() > 0)
                {
                    role = rolesElement[0].TryGetProperty("role", out var roleElement) ? roleElement.GetString() : null;
                }
            }

            _logger.LogDebug("Event {EventId}: Detected role={Role} for user event", evt.Id, role ?? "NULL");

            if (string.IsNullOrEmpty(role))
            {
                _logger.LogDebug("Event {EventId} ({ObjectType}) has no role. Skipping.", evt.Id, objectType);
                if (eventsSummary != null) eventsSummary.EventsSkipped++;
                return;
            }
        }

        // Get the data JSON for deserialization - dataElement IS the actual user/section object
        var innerDataJson = rawDataJson;

        // Handle user events (students/teachers) based on role
        if (objectType == "user" && !string.IsNullOrEmpty(role))
        {
            switch (eventType.ToLower())
            {
                case "created":
                    if (role == "student")
                    {
                        var student = System.Text.Json.JsonSerializer.Deserialize<CleverStudent>(innerDataJson);
                        if (student != null)
                        {
                            result.StudentsProcessed++;
                            bool hasChanges = await UpsertStudentAsync(schoolDb, student, syncId, changeTracker, timeContext, cancellationToken);
                            if (hasChanges)
                            {
                                result.StudentsUpdated++;
                            }
                            if (eventsSummary != null) eventsSummary.StudentCreated++;
                        }
                    }
                    else if (role == "teacher")
                    {
                        var teacher = System.Text.Json.JsonSerializer.Deserialize<CleverTeacher>(innerDataJson);
                        if (teacher != null)
                        {
                            result.TeachersProcessed++;
                            bool hasChanges = await UpsertTeacherAsync(schoolDb, teacher, syncId, changeTracker, timeContext, cancellationToken);
                            if (hasChanges)
                            {
                                result.TeachersUpdated++;
                            }
                            if (eventsSummary != null) eventsSummary.TeacherCreated++;
                        }
                    }
                    break;

                case "updated":
                    if (role == "student")
                    {
                        // DIAGNOSTIC: Log the raw JSON being deserialized
                        _logger.LogInformation(
                            "STUDENT UPDATE DIAGNOSTIC: EventId={EventId}, InnerDataJson={InnerDataJson}",
                            evt.Id, innerDataJson);

                        var student = System.Text.Json.JsonSerializer.Deserialize<CleverStudent>(innerDataJson);
                        if (student != null)
                        {
                            // DIAGNOSTIC: Log the deserialized student data
                            _logger.LogInformation(
                                "STUDENT DESERIALIZED: Id={Id}, FirstName={FirstName}, LastName={LastName}, Name.First={NameFirst}, Name.Last={NameLast}",
                                student.Id, student.Name?.First ?? "NULL", student.Name?.Last ?? "NULL",
                                student.Name?.First ?? "NULL", student.Name?.Last ?? "NULL");

                            result.StudentsProcessed++;
                            bool hasChanges = await UpsertStudentAsync(schoolDb, student, syncId, changeTracker, timeContext, cancellationToken);

                            // DIAGNOSTIC: Log whether changes were detected
                            _logger.LogInformation(
                                "STUDENT UPSERT RESULT: Id={Id}, HasChanges={HasChanges}",
                                student.Id, hasChanges);

                            if (hasChanges)
                            {
                                result.StudentsUpdated++;
                            }
                            if (eventsSummary != null) eventsSummary.StudentUpdated++;
                        }
                        else
                        {
                            _logger.LogWarning("STUDENT DESERIALIZATION FAILED: EventId={EventId}", evt.Id);
                        }
                    }
                    else if (role == "teacher")
                    {
                        var teacher = System.Text.Json.JsonSerializer.Deserialize<CleverTeacher>(innerDataJson);
                        if (teacher != null)
                        {
                            result.TeachersProcessed++;
                            bool hasChanges = await UpsertTeacherAsync(schoolDb, teacher, syncId, changeTracker, timeContext, cancellationToken);
                            if (hasChanges)
                            {
                                result.TeachersUpdated++;
                            }
                            if (eventsSummary != null) eventsSummary.TeacherUpdated++;
                        }
                    }
                    break;

                case "deleted":
                    var now = timeContext.Now;
                    if (role == "student")
                    {
                        var studentId = evt.Data.Id;
                        var student = await schoolDb.Students
                            .FirstOrDefaultAsync(s => s.CleverStudentId == studentId, cancellationToken);
                        if (student != null && student.DeletedAt == null)
                        {
                            student.DeletedAt = now;
                            student.UpdatedAt = now;
                            student.LastSyncedAt = now;
                            await schoolDb.SaveChangesAsync(cancellationToken);
                            result.StudentsDeleted++;
                            _logger.LogDebug("Soft-deleted student {CleverStudentId}", studentId);
                        }
                        if (eventsSummary != null) eventsSummary.StudentDeleted++;
                    }
                    else if (role == "teacher")
                    {
                        var teacherId = evt.Data.Id;
                        var teacher = await schoolDb.Teachers
                            .FirstOrDefaultAsync(t => t.CleverTeacherId == teacherId, cancellationToken);
                        if (teacher != null && teacher.DeletedAt == null)
                        {
                            teacher.DeletedAt = now;
                            teacher.UpdatedAt = now;
                            teacher.LastSyncedAt = now;
                            await schoolDb.SaveChangesAsync(cancellationToken);
                            result.TeachersDeleted++;
                            _logger.LogDebug("Soft-deleted teacher {CleverTeacherId}", teacherId);
                        }
                        if (eventsSummary != null) eventsSummary.TeacherDeleted++;
                    }
                    break;
            }
        }
        // Handle section events
        else if (objectType == "section")
        {
            try
            {
                var cleverSection = System.Text.Json.JsonSerializer.Deserialize<CleverSection>(innerDataJson);
                if (cleverSection != null)
                {
                    result.SectionsProcessed++;

                    var sectionNow = timeContext.Now;
                    switch (eventType.ToLower())
                    {
                        case "created":
                            var existingSectionForCreate = await schoolDb.Sections
                                .FirstOrDefaultAsync(s => s.CleverSectionId == cleverSection.Id, cancellationToken);

                            var sectionEntityCreate = new Section
                            {
                                CleverSectionId = cleverSection.Id,
                                SectionName = cleverSection.Name,
                                Period = cleverSection.Period,
                                Subject = cleverSection.Subject,
                                TermId = cleverSection.TermId,
                                CreatedAt = sectionNow,
                                UpdatedAt = sectionNow,
                                LastEventReceivedAt = sectionNow
                            };

                            if (existingSectionForCreate == null)
                            {
                                schoolDb.Sections.Add(sectionEntityCreate);
                                changeTracker.TrackSectionChange(syncId, null, sectionEntityCreate, "Created");
                                result.SectionsUpdated++;
                            }
                            else
                            {
                                existingSectionForCreate.SectionName = sectionEntityCreate.SectionName;
                                existingSectionForCreate.Period = sectionEntityCreate.Period;
                                existingSectionForCreate.Subject = sectionEntityCreate.Subject;
                                existingSectionForCreate.TermId = sectionEntityCreate.TermId;
                                existingSectionForCreate.UpdatedAt = sectionNow;
                                existingSectionForCreate.LastEventReceivedAt = sectionNow;
                                existingSectionForCreate.DeletedAt = null; // Reactivate if deleted
                                changeTracker.TrackSectionChange(syncId, existingSectionForCreate, sectionEntityCreate, "Updated");
                                result.SectionsUpdated++;
                            }

                            var sectionForCreateAssoc = existingSectionForCreate ?? sectionEntityCreate;
                            if (sectionForCreateAssoc.SectionId > 0)
                            {
                                await SyncSectionTeachersAsync(schoolDb, sectionForCreateAssoc, cleverSection.Teachers, cleverSection.Teacher, timeContext, cancellationToken);
                                await SyncSectionStudentsAsync(schoolDb, sectionForCreateAssoc, cleverSection.Students, timeContext, cancellationToken, workshopLinkedSectionIds, workshopTracker);
                            }

                            await schoolDb.SaveChangesAsync(cancellationToken);
                            if (eventsSummary != null) eventsSummary.SectionCreated++;
                            break;

                        case "updated":
                            var existingSectionForUpdate = await schoolDb.Sections
                                .FirstOrDefaultAsync(s => s.CleverSectionId == cleverSection.Id, cancellationToken);

                            var sectionEntityUpdate = new Section
                            {
                                CleverSectionId = cleverSection.Id,
                                SectionName = cleverSection.Name,
                                Period = cleverSection.Period,
                                Subject = cleverSection.Subject,
                                TermId = cleverSection.TermId,
                                CreatedAt = sectionNow,
                                UpdatedAt = sectionNow,
                                LastEventReceivedAt = sectionNow
                            };

                            if (existingSectionForUpdate == null)
                            {
                                schoolDb.Sections.Add(sectionEntityUpdate);
                                changeTracker.TrackSectionChange(syncId, null, sectionEntityUpdate, "Created");
                                result.SectionsUpdated++;
                            }
                            else
                            {
                                existingSectionForUpdate.SectionName = sectionEntityUpdate.SectionName;
                                existingSectionForUpdate.Period = sectionEntityUpdate.Period;
                                existingSectionForUpdate.Subject = sectionEntityUpdate.Subject;
                                existingSectionForUpdate.TermId = sectionEntityUpdate.TermId;
                                existingSectionForUpdate.UpdatedAt = sectionNow;
                                existingSectionForUpdate.LastEventReceivedAt = sectionNow;
                                existingSectionForUpdate.DeletedAt = null; // Reactivate if deleted
                                changeTracker.TrackSectionChange(syncId, existingSectionForUpdate, sectionEntityUpdate, "Updated");
                                result.SectionsUpdated++;
                            }

                            var sectionForUpdateAssoc = existingSectionForUpdate ?? sectionEntityUpdate;
                            if (sectionForUpdateAssoc.SectionId > 0)
                            {
                                await SyncSectionTeachersAsync(schoolDb, sectionForUpdateAssoc, cleverSection.Teachers, cleverSection.Teacher, timeContext, cancellationToken);
                                await SyncSectionStudentsAsync(schoolDb, sectionForUpdateAssoc, cleverSection.Students, timeContext, cancellationToken, workshopLinkedSectionIds, workshopTracker);
                            }

                            await schoolDb.SaveChangesAsync(cancellationToken);
                            if (eventsSummary != null) eventsSummary.SectionUpdated++;
                            break;

                        case "deleted":
                            var sectionToDelete = await schoolDb.Sections
                                .FirstOrDefaultAsync(s => s.CleverSectionId == cleverSection.Id, cancellationToken);
                            if (sectionToDelete != null && sectionToDelete.DeletedAt == null)
                            {
                                sectionToDelete.DeletedAt = sectionNow;
                                sectionToDelete.UpdatedAt = sectionNow;
                                sectionToDelete.LastEventReceivedAt = sectionNow;
                                await schoolDb.SaveChangesAsync(cancellationToken);
                                changeTracker.TrackSectionChange(syncId, sectionToDelete, sectionToDelete, "Deleted");
                                _logger.LogDebug("Soft-deleted section {CleverSectionId}", cleverSection.Id);
                            }
                            if (eventsSummary != null) eventsSummary.SectionDeleted++;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process section event {EventId}", evt.Id);
                result.SectionsFailed++;
            }
        }
        // Handle term events
        else if (objectType == "term")
        {
            try
            {
                var cleverTerm = System.Text.Json.JsonSerializer.Deserialize<CleverTerm>(innerDataJson);
                if (cleverTerm != null)
                {
                    result.TermsProcessed++;

                    var termNow = timeContext.Now;
                    switch (eventType.ToLower())
                    {
                        case "created":
                            var existingTermForCreate = await schoolDb.Terms
                                .FirstOrDefaultAsync(t => t.CleverTermId == cleverTerm.Id, cancellationToken);

                            // Parse dates from ISO 8601 strings
                            DateTime? startDate = null;
                            DateTime? endDate = null;
                            if (!string.IsNullOrEmpty(cleverTerm.StartDate) && DateTime.TryParse(cleverTerm.StartDate, out var parsedStart))
                                startDate = parsedStart;
                            if (!string.IsNullOrEmpty(cleverTerm.EndDate) && DateTime.TryParse(cleverTerm.EndDate, out var parsedEnd))
                                endDate = parsedEnd;

                            var termEntityCreate = new Term
                            {
                                CleverTermId = cleverTerm.Id,
                                CleverDistrictId = cleverTerm.District,
                                Name = cleverTerm.Name,
                                StartDate = startDate,
                                EndDate = endDate,
                                CreatedAt = termNow,
                                UpdatedAt = termNow,
                                LastEventReceivedAt = termNow,
                                LastSyncedAt = termNow
                            };

                            if (existingTermForCreate == null)
                            {
                                schoolDb.Terms.Add(termEntityCreate);
                                changeTracker.TrackTermChange(syncId, null, termEntityCreate, "Created");
                                result.TermsUpdated++;
                            }
                            else
                            {
                                // Term exists - update it and reactivate if deleted (restoration)
                                existingTermForCreate.Name = termEntityCreate.Name;
                                existingTermForCreate.StartDate = startDate;
                                existingTermForCreate.EndDate = endDate;
                                existingTermForCreate.UpdatedAt = termNow;
                                existingTermForCreate.LastEventReceivedAt = termNow;
                                existingTermForCreate.LastSyncedAt = termNow;
                                if (existingTermForCreate.DeletedAt != null)
                                {
                                    existingTermForCreate.DeletedAt = null; // Reactivate (restoration)
                                    _logger.LogInformation("Term {CleverTermId} ({Name}) was restored via created event",
                                        cleverTerm.Id, cleverTerm.Name);
                                }
                                changeTracker.TrackTermChange(syncId, existingTermForCreate, termEntityCreate, "Updated");
                                result.TermsUpdated++;
                            }

                            await schoolDb.SaveChangesAsync(cancellationToken);
                            if (eventsSummary != null) eventsSummary.TermCreated++;
                            break;

                        case "updated":
                            var existingTermForUpdate = await schoolDb.Terms
                                .FirstOrDefaultAsync(t => t.CleverTermId == cleverTerm.Id, cancellationToken);

                            // Parse dates
                            DateTime? updateStartDate = null;
                            DateTime? updateEndDate = null;
                            if (!string.IsNullOrEmpty(cleverTerm.StartDate) && DateTime.TryParse(cleverTerm.StartDate, out var parsedUpdateStart))
                                updateStartDate = parsedUpdateStart;
                            if (!string.IsNullOrEmpty(cleverTerm.EndDate) && DateTime.TryParse(cleverTerm.EndDate, out var parsedUpdateEnd))
                                updateEndDate = parsedUpdateEnd;

                            var termEntityUpdate = new Term
                            {
                                CleverTermId = cleverTerm.Id,
                                CleverDistrictId = cleverTerm.District,
                                Name = cleverTerm.Name,
                                StartDate = updateStartDate,
                                EndDate = updateEndDate,
                                CreatedAt = termNow,
                                UpdatedAt = termNow,
                                LastEventReceivedAt = termNow,
                                LastSyncedAt = termNow
                            };

                            if (existingTermForUpdate == null)
                            {
                                schoolDb.Terms.Add(termEntityUpdate);
                                changeTracker.TrackTermChange(syncId, null, termEntityUpdate, "Created");
                                result.TermsUpdated++;
                            }
                            else
                            {
                                existingTermForUpdate.Name = termEntityUpdate.Name;
                                existingTermForUpdate.StartDate = updateStartDate;
                                existingTermForUpdate.EndDate = updateEndDate;
                                existingTermForUpdate.UpdatedAt = termNow;
                                existingTermForUpdate.LastEventReceivedAt = termNow;
                                existingTermForUpdate.LastSyncedAt = termNow;
                                if (existingTermForUpdate.DeletedAt != null)
                                {
                                    existingTermForUpdate.DeletedAt = null; // Reactivate (restoration)
                                    _logger.LogInformation("Term {CleverTermId} ({Name}) was restored via updated event",
                                        cleverTerm.Id, cleverTerm.Name);
                                }
                                changeTracker.TrackTermChange(syncId, existingTermForUpdate, termEntityUpdate, "Updated");
                                result.TermsUpdated++;
                            }

                            await schoolDb.SaveChangesAsync(cancellationToken);
                            if (eventsSummary != null) eventsSummary.TermUpdated++;
                            break;

                        case "deleted":
                            var termToDelete = await schoolDb.Terms
                                .FirstOrDefaultAsync(t => t.CleverTermId == cleverTerm.Id, cancellationToken);
                            if (termToDelete != null && termToDelete.DeletedAt == null)
                            {
                                termToDelete.DeletedAt = termNow;
                                termToDelete.UpdatedAt = termNow;
                                termToDelete.LastEventReceivedAt = termNow;
                                await schoolDb.SaveChangesAsync(cancellationToken);
                                result.TermsDeleted++;
                                _logger.LogDebug("Soft-deleted term {CleverTermId}", cleverTerm.Id);
                            }
                            if (eventsSummary != null) eventsSummary.TermDeleted++;
                            break;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process term event {EventId}", evt.Id);
                result.TermsFailed++;
            }
        }
        else
        {
            _logger.LogWarning("Unknown object type: {ObjectType} for event {EventId}", objectType, evt.Id);
            if (eventsSummary != null) eventsSummary.EventsSkipped++;
        }
    }

    /// <summary>
    /// Syncs students from Clever API to school database.
    /// </summary>
    /// <returns>The SyncId from the Student SyncHistory record</returns>
    private async Task<int> SyncStudentsAsync(
        School school,
        SchoolDbContext schoolDb,
        SyncResult result,
        DateTime? lastModified,
        ISchoolTimeContext timeContext,
        IProgress<SyncProgress>? progress,
        int startPercent,
        int endPercent,
        WorkshopSyncTracker? workshopTracker,
        CancellationToken cancellationToken)
    {
        var syncHistory = new SyncHistory
        {
            SchoolId = school.SchoolId,
            EntityType = "Student",
            SyncType = result.SyncType,
            SyncStartTime = DateTime.UtcNow,
            Status = "InProgress",
            LastSyncTimestamp = lastModified
        };

        // Save SyncHistory immediately to get the SyncId for change tracking
        _sessionDb.SyncHistory.Add(syncHistory);
        await _sessionDb.SaveChangesAsync(cancellationToken);

        // Create change tracker for detailed change logging
        var changeTracker = new ChangeTracker(_sessionDb, _logger);

        try
        {
            // Fetch students from Clever API
            var cleverStudents = await _cleverClient.GetStudentsAsync(school.CleverSchoolId, lastModified, cancellationToken);

            _logger.LogDebug("Fetched {Count} students from Clever API for school {SchoolId}",
                cleverStudents.Length, school.SchoolId);

            // Upsert students
            int totalStudents = cleverStudents.Length;
            int percentRange = endPercent - startPercent;

            for (int i = 0; i < cleverStudents.Length; i++)
            {
                var cleverStudent = cleverStudents[i];
                try
                {
                    result.StudentsProcessed++; // Count every student examined
                    // Pass workshopTracker to track grade changes
                    bool hasChanges = await UpsertStudentAsync(schoolDb, cleverStudent, syncHistory.SyncId, changeTracker, timeContext, cancellationToken, workshopTracker);
                    if (hasChanges)
                    {
                        result.StudentsUpdated++; // Only count if actually changed
                    }

                    // Report progress every 10 students or at the end
                    if ((i + 1) % 10 == 0 || i == totalStudents - 1)
                    {
                        int currentPercent = startPercent + (percentRange * (i + 1) / totalStudents);
                        progress?.Report(new SyncProgress
                        {
                            PercentComplete = currentPercent,
                            CurrentOperation = $"Processing {result.StudentsProcessed}/{totalStudents} students, {result.StudentsUpdated} updated",
                            StudentsUpdated = result.StudentsUpdated,
                            StudentsProcessed = result.StudentsProcessed,
                            StudentsFailed = result.StudentsFailed
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upsert student {CleverStudentId} for school {SchoolId}",
                        cleverStudent.Id, school.SchoolId);
                    result.StudentsFailed++;
                }
            }

            // Save all tracked changes
            await changeTracker.SaveChangesAsync(cancellationToken);

            syncHistory.Status = "Success";
            syncHistory.RecordsProcessed = result.StudentsProcessed;
            syncHistory.RecordsUpdated = result.StudentsUpdated;
            syncHistory.RecordsFailed = result.StudentsFailed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync students for school {SchoolId}", school.SchoolId);
            syncHistory.Status = "Failed";
            syncHistory.ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            syncHistory.SyncEndTime = DateTime.UtcNow;
            await _sessionDb.SaveChangesAsync(cancellationToken);
        }

        return syncHistory.SyncId;
    }

    /// <summary>
    /// Syncs teachers from Clever API to school database.
    /// </summary>
    private async Task SyncTeachersAsync(
        School school,
        SchoolDbContext schoolDb,
        SyncResult result,
        DateTime? lastModified,
        ISchoolTimeContext timeContext,
        IProgress<SyncProgress>? progress,
        int startPercent,
        int endPercent,
        CancellationToken cancellationToken)
    {
        var syncHistory = new SyncHistory
        {
            SchoolId = school.SchoolId,
            EntityType = "Teacher",
            SyncType = result.SyncType,
            SyncStartTime = DateTime.UtcNow,
            Status = "InProgress",
            LastSyncTimestamp = lastModified
        };

        // Save SyncHistory immediately to get the SyncId for change tracking
        _sessionDb.SyncHistory.Add(syncHistory);
        await _sessionDb.SaveChangesAsync(cancellationToken);

        // Create change tracker for detailed change logging
        var changeTracker = new ChangeTracker(_sessionDb, _logger);

        try
        {
            // Fetch teachers from Clever API
            var cleverTeachers = await _cleverClient.GetTeachersAsync(school.CleverSchoolId, lastModified, cancellationToken);

            _logger.LogDebug("Fetched {Count} teachers from Clever API for school {SchoolId}",
                cleverTeachers.Length, school.SchoolId);

            // Upsert teachers
            int totalTeachers = cleverTeachers.Length;
            int percentRange = endPercent - startPercent;

            for (int i = 0; i < cleverTeachers.Length; i++)
            {
                var cleverTeacher = cleverTeachers[i];
                try
                {
                    result.TeachersProcessed++; // Count every teacher examined
                    bool hasChanges = await UpsertTeacherAsync(schoolDb, cleverTeacher, syncHistory.SyncId, changeTracker, timeContext, cancellationToken);
                    if (hasChanges)
                    {
                        result.TeachersUpdated++; // Only count if actually changed
                    }

                    // Report progress every 10 teachers or at the end
                    if ((i + 1) % 10 == 0 || i == totalTeachers - 1)
                    {
                        int currentPercent = startPercent + (percentRange * (i + 1) / totalTeachers);
                        progress?.Report(new SyncProgress
                        {
                            PercentComplete = currentPercent,
                            CurrentOperation = $"Processing {result.TeachersProcessed}/{totalTeachers} teachers, {result.TeachersUpdated} updated",
                            StudentsUpdated = result.StudentsUpdated,
                            StudentsProcessed = result.StudentsProcessed,
                            StudentsFailed = result.StudentsFailed,
                            TeachersUpdated = result.TeachersUpdated,
                            TeachersProcessed = result.TeachersProcessed,
                            TeachersFailed = result.TeachersFailed
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upsert teacher {CleverTeacherId} for school {SchoolId}",
                        cleverTeacher.Id, school.SchoolId);
                    result.TeachersFailed++;
                }
            }

            // Save all tracked changes
            await changeTracker.SaveChangesAsync(cancellationToken);

            syncHistory.Status = "Success";
            syncHistory.RecordsProcessed = result.TeachersProcessed;
            syncHistory.RecordsUpdated = result.TeachersUpdated;
            syncHistory.RecordsFailed = result.TeachersFailed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync teachers for school {SchoolId}", school.SchoolId);
            syncHistory.Status = "Failed";
            syncHistory.ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            syncHistory.SyncEndTime = DateTime.UtcNow;
            await _sessionDb.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Upserts a student record (insert if new, update if exists).
    /// Source: FR-014 - Upsert logic for students
    /// Syncs fields: FirstName, MiddleName, LastName, Grade, GradeLevel, StudentNumber, StateStudentId
    /// </summary>
    /// <param name="schoolDb">School database context</param>
    /// <param name="cleverStudent">Student data from Clever API</param>
    /// <param name="syncId">Current sync ID for change tracking</param>
    /// <param name="changeTracker">Change tracker for audit logging</param>
    /// <param name="timeContext">Time context for timestamps</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="workshopTracker">Optional tracker for workshop-relevant changes (grade changes)</param>
    private async Task<bool> UpsertStudentAsync(
        SchoolDbContext schoolDb,
        CleverStudent cleverStudent,
        int syncId,
        ChangeTracker changeTracker,
        ISchoolTimeContext timeContext,
        CancellationToken cancellationToken,
        WorkshopSyncTracker? workshopTracker = null)
    {
        var student = await schoolDb.Students
            .FirstOrDefaultAsync(s => s.CleverStudentId == cleverStudent.Id, cancellationToken);

        var now = timeContext.Now;
        bool hasChanges = false;

        // Parse grade from Clever API (string) to int for database
        int? parsedGrade = _validationService.ParseGrade(cleverStudent.Grade);

        // Get additional fields from Clever
        var middleName = cleverStudent.Name.Middle;
        var stateStudentId = cleverStudent.SisId ?? string.Empty;
        var gradeLevel = cleverStudent.Grade ?? "0"; // String representation of grade

        if (student == null)
        {
            // Insert new student
            student = new Student
            {
                CleverStudentId = cleverStudent.Id,
                FirstName = cleverStudent.Name.First,
                MiddleName = middleName,
                LastName = cleverStudent.Name.Last,
                Grade = parsedGrade,
                GradeLevel = gradeLevel,
                StudentNumber = cleverStudent.StudentNumber ?? string.Empty,
                StateStudentId = stateStudentId,
                CreatedAt = now,
                UpdatedAt = now,
                LastSyncedAt = now
            };
            schoolDb.Students.Add(student);
            hasChanges = true;

            // Track creation
            changeTracker.TrackStudentChange(syncId, null, student, "Created");
        }
        else
        {
            // Always update LastSyncedAt to track that we saw this record
            student.LastSyncedAt = now;

            // Check if any fields actually changed (treating null and empty string as equivalent)
            var firstNameChanged = !_validationService.StringsEqual(student.FirstName, cleverStudent.Name.First);
            var middleNameChanged = !_validationService.StringsEqual(student.MiddleName, middleName);
            var lastNameChanged = !_validationService.StringsEqual(student.LastName, cleverStudent.Name.Last);
            var gradeChanged = student.Grade != parsedGrade;
            var gradeLevelChanged = !_validationService.StringsEqual(student.GradeLevel, gradeLevel);
            var studentNumberChanged = !_validationService.StringsEqual(student.StudentNumber, cleverStudent.StudentNumber);
            var stateStudentIdChanged = !_validationService.StringsEqual(student.StateStudentId, stateStudentId);
            var wasDeleted = student.DeletedAt != null;

            // DIAGNOSTIC: Log comparison details for LastName
            _logger.LogInformation(
                "UPSERT COMPARISON: CleverId={CleverId}, DB.LastName=[{DbLastName}], Clever.Name.Last=[{CleverLastName}], LastNameChanged={Changed}",
                cleverStudent.Id,
                student.LastName ?? "NULL",
                cleverStudent.Name?.Last ?? "NULL",
                lastNameChanged);

            if (firstNameChanged || middleNameChanged || lastNameChanged || gradeChanged ||
                gradeLevelChanged || studentNumberChanged || stateStudentIdChanged || wasDeleted)
            {
                // Track grade changes for workshop sync
                if (gradeChanged && workshopTracker != null)
                {
                    workshopTracker.HasGradeChanges = true;
                    workshopTracker.StudentGradesChanged++;
                    _logger.LogDebug(
                        "Student {StudentId} ({Name}) grade changed from {OldGrade} to {NewGrade}",
                        student.StudentId, $"{student.FirstName} {student.LastName}",
                        student.Grade, parsedGrade);
                }

                // Capture old state before updating
                var oldStudent = new Student
                {
                    CleverStudentId = student.CleverStudentId,
                    FirstName = student.FirstName,
                    MiddleName = student.MiddleName,
                    LastName = student.LastName,
                    Grade = student.Grade,
                    GradeLevel = student.GradeLevel,
                    StudentNumber = student.StudentNumber,
                    StateStudentId = student.StateStudentId
                };

                // Update existing student only if changed
                student.FirstName = cleverStudent.Name.First;
                student.MiddleName = middleName;
                student.LastName = cleverStudent.Name.Last;
                student.Grade = parsedGrade;
                student.GradeLevel = gradeLevel;
                student.StudentNumber = cleverStudent.StudentNumber ?? string.Empty;
                student.StateStudentId = stateStudentId;
                student.UpdatedAt = now;
                student.DeletedAt = null; // Reactivate if it was deleted
                hasChanges = true;

                // Track update with old and new values
                changeTracker.TrackStudentChange(syncId, oldStudent, student, "Updated");
            }
        }

        if (hasChanges)
        {
            await schoolDb.SaveChangesAsync(cancellationToken);
        }

        return hasChanges;
    }

    /// <summary>
    /// Upserts a teacher record (insert if new, update if exists).
    /// Source: FR-014 - Upsert logic for teachers
    /// Syncs fields: FirstName, LastName, FullName, StaffNumber, TeacherNumber, UserName
    /// Note: Does NOT sync app-specific fields like PriorityId, Administrator, RoomId, etc.
    /// </summary>
    private async Task<bool> UpsertTeacherAsync(
        SchoolDbContext schoolDb,
        CleverTeacher cleverTeacher,
        int syncId,
        ChangeTracker changeTracker,
        ISchoolTimeContext timeContext,
        CancellationToken cancellationToken)
    {
        var teacher = await schoolDb.Teachers
            .FirstOrDefaultAsync(t => t.CleverTeacherId == cleverTeacher.Id, cancellationToken);

        var now = timeContext.Now;
        bool hasChanges = false;

        // Get fields from Clever API
        var staffNumber = cleverTeacher.SisId ?? string.Empty;
        var teacherNumber = cleverTeacher.TeacherNumber;
        var userName = cleverTeacher.Roles?.Teacher?.Credentials?.DistrictUsername;
        var fullName = $"{cleverTeacher.Name.First} {cleverTeacher.Name.Last}".Trim();

        if (teacher == null)
        {
            // Insert new teacher
            teacher = new Teacher
            {
                CleverTeacherId = cleverTeacher.Id,
                FirstName = cleverTeacher.Name.First,
                LastName = cleverTeacher.Name.Last,
                FullName = fullName,
                StaffNumber = staffNumber,
                TeacherNumber = teacherNumber,
                UserName = userName,
                CreatedAt = now,
                UpdatedAt = now,
                LastSyncedAt = now
            };
            schoolDb.Teachers.Add(teacher);
            hasChanges = true;

            // Track creation
            changeTracker.TrackTeacherChange(syncId, null, teacher, "Created");
        }
        else
        {
            // Always update LastSyncedAt to track that we saw this record
            teacher.LastSyncedAt = now;

            // Check if any fields actually changed (treating null and empty string as equivalent)
            var firstNameChanged = !_validationService.StringsEqual(teacher.FirstName, cleverTeacher.Name.First);
            var lastNameChanged = !_validationService.StringsEqual(teacher.LastName, cleverTeacher.Name.Last);
            var fullNameChanged = !_validationService.StringsEqual(teacher.FullName, fullName);
            var staffNumberChanged = !_validationService.StringsEqual(teacher.StaffNumber, staffNumber);
            var teacherNumberChanged = !_validationService.StringsEqual(teacher.TeacherNumber, teacherNumber);
            var userNameChanged = !_validationService.StringsEqual(teacher.UserName, userName);
            var wasDeleted = teacher.DeletedAt != null;

            if (firstNameChanged || lastNameChanged || fullNameChanged ||
                staffNumberChanged || teacherNumberChanged || userNameChanged || wasDeleted)
            {
                // Capture old state before updating
                var oldTeacher = new Teacher
                {
                    CleverTeacherId = teacher.CleverTeacherId,
                    FirstName = teacher.FirstName,
                    LastName = teacher.LastName,
                    FullName = teacher.FullName,
                    StaffNumber = teacher.StaffNumber,
                    TeacherNumber = teacher.TeacherNumber,
                    UserName = teacher.UserName
                };

                // Update existing teacher only if changed
                teacher.FirstName = cleverTeacher.Name.First;
                teacher.LastName = cleverTeacher.Name.Last;
                teacher.FullName = fullName;
                teacher.StaffNumber = staffNumber;
                teacher.TeacherNumber = teacherNumber;
                teacher.UserName = userName;
                teacher.UpdatedAt = now;
                teacher.DeletedAt = null; // Reactivate if it was deleted
                hasChanges = true;

                // Track update with old and new values
                changeTracker.TrackTeacherChange(syncId, oldTeacher, teacher, "Updated");
            }
        }

        if (hasChanges)
        {
            await schoolDb.SaveChangesAsync(cancellationToken);
        }

        return hasChanges;
    }

    /// <inheritdoc />
    public async Task<SyncHistory[]> GetSyncHistoryAsync(
        int schoolId,
        string? entityType = null,
        int limit = 10,
        CancellationToken cancellationToken = default)
    {
        var query = _sessionDb.SyncHistory
            .Where(h => h.SchoolId == schoolId);

        if (!string.IsNullOrEmpty(entityType))
        {
            query = query.Where(h => h.EntityType == entityType);
        }

        var history = await query
            .OrderByDescending(h => h.SyncStartTime)
            .Take(limit)
            .ToArrayAsync(cancellationToken);

        return history;
    }

    /// <summary>
    /// Syncs sections from Clever API to school database, including teacher and student associations.
    /// Includes change detection and workshop-linked section warnings.
    /// </summary>
    /// <returns>The SyncId from the Section SyncHistory record for workshop sync</returns>
    private async Task<int> SyncSectionsAsync(
        School school,
        SchoolDbContext schoolDb,
        SyncResult result,
        ISchoolTimeContext timeContext,
        IProgress<SyncProgress>? progress,
        int startPercent,
        int endPercent,
        WorkshopSyncTracker workshopTracker,
        CancellationToken cancellationToken)
    {
        var syncHistory = new SyncHistory
        {
            SchoolId = school.SchoolId,
            EntityType = "Section",
            SyncType = result.SyncType,
            SyncStartTime = DateTime.UtcNow,
            Status = "InProgress",
            RecordsProcessed = 0
        };

        _sessionDb.SyncHistory.Add(syncHistory);
        await _sessionDb.SaveChangesAsync(cancellationToken);

        var changeTracker = new ChangeTracker(_sessionDb, _logger);

        try
        {
            var cleverSections = await _cleverClient.GetSectionsAsync(school.CleverSchoolId, cancellationToken);

            _logger.LogDebug("Fetched {Count} sections from Clever API for school {SchoolId}",
                cleverSections.Length, school.SchoolId);

            // Pre-load all workshop-linked sections for efficient lookup
            var workshopLinkedSectionIds = await _workshopSyncService.GetWorkshopLinkedSectionIdsAsync(schoolDb, cancellationToken);

            _logger.LogDebug("Found {Count} sections linked to workshops", workshopLinkedSectionIds.Count);

            int totalSections = cleverSections.Length;
            int percentRange = endPercent - startPercent;

            // Track which sections from Clever we've seen (for detecting deletions)
            var cleverSectionIds = new HashSet<string>(cleverSections.Select(s => s.Id));

            for (int i = 0; i < cleverSections.Length; i++)
            {
                var cleverSection = cleverSections[i];
                try
                {
                    result.SectionsProcessed++;

                    var existingSection = await schoolDb.Sections
                        .FirstOrDefaultAsync(s => s.CleverSectionId == cleverSection.Id, cancellationToken);

                    if (existingSection == null)
                    {
                        // New section - create it
                        var newSection = new Section
                        {
                            CleverSectionId = cleverSection.Id,
                            SectionName = cleverSection.Name,
                            Period = cleverSection.Period,
                            Subject = cleverSection.Subject,
                            TermId = cleverSection.TermId,
                            CreatedAt = timeContext.Now,
                            UpdatedAt = timeContext.Now,
                            LastEventReceivedAt = timeContext.Now
                        };
                        schoolDb.Sections.Add(newSection);
                        await schoolDb.SaveChangesAsync(cancellationToken);
                        changeTracker.TrackSectionChange(syncHistory.SyncId, null, newSection, "Created");
                        result.SectionsUpdated++;

                        // Handle associations for new section
                        await SyncSectionTeachersAsync(schoolDb, newSection, cleverSection.Teachers, cleverSection.Teacher, timeContext, cancellationToken);
                        await SyncSectionStudentsAsync(schoolDb, newSection, cleverSection.Students, timeContext, cancellationToken, workshopLinkedSectionIds, workshopTracker);
                    }
                    else
                    {
                        // Existing section - check for changes
                        bool hasChanges = await UpsertSectionAsync(
                            schoolDb, existingSection, cleverSection,
                            syncHistory.SyncId, changeTracker, workshopLinkedSectionIds, result, timeContext,
                            cancellationToken);

                        if (hasChanges)
                        {
                            result.SectionsUpdated++;
                        }

                        // Handle associations (always sync to ensure accuracy)
                        await SyncSectionTeachersAsync(schoolDb, existingSection, cleverSection.Teachers, cleverSection.Teacher, timeContext, cancellationToken);
                        await SyncSectionStudentsAsync(schoolDb, existingSection, cleverSection.Students, timeContext, cancellationToken, workshopLinkedSectionIds, workshopTracker);
                    }

                    if ((i + 1) % 50 == 0 || i == totalSections - 1)
                    {
                        int currentPercent = startPercent + (percentRange * (i + 1) / totalSections);
                        progress?.Report(new SyncProgress
                        {
                            PercentComplete = currentPercent,
                            CurrentOperation = $"Processing {result.SectionsProcessed}/{totalSections} sections, {result.SectionsUpdated} updated",
                            WarningsGenerated = result.WarningsGenerated
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upsert section {CleverSectionId} for school {SchoolId}",
                        cleverSection.Id, school.SchoolId);
                    result.SectionsFailed++;
                }
            }

            // Check for sections that exist in DB but not in Clever (potential deletions)
            await CheckForDeletedSectionsAsync(
                schoolDb, cleverSectionIds, workshopLinkedSectionIds,
                syncHistory.SyncId, result, timeContext, cancellationToken);

            await changeTracker.SaveChangesAsync(cancellationToken);
            await schoolDb.SaveChangesAsync(cancellationToken);

            syncHistory.Status = "Success";
            syncHistory.RecordsProcessed = result.SectionsProcessed;
            syncHistory.RecordsUpdated = result.SectionsUpdated;
            syncHistory.RecordsFailed = result.SectionsFailed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync sections for school {SchoolId}", school.SchoolId);
            syncHistory.Status = "Failed";
            syncHistory.ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            syncHistory.SyncEndTime = DateTime.UtcNow;
            await _sessionDb.SaveChangesAsync(cancellationToken);
        }

        return syncHistory.SyncId;
    }

    /// <summary>
    /// Upserts a section with proper change detection.
    /// Generates warnings for workshop-linked sections that are being modified.
    /// Syncs fields: SectionName, Period, Subject, TermId
    /// </summary>
    private async Task<bool> UpsertSectionAsync(
        SchoolDbContext schoolDb,
        Section existingSection,
        CleverApi.Models.CleverSection cleverSection,
        int syncId,
        ChangeTracker changeTracker,
        HashSet<int> workshopLinkedSectionIds,
        SyncResult result,
        ISchoolTimeContext timeContext,
        CancellationToken cancellationToken)
    {
        var now = timeContext.Now;
        bool hasChanges = false;

        // Check for field changes
        var sectionNameChanged = !_validationService.StringsEqual(existingSection.SectionName, cleverSection.Name);
        var periodChanged = !_validationService.StringsEqual(existingSection.Period, cleverSection.Period);
        var subjectChanged = !_validationService.StringsEqual(existingSection.Subject, cleverSection.Subject);
        var termIdChanged = !_validationService.StringsEqual(existingSection.TermId, cleverSection.TermId);
        var wasDeleted = existingSection.DeletedAt != null;

        // Check if this is a workshop-linked section with changes
        bool isWorkshopLinked = workshopLinkedSectionIds.Contains(existingSection.SectionId);

        if (sectionNameChanged || periodChanged || subjectChanged || termIdChanged || wasDeleted)
        {
            hasChanges = true;

            // If workshop-linked and has significant changes, generate warning
            if (isWorkshopLinked && sectionNameChanged)
            {
                await GenerateWorkshopWarningAsync(
                    schoolDb, existingSection, syncId, "SectionModified",
                    $"Section '{existingSection.SectionName}' (ID: {existingSection.SectionId}) linked to workshops has been modified. " +
                    $"Name changed to: '{cleverSection.Name}'",
                    result, cancellationToken);
            }

            // Capture old state for change tracking
            var oldSection = new Section
            {
                SectionId = existingSection.SectionId,
                CleverSectionId = existingSection.CleverSectionId,
                SectionName = existingSection.SectionName,
                Period = existingSection.Period,
                Subject = existingSection.Subject,
                TermId = existingSection.TermId
            };

            // Update section
            existingSection.SectionName = cleverSection.Name;
            existingSection.Period = cleverSection.Period;
            existingSection.Subject = cleverSection.Subject;
            existingSection.TermId = cleverSection.TermId;
            existingSection.DeletedAt = null; // Reactivate if was deleted
            existingSection.UpdatedAt = now;
            existingSection.LastEventReceivedAt = now;

            changeTracker.TrackSectionChange(syncId, oldSection, existingSection, "Updated");
            await schoolDb.SaveChangesAsync(cancellationToken);
        }

        return hasChanges;
    }

    /// <summary>
    /// Checks for sections that exist in the database but are no longer in Clever.
    /// Generates warnings for workshop-linked sections that would be deactivated/deleted.
    /// </summary>
    private async Task CheckForDeletedSectionsAsync(
        SchoolDbContext schoolDb,
        HashSet<string> cleverSectionIds,
        HashSet<int> workshopLinkedSectionIds,
        int syncId,
        SyncResult result,
        ISchoolTimeContext timeContext,
        CancellationToken cancellationToken)
    {
        // Find active sections in DB that are not in Clever's response
        var sectionsInDb = await schoolDb.Sections
            .Where(s => s.DeletedAt == null)
            .Select(s => new { s.SectionId, s.CleverSectionId, s.SectionName })
            .ToListAsync(cancellationToken);

        var missingSections = sectionsInDb
            .Where(s => !cleverSectionIds.Contains(s.CleverSectionId))
            .ToList();

        var now = timeContext.Now;
        foreach (var missingSection in missingSections)
        {
            if (workshopLinkedSectionIds.Contains(missingSection.SectionId))
            {
                // Workshop-linked section would be deleted - generate warning and skip
                var section = await schoolDb.Sections.FindAsync(new object[] { missingSection.SectionId }, cancellationToken);
                if (section != null)
                {
                    await GenerateWorkshopWarningAsync(
                        schoolDb, section, syncId, "SectionDeleted",
                        $"Section '{missingSection.SectionName}' (ID: {missingSection.SectionId}) is linked to workshops but no longer exists in Clever. " +
                        "The section was NOT deactivated. Manual review required.",
                        result, cancellationToken);

                    result.SectionsSkippedWorkshopLinked++;
                    _logger.LogWarning(
                        "Section {SectionId} ({SectionName}) is linked to workshops but missing from Clever. Skipping deactivation.",
                        missingSection.SectionId, missingSection.SectionName);
                }
            }
            else
            {
                // Not workshop-linked - safe to soft-delete
                var section = await schoolDb.Sections.FindAsync(new object[] { missingSection.SectionId }, cancellationToken);
                if (section != null)
                {
                    section.DeletedAt = now;
                    section.UpdatedAt = now;
                    _logger.LogInformation(
                        "Soft-deleting section {SectionId} ({SectionName}) - no longer in Clever",
                        missingSection.SectionId, missingSection.SectionName);
                }
            }
        }

        await schoolDb.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Generates a warning for workshop-linked sections that are being modified or deleted.
    /// </summary>
    private async Task GenerateWorkshopWarningAsync(
        SchoolDbContext schoolDb,
        Section section,
        int syncId,
        string warningType,
        string message,
        SyncResult result,
        CancellationToken cancellationToken)
    {
        // Get workshop names for this section
        var affectedWorkshops = await schoolDb.WorkshopSections
            .Where(ws => ws.SectionId == section.SectionId)
            .Include(ws => ws.Workshop)
            .Select(ws => new { ws.Workshop!.WorkshopId, ws.Workshop.WorkshopName })
            .ToListAsync(cancellationToken);

        var workshopNames = affectedWorkshops.Select(w => w.WorkshopName).ToList();
        var workshopJson = System.Text.Json.JsonSerializer.Serialize(
            affectedWorkshops.Select(w => new { w.WorkshopId, w.WorkshopName }));

        var warning = new Database.SessionDb.Entities.SyncWarning
        {
            SyncId = syncId,
            WarningType = warningType,
            EntityType = "Section",
            EntityId = section.SectionId,
            CleverEntityId = section.CleverSectionId,
            EntityName = section.SectionName ?? $"Section {section.CleverSectionId}",
            Message = message,
            AffectedWorkshops = workshopJson,
            AffectedWorkshopCount = affectedWorkshops.Count,
            IsAcknowledged = false,
            CreatedAt = DateTime.UtcNow
        };

        _sessionDb.SyncWarnings.Add(warning);
        await _sessionDb.SaveChangesAsync(cancellationToken);

        // Add to result for immediate visibility
        var sectionDisplayName = section.SectionName ?? $"Section {section.CleverSectionId}";
        result.WarningsGenerated++;
        result.Warnings.Add(new SyncWarningInfo
        {
            WarningType = warningType,
            EntityType = "Section",
            EntityId = section.SectionId,
            EntityName = sectionDisplayName,
            Message = message,
            AffectedWorkshopNames = workshopNames
        });

        _logger.LogWarning(
            "SYNC WARNING [{WarningType}]: Section {SectionId} ({SectionName}) - {Message}. Affects {Count} workshop(s): {Workshops}",
            warningType, section.SectionId, sectionDisplayName, message, affectedWorkshops.Count,
            string.Join(", ", workshopNames));
    }

    /// <summary>
    /// Gets a human-readable summary of section changes.
    /// </summary>
    private static string GetChangeSummary(Section existing, CleverApi.Models.CleverSection clever, bool sectionNameChanged)
    {
        var changes = new List<string>();
        if (sectionNameChanged)
            changes.Add($"SectionName: '{existing.SectionName}' → '{clever.Name}'");
        return string.Join(", ", changes);
    }

    /// <summary>
    /// Syncs teacher-section associations for a given section.
    /// </summary>
    private async Task SyncSectionTeachersAsync(
        SchoolDbContext schoolDb,
        Section section,
        string[] cleverTeacherIds,
        string? primaryTeacherId,
        ISchoolTimeContext timeContext,
        CancellationToken cancellationToken)
    {
        // Remove existing associations
        var existingAssociations = await schoolDb.TeacherSections
            .Where(ts => ts.SectionId == section.SectionId)
            .ToListAsync(cancellationToken);
        schoolDb.TeacherSections.RemoveRange(existingAssociations);

        // Add new associations
        var now = timeContext.Now;
        foreach (var cleverTeacherId in cleverTeacherIds ?? Array.Empty<string>())
        {
            var teacher = await schoolDb.Teachers
                .FirstOrDefaultAsync(t => t.CleverTeacherId == cleverTeacherId, cancellationToken);

            if (teacher != null)
            {
                var isPrimary = cleverTeacherId == primaryTeacherId;
                var association = new TeacherSection
                {
                    TeacherId = teacher.TeacherId,
                    SectionId = section.SectionId,
                    IsPrimary = isPrimary,
                    CreatedAt = now
                };
                schoolDb.TeacherSections.Add(association);
            }
            else
            {
                _logger.LogWarning("Teacher {CleverTeacherId} not found for section {SectionId}",
                    cleverTeacherId, section.CleverSectionId);
            }
        }

        await schoolDb.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Syncs student-section enrollments for a given section.
    /// Optionally tracks changes to workshop-linked sections for triggering workshop sync.
    /// </summary>
    /// <param name="schoolDb">School database context</param>
    /// <param name="section">The section to sync enrollments for</param>
    /// <param name="cleverStudentIds">Student IDs from Clever API</param>
    /// <param name="timeContext">Time context for timestamps</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <param name="workshopLinkedSectionIds">Optional set of section IDs linked to workshops</param>
    /// <param name="workshopTracker">Optional tracker for workshop-relevant changes</param>
    private async Task SyncSectionStudentsAsync(
        SchoolDbContext schoolDb,
        Section section,
        string[] cleverStudentIds,
        ISchoolTimeContext timeContext,
        CancellationToken cancellationToken,
        HashSet<int>? workshopLinkedSectionIds = null,
        WorkshopSyncTracker? workshopTracker = null)
    {
        var now = timeContext.Now;
        var incomingStudentIds = cleverStudentIds ?? Array.Empty<string>();

        // Check if this section is linked to workshops
        var isWorkshopLinkedSection = workshopLinkedSectionIds?.Contains(section.SectionId) ?? false;

        // Get existing enrollments for this section
        var existingEnrollments = await schoolDb.StudentSections
            .Where(ss => ss.SectionId == section.SectionId)
            .Include(ss => ss.Student)
            .ToListAsync(cancellationToken);

        // Build lookup of existing enrollments by CleverStudentId
        var existingByCleverStudentId = existingEnrollments
            .Where(e => e.Student != null)
            .ToDictionary(e => e.Student.CleverStudentId, e => e);

        // Track which enrollments to keep
        var enrollmentsToKeep = new HashSet<int>();
        int studentsAdded = 0;

        // Process incoming enrollments
        foreach (var cleverStudentId in incomingStudentIds)
        {
            var student = await schoolDb.Students
                .FirstOrDefaultAsync(s => s.CleverStudentId == cleverStudentId, cancellationToken);

            if (student != null)
            {
                if (existingByCleverStudentId.TryGetValue(cleverStudentId, out var existingEnrollment))
                {
                    // Existing enrollment - keep it
                    enrollmentsToKeep.Add(existingEnrollment.StudentSectionId);
                }
                else
                {
                    // Add new enrollment
                    var enrollment = new StudentSection
                    {
                        StudentId = student.StudentId,
                        SectionId = section.SectionId,
                        OffCampus = false,
                        CreatedAt = now
                    };
                    schoolDb.StudentSections.Add(enrollment);
                    studentsAdded++;
                }
            }
            else
            {
                _logger.LogWarning("Student {CleverStudentId} not found for section {SectionId}",
                    cleverStudentId, section.CleverSectionId);
            }
        }

        // Remove enrollments no longer in Clever
        var enrollmentsToRemove = existingEnrollments
            .Where(e => !enrollmentsToKeep.Contains(e.StudentSectionId))
            .ToList();
        int studentsRemoved = enrollmentsToRemove.Count;
        schoolDb.StudentSections.RemoveRange(enrollmentsToRemove);

        await schoolDb.SaveChangesAsync(cancellationToken);

        // Track workshop-relevant changes if this is a workshop-linked section
        if (isWorkshopLinkedSection && workshopTracker != null && (studentsAdded > 0 || studentsRemoved > 0))
        {
            workshopTracker.HasWorkshopEnrollmentChanges = true;
            workshopTracker.StudentsAddedToWorkshopSections += studentsAdded;
            workshopTracker.StudentsRemovedFromWorkshopSections += studentsRemoved;

            _logger.LogDebug(
                "Workshop-linked section {SectionId} ({SectionName}): {Added} students added, {Removed} students removed",
                section.SectionId, section.SectionName, studentsAdded, studentsRemoved);
        }
    }

    /// <summary>
    /// Syncs terms from Clever API to school database.
    /// Terms are district-level entities in Clever but stored per-school for data isolation.
    /// </summary>
    /// <remarks>
    /// <para>Terms endpoint is district-level (/terms), not filtered by school.</para>
    /// <para>Supports soft-delete and restoration: if a deleted term reappears in Clever, it is reactivated.</para>
    /// </remarks>
    private async Task SyncTermsAsync(
        School school,
        SchoolDbContext schoolDb,
        SyncResult result,
        DateTime syncStartTime,
        ISchoolTimeContext timeContext,
        IProgress<SyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        var syncHistory = new SyncHistory
        {
            SchoolId = school.SchoolId,
            EntityType = "Term",
            SyncType = result.SyncType,
            SyncStartTime = DateTime.UtcNow,
            Status = "InProgress",
            LastSyncTimestamp = syncStartTime
        };

        _sessionDb.SyncHistory.Add(syncHistory);
        await _sessionDb.SaveChangesAsync(cancellationToken);

        var changeTracker = new ChangeTracker(_sessionDb, _logger);

        try
        {
            // Fetch terms from Clever API (district-level endpoint)
            var cleverTerms = await _cleverClient.GetTermsAsync(school.CleverSchoolId, cancellationToken);

            _logger.LogDebug("Fetched {Count} terms from Clever API for school {SchoolId}",
                cleverTerms.Length, school.SchoolId);

            int totalTerms = cleverTerms.Length;

            for (int i = 0; i < cleverTerms.Length; i++)
            {
                var cleverTerm = cleverTerms[i];
                try
                {
                    result.TermsProcessed++;
                    bool hasChanges = await UpsertTermAsync(schoolDb, cleverTerm, syncHistory.SyncId, changeTracker, syncStartTime, cancellationToken);
                    if (hasChanges)
                    {
                        result.TermsUpdated++;
                    }

                    // Report progress every 10 terms or at the end
                    if ((i + 1) % 10 == 0 || i == totalTerms - 1)
                    {
                        progress?.Report(new SyncProgress
                        {
                            CurrentOperation = $"Processing {result.TermsProcessed}/{totalTerms} terms, {result.TermsUpdated} updated",
                            TermsProcessed = result.TermsProcessed,
                            TermsUpdated = result.TermsUpdated,
                            TermsFailed = result.TermsFailed
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upsert term {CleverTermId} for school {SchoolId}",
                        cleverTerm.Id, school.SchoolId);
                    result.TermsFailed++;
                }
            }

            // Save all tracked changes
            await changeTracker.SaveChangesAsync(cancellationToken);

            syncHistory.Status = "Success";
            syncHistory.RecordsProcessed = result.TermsProcessed;
            syncHistory.RecordsUpdated = result.TermsUpdated;
            syncHistory.RecordsFailed = result.TermsFailed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync terms for school {SchoolId}", school.SchoolId);
            syncHistory.Status = "Failed";
            syncHistory.ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            syncHistory.SyncEndTime = DateTime.UtcNow;
            await _sessionDb.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>
    /// Upserts a term record (insert if new, update if exists).
    /// Supports restoration: if a deleted term reappears in Clever, clears DeletedAt.
    /// </summary>
    /// <param name="schoolDb">School database context</param>
    /// <param name="cleverTerm">Term data from Clever API</param>
    /// <param name="syncId">Current sync ID for change tracking</param>
    /// <param name="changeTracker">Change tracker for audit logging</param>
    /// <param name="syncStartTime">Sync start time for LastSyncedAt</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if changes were made, false otherwise</returns>
    private async Task<bool> UpsertTermAsync(
        SchoolDbContext schoolDb,
        CleverApi.Models.CleverTerm cleverTerm,
        int syncId,
        ChangeTracker changeTracker,
        DateTime syncStartTime,
        CancellationToken cancellationToken)
    {
        var term = await schoolDb.Terms
            .FirstOrDefaultAsync(t => t.CleverTermId == cleverTerm.Id, cancellationToken);

        var now = syncStartTime;
        bool hasChanges = false;

        // Parse dates from ISO 8601 strings
        DateTime? startDate = null;
        DateTime? endDate = null;

        if (!string.IsNullOrEmpty(cleverTerm.StartDate) && DateTime.TryParse(cleverTerm.StartDate, out var parsedStart))
        {
            startDate = parsedStart;
        }
        if (!string.IsNullOrEmpty(cleverTerm.EndDate) && DateTime.TryParse(cleverTerm.EndDate, out var parsedEnd))
        {
            endDate = parsedEnd;
        }

        if (term == null)
        {
            // Insert new term
            term = new Term
            {
                CleverTermId = cleverTerm.Id,
                CleverDistrictId = cleverTerm.District,
                Name = cleverTerm.Name,
                StartDate = startDate,
                EndDate = endDate,
                CreatedAt = now,
                UpdatedAt = now,
                LastSyncedAt = now
            };
            schoolDb.Terms.Add(term);
            hasChanges = true;

            // Track creation
            changeTracker.TrackTermChange(syncId, null, term, "Created");
        }
        else
        {
            // Always update LastSyncedAt to track that we saw this record
            term.LastSyncedAt = now;

            // Check if any fields actually changed
            var nameChanged = !_validationService.StringsEqual(term.Name, cleverTerm.Name);
            var startDateChanged = term.StartDate != startDate;
            var endDateChanged = term.EndDate != endDate;
            var wasDeleted = term.DeletedAt != null;

            if (nameChanged || startDateChanged || endDateChanged || wasDeleted)
            {
                // Capture old state before updating
                var oldTerm = new Term
                {
                    CleverTermId = term.CleverTermId,
                    CleverDistrictId = term.CleverDistrictId,
                    Name = term.Name,
                    StartDate = term.StartDate,
                    EndDate = term.EndDate
                };

                // Update existing term
                term.Name = cleverTerm.Name;
                term.StartDate = startDate;
                term.EndDate = endDate;
                term.UpdatedAt = now;
                term.DeletedAt = null; // Reactivate if it was deleted (restoration)
                hasChanges = true;

                // Track update with old and new values
                changeTracker.TrackTermChange(syncId, oldTerm, term, "Updated");

                if (wasDeleted)
                {
                    _logger.LogInformation("Term {CleverTermId} ({Name}) was restored (previously deleted)",
                        term.CleverTermId, term.Name);
                }
            }
        }

        if (hasChanges)
        {
            await schoolDb.SaveChangesAsync(cancellationToken);
        }

        return hasChanges;
    }
}
