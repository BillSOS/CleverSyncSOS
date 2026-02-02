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
using CleverSyncSOS.Core.Sync.Handlers;
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

    // Entity sync handlers (extracted for better maintainability)
    private readonly StudentSyncHandler _studentHandler;
    private readonly TeacherSyncHandler _teacherHandler;
    private readonly SectionSyncHandler _sectionHandler;
    private readonly TermSyncHandler _termHandler;
    private readonly CleverEventProcessor _eventProcessor;

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
    ///   <item><description><b>Entity handlers</b>: Dedicated handlers for each entity type (Student, Teacher, Section, Term)</description></item>
    /// </list>
    /// </remarks>
    public SyncService(
        ICleverApiClient cleverClient,
        SessionDbContext sessionDb,
        SchoolDatabaseConnectionFactory schoolDbFactory,
        ILocalTimeService localTimeService,
        IWorkshopSyncService workshopSyncService,
        ISyncValidationService validationService,
        IServiceProvider serviceProvider,
        ILogger<SyncService> logger,
        StudentSyncHandler studentHandler,
        TeacherSyncHandler teacherHandler,
        SectionSyncHandler sectionHandler,
        TermSyncHandler termHandler,
        CleverEventProcessor eventProcessor)
    {
        _cleverClient = cleverClient;
        _sessionDb = sessionDb;
        _schoolDbFactory = schoolDbFactory;
        _localTimeService = localTimeService;
        _workshopSyncService = workshopSyncService;
        _validationService = validationService;
        _serviceProvider = serviceProvider;
        _logger = logger;
        _studentHandler = studentHandler;
        _teacherHandler = teacherHandler;
        _sectionHandler = sectionHandler;
        _termHandler = termHandler;
        _eventProcessor = eventProcessor;
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
            var workshopTracker = new Workshop.WorkshopSyncTracker();

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
    /// <returns>The SyncId to use for workshop sync (Section SyncId)</returns>
    private async Task<int> PerformFullSyncAsync(
        School school,
        SchoolDbContext schoolDb,
        SyncResult result,
        ISchoolTimeContext timeContext,
        IProgress<SyncProgress>? progress,
        Workshop.WorkshopSyncTracker workshopTracker,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Performing FULL sync for school {SchoolId}", school.SchoolId);

        // Capture the sync start time in local time for LastSyncedAt timestamps
        var syncStartTime = timeContext.Now;

        // Clear EF change tracker so UpsertAsync fetches fresh data from DB
        schoolDb.ChangeTracker.Clear();

        // Pre-load workshop-linked sections for efficient lookup
        var workshopLinkedSectionIds = await _workshopSyncService.GetWorkshopLinkedSectionIdsAsync(schoolDb, cancellationToken);

        // Create shared sync context for all handlers
        var context = new SyncContext
        {
            School = school,
            SchoolDb = schoolDb,
            SessionDb = _sessionDb,
            Result = result,
            TimeContext = timeContext,
            Progress = progress,
            CancellationToken = cancellationToken,
            SyncStartTime = syncStartTime,
            LastModified = syncStartTime,
            WorkshopTracker = workshopTracker,
            WorkshopLinkedSectionIds = workshopLinkedSectionIds
        };

        progress?.Report(new SyncProgress
        {
            PercentComplete = 20,
            CurrentOperation = "Fetching students from Clever API..."
        });

        // Sync students using handler
        int studentSyncId = await _studentHandler.SyncAllAsync(context, 20, 60);
        context.StudentSyncId = studentSyncId;

        progress?.Report(new SyncProgress
        {
            PercentComplete = 60,
            CurrentOperation = "Fetching teachers from Clever API...",
            StudentsProcessed = result.StudentsProcessed,
            StudentsFailed = result.StudentsFailed
        });

        // Sync teachers using handler
        await _teacherHandler.SyncAllAsync(context, 60, 80);

        progress?.Report(new SyncProgress
        {
            PercentComplete = 80,
            CurrentOperation = "Syncing sections from Clever API...",
            StudentsProcessed = result.StudentsProcessed,
            TeachersProcessed = result.TeachersProcessed
        });

        // Sync sections using handler (includes student enrollments)
        int sectionSyncId = await _sectionHandler.SyncAllAsync(context, 80, 88);
        context.SectionSyncId = sectionSyncId;

        progress?.Report(new SyncProgress
        {
            PercentComplete = 88,
            CurrentOperation = "Syncing terms from Clever API...",
            StudentsProcessed = result.StudentsProcessed,
            TeachersProcessed = result.TeachersProcessed,
            SectionsProcessed = result.SectionsProcessed
        });

        // Sync terms using handler
        await _termHandler.SyncAllAsync(context, 88, 92);

        // Detect and soft-delete orphaned entities using handlers
        var changeTracker = new ChangeTracker(_sessionDb, _logger);
        await _studentHandler.DetectOrphansAsync(context, studentSyncId, changeTracker);
        await _teacherHandler.DetectOrphansAsync(context, studentSyncId, changeTracker);
        await _termHandler.DetectOrphansAsync(context, studentSyncId, changeTracker);
        await changeTracker.SaveChangesAsync(cancellationToken);

        // Count orphaned entities for reporting
        result.StudentsDeleted = await schoolDb.Students
            .CountAsync(s => s.DeletedAt >= syncStartTime && s.UpdatedAt >= syncStartTime, cancellationToken);
        result.TeachersDeleted = await schoolDb.Teachers
            .CountAsync(t => t.DeletedAt >= syncStartTime && t.UpdatedAt >= syncStartTime, cancellationToken);
        result.TermsDeleted = await schoolDb.Terms
            .CountAsync(t => t.DeletedAt >= syncStartTime && t.UpdatedAt >= syncStartTime && !t.IsManual, cancellationToken);

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
        Workshop.WorkshopSyncTracker workshopTracker,
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

            // Pre-load workshop-linked sections
            var fallbackWorkshopSectionIds = await _workshopSyncService.GetWorkshopLinkedSectionIdsAsync(schoolDb, cancellationToken);

            // Create sync context for handlers
            var fallbackContext = new SyncContext
            {
                School = school,
                SchoolDb = schoolDb,
                SessionDb = _sessionDb,
                Result = result,
                TimeContext = timeContext,
                Progress = progress,
                CancellationToken = cancellationToken,
                SyncStartTime = timeContext.Now,
                LastModified = lastModified,
                WorkshopTracker = workshopTracker,
                WorkshopLinkedSectionIds = fallbackWorkshopSectionIds
            };

            // Use handlers for data API fallback
            int studentSyncId = await _studentHandler.SyncAllAsync(fallbackContext, 10, 60);
            await _teacherHandler.SyncAllAsync(fallbackContext, 60, 100);

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

        // Create sync context for event processor
        var eventContext = new SyncContext
        {
            School = school,
            SchoolDb = schoolDb,
            SessionDb = _sessionDb,
            Result = result,
            TimeContext = timeContext,
            Progress = progress,
            CancellationToken = cancellationToken,
            SyncStartTime = timeContext.Now,
            LastModified = lastModified,
            WorkshopTracker = workshopTracker,
            WorkshopLinkedSectionIds = workshopLinkedSectionIds
        };

        // Process events in chronological order using event processor
        string? latestEventId = null;
        DateTime? latestEventTimestamp = null;
        int eventsProcessed = 0;
        foreach (var evt in events)
        {
            try
            {
                await _eventProcessor.ProcessEventAsync(eventContext, evt, eventSyncHistory.SyncId, changeTracker);
                latestEventId = evt.Id;
                if (evt.Created.HasValue)
                {
                    latestEventTimestamp = evt.Created.Value;
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
                result.StudentsFailed++;
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
}
