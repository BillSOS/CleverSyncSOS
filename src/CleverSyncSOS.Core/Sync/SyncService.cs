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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Core.Sync;

/// <summary>
/// Synchronization service that orchestrates data sync from Clever API to databases.
/// Source: SpecKit/Plans/001-clever-api-auth/plan.md (Stage 2)
/// Implements dual-database architecture with SessionDb for orchestration and per-school databases for data.
/// </summary>
public class SyncService : ISyncService
{
    private readonly ICleverApiClient _cleverClient;
    private readonly SessionDbContext _sessionDb;
    private readonly SchoolDatabaseConnectionFactory _schoolDbFactory;
    private readonly ILocalTimeService _localTimeService;
    private readonly ILogger<SyncService> _logger;

    public SyncService(
        ICleverApiClient cleverClient,
        SessionDbContext sessionDb,
        SchoolDatabaseConnectionFactory schoolDbFactory,
        ILocalTimeService localTimeService,
        ILogger<SyncService> logger)
    {
        _cleverClient = cleverClient;
        _sessionDb = sessionDb;
        _schoolDbFactory = schoolDbFactory;
        _localTimeService = localTimeService;
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

            // Step 4e-4g: Sync students and teachers
            if (isFullSync)
            {
                await PerformFullSyncAsync(school, schoolDb, result, timeContext, progress, cancellationToken);
            }
            else
            {
                await PerformIncrementalSyncAsync(school, schoolDb, result, lastSync!.LastSyncTimestamp, timeContext, progress, cancellationToken);
            }

            // Reset RequiresFullSync flag after successful full sync
            if (isFullSync && school.RequiresFullSync)
            {
                school.RequiresFullSync = false;
                school.UpdatedAt = DateTime.UtcNow;
                await _sessionDb.SaveChangesAsync(cancellationToken);
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
    private async Task PerformFullSyncAsync(
        School school,
        SchoolDbContext schoolDb,
        SyncResult result,
        ISchoolTimeContext timeContext,
        IProgress<SyncProgress>? progress,
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
        await SyncStudentsAsync(school, schoolDb, result, syncStartTime, timeContext, progress, 20, 60, cancellationToken);

        progress?.Report(new SyncProgress
        {
            PercentComplete = 60,
            CurrentOperation = "Fetching teachers from Clever API...",
            StudentsProcessed = result.StudentsProcessed,
            StudentsFailed = result.StudentsFailed
        });

        // Pass syncStartTime (local time) so next incremental sync has a valid timestamp
        await SyncTeachersAsync(school, schoolDb, result, syncStartTime, timeContext, progress, 60, 90, cancellationToken);

        // Step 3: Soft-delete students and teachers not seen in this sync
        // Records not seen will have LastSyncedAt < syncStartTime
        var orphanedStudents = await schoolDb.Students
            .Where(s => s.LastSyncedAt < syncStartTime && s.DeletedAt == null)
            .ToListAsync(cancellationToken);
        var orphanedTeachers = await schoolDb.Teachers
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

        result.StudentsDeleted = orphanedStudents.Count;
        result.TeachersDeleted = orphanedTeachers.Count;

        await schoolDb.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Full sync complete for school {SchoolId}: Soft-deleted {StudentsDeleted} students, {TeachersDeleted} teachers",
            school.SchoolId, result.StudentsDeleted, result.TeachersDeleted);

        // Establish baseline for future incremental syncs
        try
        {
            var latestEventId = await _cleverClient.GetLatestEventIdAsync(cancellationToken);

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
    }

    /// <summary>
    /// Performs an incremental sync using Clever's Events API.
    /// Source: FR-024 - Incremental sync using Events API
    /// Documentation: https://dev.clever.com/docs/events-api
    /// </summary>
    private async Task PerformIncrementalSyncAsync(
        School school,
        SchoolDbContext schoolDb,
        SyncResult result,
        DateTime? lastModified,
        ISchoolTimeContext timeContext,
        IProgress<SyncProgress>? progress,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("Performing INCREMENTAL sync for school {SchoolId} using Events API",
            school.SchoolId);

        // Get the last event ID from the most recent successful sync
        var lastEventId = await _sessionDb.SyncHistory
            .Where(h => h.SchoolId == school.SchoolId && h.Status == "Success" && h.LastEventId != null)
            .OrderByDescending(h => h.SyncEndTime)
            .Select(h => h.LastEventId)
            .FirstOrDefaultAsync(cancellationToken);

        if (string.IsNullOrEmpty(lastEventId))
        {
            // Events API not available - use data API with change detection
            _logger.LogInformation("Events API not available for school {SchoolId}. Using data API with change detection.", school.SchoolId);

            // Fetch all students and teachers without marking them inactive
            // The UpsertStudentAsync method will detect actual changes
            await SyncStudentsAsync(school, schoolDb, result, lastModified, timeContext, progress, 10, 60, cancellationToken);
            await SyncTeachersAsync(school, schoolDb, result, lastModified, timeContext, progress, 60, 100, cancellationToken);

            _logger.LogInformation("Incremental sync complete using data API. Students: {Processed} processed, {Updated} updated",
                result.StudentsProcessed, result.StudentsUpdated);
            return;
        }

        _logger.LogInformation("Fetching events after event ID: {EventId}", lastEventId);

        // Fetch events for this school since the last event ID
        var events = await _cleverClient.GetEventsAsync(
            startingAfter: lastEventId,
            schoolId: school.CleverSchoolId,
            recordType: "users", // Students and teachers are both "users" in Clever
            limit: 1000,
            cancellationToken: cancellationToken);

        _logger.LogInformation("Retrieved {Count} events for school {SchoolId}", events.Length, school.SchoolId);

        // Initialize EventsSummary for tracking
        result.EventsSummary = new EventsSummary();

        if (events.Length == 0)
        {
            _logger.LogInformation("No new events to process for school {SchoolId}", school.SchoolId);

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

            return;
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

        // Process events in chronological order (they're already sorted oldest to newest)
        string? latestEventId = null;
        int eventsProcessed = 0;
        foreach (var evt in events)
        {
            try
            {
                await ProcessEventAsync(school, schoolDb, evt, result, eventSyncHistory.SyncId, changeTracker, timeContext, cancellationToken);
                latestEventId = evt.Id; // Track the latest event ID we've processed
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
        if (!string.IsNullOrEmpty(latestEventId))
        {
            eventSyncHistory.SyncEndTime = DateTime.UtcNow;
            eventSyncHistory.Status = "Success";
            eventSyncHistory.RecordsProcessed = events.Length;
            eventSyncHistory.LastEventId = latestEventId;

            // Serialize and save EventsSummary
            if (result.EventsSummary != null)
            {
                eventSyncHistory.EventsSummaryJson = System.Text.Json.JsonSerializer.Serialize(result.EventsSummary);
            }

            await _sessionDb.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Incremental sync complete. Last event ID: {EventId}. {EventsSummary}",
                latestEventId, result.EventsSummary?.ToDisplayString() ?? "No events");
        }
    }

    /// <summary>
    /// Processes a single event from Clever's Events API.
    /// Handles created, updated, and deleted events for students and teachers.
    /// </summary>
    private async Task ProcessEventAsync(
        School school,
        SchoolDbContext schoolDb,
        CleverEvent evt,
        SyncResult result,
        int syncId,
        ChangeTracker changeTracker,
        ISchoolTimeContext timeContext,
        CancellationToken cancellationToken)
    {
        var objectType = evt.Data.Object;
        var eventType = evt.Type;
        var eventsSummary = result.EventsSummary;

        // Track total events processed
        if (eventsSummary != null)
        {
            eventsSummary.TotalEventsProcessed++;
        }

        _logger.LogDebug("Processing event {EventId}: {EventType} {ObjectType} ({ObjectId})",
            evt.Id, eventType, objectType, evt.Data.Id);

        // Determine if this is a student or teacher based on role
        // We need to deserialize the raw data to check the role
        if (evt.Data.RawData == null || evt.Data.RawData.Value.ValueKind == System.Text.Json.JsonValueKind.Undefined)
        {
            _logger.LogWarning("Event {EventId} has no data. Skipping.", evt.Id);
            return;
        }

        // Parse the role from the raw data (RawData is already a JsonElement)
        var rawDataJson = evt.Data.RawData.Value.GetRawText();
        var dataWrapper = System.Text.Json.JsonSerializer.Deserialize<CleverDataWrapper<System.Text.Json.JsonElement>>(rawDataJson);

        if (dataWrapper?.Data == null)
        {
            _logger.LogWarning("Event {EventId} has invalid data structure. Skipping.", evt.Id);
            return;
        }

        var role = dataWrapper.Data.TryGetProperty("roles", out var rolesElement) &&
                   rolesElement.GetArrayLength() > 0
            ? rolesElement[0].TryGetProperty("role", out var roleElement) ? roleElement.GetString() : null
            : null;

        if (string.IsNullOrEmpty(role))
        {
            _logger.LogDebug("Event {EventId} has no role. Skipping.", evt.Id);
            return;
        }

        // Handle user events (students/teachers) based on role
        if (objectType == "user" && !string.IsNullOrEmpty(role))
        {
            switch (eventType.ToLower())
            {
                case "created":
                    if (role == "student")
                    {
                        var student = System.Text.Json.JsonSerializer.Deserialize<CleverStudent>(rawDataJson);
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
                        var teacher = System.Text.Json.JsonSerializer.Deserialize<CleverTeacher>(rawDataJson);
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
                        var student = System.Text.Json.JsonSerializer.Deserialize<CleverStudent>(rawDataJson);
                        if (student != null)
                        {
                            result.StudentsProcessed++;
                            bool hasChanges = await UpsertStudentAsync(schoolDb, student, syncId, changeTracker, timeContext, cancellationToken);
                            if (hasChanges)
                            {
                                result.StudentsUpdated++;
                            }
                            if (eventsSummary != null) eventsSummary.StudentUpdated++;
                        }
                    }
                    else if (role == "teacher")
                    {
                        var teacher = System.Text.Json.JsonSerializer.Deserialize<CleverTeacher>(rawDataJson);
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
        // Course events are ignored - courses are district-level and not synced
        // Handle section events
        else if (objectType == "section")
        {
            try
            {
                var cleverSection = System.Text.Json.JsonSerializer.Deserialize<CleverSection>(rawDataJson);
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
                                CleverCourseId = cleverSection.Course,
                                SectionNumber = cleverSection.SectionNumber ?? string.Empty,
                                SectionName = cleverSection.Name,
                                Period = cleverSection.Period,
                                Subject = cleverSection.Subject,
                                CreatedAt = sectionNow,
                                UpdatedAt = sectionNow,
                                LastSyncedAt = sectionNow
                            };

                            if (existingSectionForCreate == null)
                            {
                                schoolDb.Sections.Add(sectionEntityCreate);
                                changeTracker.TrackSectionChange(syncId, null, sectionEntityCreate, "Created");
                                result.SectionsUpdated++;
                            }
                            else
                            {
                                existingSectionForCreate.SectionNumber = sectionEntityCreate.SectionNumber;
                                existingSectionForCreate.SectionName = sectionEntityCreate.SectionName;
                                existingSectionForCreate.Period = sectionEntityCreate.Period;
                                existingSectionForCreate.Subject = sectionEntityCreate.Subject;
                                existingSectionForCreate.CleverCourseId = sectionEntityCreate.CleverCourseId;
                                existingSectionForCreate.UpdatedAt = sectionNow;
                                existingSectionForCreate.LastSyncedAt = sectionNow;
                                existingSectionForCreate.DeletedAt = null; // Reactivate if deleted
                                changeTracker.TrackSectionChange(syncId, existingSectionForCreate, sectionEntityCreate, "Updated");
                                result.SectionsUpdated++;
                            }

                            var sectionForCreateAssoc = existingSectionForCreate ?? sectionEntityCreate;
                            if (sectionForCreateAssoc.SectionId > 0)
                            {
                                await SyncSectionTeachersAsync(schoolDb, sectionForCreateAssoc, cleverSection.Teachers, cleverSection.Teacher, timeContext, cancellationToken);
                                await SyncSectionStudentsAsync(schoolDb, sectionForCreateAssoc, cleverSection.Students, timeContext, cancellationToken);
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
                                CleverCourseId = cleverSection.Course,
                                SectionNumber = cleverSection.SectionNumber ?? string.Empty,
                                SectionName = cleverSection.Name,
                                Period = cleverSection.Period,
                                Subject = cleverSection.Subject,
                                CreatedAt = sectionNow,
                                UpdatedAt = sectionNow,
                                LastSyncedAt = sectionNow
                            };

                            if (existingSectionForUpdate == null)
                            {
                                schoolDb.Sections.Add(sectionEntityUpdate);
                                changeTracker.TrackSectionChange(syncId, null, sectionEntityUpdate, "Created");
                                result.SectionsUpdated++;
                            }
                            else
                            {
                                existingSectionForUpdate.SectionNumber = sectionEntityUpdate.SectionNumber;
                                existingSectionForUpdate.SectionName = sectionEntityUpdate.SectionName;
                                existingSectionForUpdate.Period = sectionEntityUpdate.Period;
                                existingSectionForUpdate.Subject = sectionEntityUpdate.Subject;
                                existingSectionForUpdate.CleverCourseId = sectionEntityUpdate.CleverCourseId;
                                existingSectionForUpdate.UpdatedAt = sectionNow;
                                existingSectionForUpdate.LastSyncedAt = sectionNow;
                                existingSectionForUpdate.DeletedAt = null; // Reactivate if deleted
                                changeTracker.TrackSectionChange(syncId, existingSectionForUpdate, sectionEntityUpdate, "Updated");
                                result.SectionsUpdated++;
                            }

                            var sectionForUpdateAssoc = existingSectionForUpdate ?? sectionEntityUpdate;
                            if (sectionForUpdateAssoc.SectionId > 0)
                            {
                                await SyncSectionTeachersAsync(schoolDb, sectionForUpdateAssoc, cleverSection.Teachers, cleverSection.Teacher, timeContext, cancellationToken);
                                await SyncSectionStudentsAsync(schoolDb, sectionForUpdateAssoc, cleverSection.Students, timeContext, cancellationToken);
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
                                sectionToDelete.LastSyncedAt = sectionNow;
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
        else
        {
            _logger.LogWarning("Unknown object type: {ObjectType} for event {EventId}", objectType, evt.Id);
            if (eventsSummary != null) eventsSummary.EventsSkipped++;
        }
    }

    /// <summary>
    /// Syncs students from Clever API to school database.
    /// </summary>
    private async Task SyncStudentsAsync(
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
                    bool hasChanges = await UpsertStudentAsync(schoolDb, cleverStudent, syncHistory.SyncId, changeTracker, timeContext, cancellationToken);
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
    private async Task<bool> UpsertStudentAsync(
        SchoolDbContext schoolDb,
        CleverStudent cleverStudent,
        int syncId,
        ChangeTracker changeTracker,
        ISchoolTimeContext timeContext,
        CancellationToken cancellationToken)
    {
        var student = await schoolDb.Students
            .FirstOrDefaultAsync(s => s.CleverStudentId == cleverStudent.Id, cancellationToken);

        var now = timeContext.Now;
        bool hasChanges = false;

        // Parse grade from Clever API (string) to int for database
        int? parsedGrade = ParseGrade(cleverStudent.Grade);

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
            var firstNameChanged = !StringsEqual(student.FirstName, cleverStudent.Name.First);
            var middleNameChanged = !StringsEqual(student.MiddleName, middleName);
            var lastNameChanged = !StringsEqual(student.LastName, cleverStudent.Name.Last);
            var gradeChanged = student.Grade != parsedGrade;
            var gradeLevelChanged = !StringsEqual(student.GradeLevel, gradeLevel);
            var studentNumberChanged = !StringsEqual(student.StudentNumber, cleverStudent.StudentNumber);
            var stateStudentIdChanged = !StringsEqual(student.StateStudentId, stateStudentId);
            var wasDeleted = student.DeletedAt != null;

            if (firstNameChanged || middleNameChanged || lastNameChanged || gradeChanged ||
                gradeLevelChanged || studentNumberChanged || stateStudentIdChanged || wasDeleted)
            {
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
    /// Parses a grade string from Clever API to an integer.
    /// Handles various formats including numeric grades (1-12), "K", "PreK", etc.
    /// </summary>
    private static int? ParseGrade(string? gradeString)
    {
        if (string.IsNullOrWhiteSpace(gradeString))
            return null;

        // Try direct integer parse first
        if (int.TryParse(gradeString, out int grade))
            return grade;

        // Handle common string grade values
        var normalizedGrade = gradeString.Trim().ToUpperInvariant();

        return normalizedGrade switch
        {
            "K" or "KINDERGARTEN" => 0,
            "PK" or "PRE-K" or "PREK" or "PRE-KINDERGARTEN" => -1,
            "TK" or "TRANSITIONAL KINDERGARTEN" => -1,
            _ => null // Unknown grade format
        };
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
            var firstNameChanged = !StringsEqual(teacher.FirstName, cleverTeacher.Name.First);
            var lastNameChanged = !StringsEqual(teacher.LastName, cleverTeacher.Name.Last);
            var fullNameChanged = !StringsEqual(teacher.FullName, fullName);
            var staffNumberChanged = !StringsEqual(teacher.StaffNumber, staffNumber);
            var teacherNumberChanged = !StringsEqual(teacher.TeacherNumber, teacherNumber);
            var userNameChanged = !StringsEqual(teacher.UserName, userName);
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
    /// Note: Courses are NOT synced - CleverCourseId is stored on sections for reference only.
    /// Includes change detection and workshop-linked section warnings.
    /// </summary>
    private async Task SyncSectionsAsync(
        School school,
        SchoolDbContext schoolDb,
        SyncResult result,
        ISchoolTimeContext timeContext,
        IProgress<SyncProgress>? progress,
        int startPercent,
        int endPercent,
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
            var workshopLinkedSectionIds = await schoolDb.WorkshopSections
                .Select(ws => ws.SectionId)
                .Distinct()
                .ToHashSetAsync(cancellationToken);

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

                    // Note: Courses are NOT synced - CleverCourseId is stored for reference only
                    // Courses are district-level in Clever and syncing them would bring irrelevant data from other schools

                    var existingSection = await schoolDb.Sections
                        .FirstOrDefaultAsync(s => s.CleverSectionId == cleverSection.Id, cancellationToken);

                    if (existingSection == null)
                    {
                        // New section - create it
                        var newSection = new Section
                        {
                            CleverSectionId = cleverSection.Id,
                            CleverCourseId = cleverSection.Course, // Store for reference only, no FK
                            SectionNumber = cleverSection.SectionNumber ?? string.Empty,
                            SectionName = cleverSection.Name,
                            Period = cleverSection.Period,
                            Subject = cleverSection.Subject,
                            CreatedAt = timeContext.Now,
                            UpdatedAt = timeContext.Now,
                            LastSyncedAt = timeContext.Now
                        };
                        schoolDb.Sections.Add(newSection);
                        await schoolDb.SaveChangesAsync(cancellationToken);
                        changeTracker.TrackSectionChange(syncHistory.SyncId, null, newSection, "Created");
                        result.SectionsUpdated++;

                        // Handle associations for new section
                        await SyncSectionTeachersAsync(schoolDb, newSection, cleverSection.Teachers, cleverSection.Teacher, timeContext, cancellationToken);
                        await SyncSectionStudentsAsync(schoolDb, newSection, cleverSection.Students, timeContext, cancellationToken);
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
                        await SyncSectionStudentsAsync(schoolDb, existingSection, cleverSection.Students, timeContext, cancellationToken);
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
    }

    /// <summary>
    /// Upserts a section with proper change detection.
    /// Generates warnings for workshop-linked sections that are being modified.
    /// Syncs fields: SectionNumber, SectionName, Period, Subject, CleverCourseId
    /// Note: CourseId FK no longer exists - courses are not synced
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

        // Get fields from Clever
        var sectionNumber = cleverSection.SectionNumber ?? string.Empty;
        var cleverCourseId = cleverSection.Course; // Nullable - stored for reference only

        // Check for field changes
        var sectionNumberChanged = !StringsEqual(existingSection.SectionNumber, sectionNumber);
        var sectionNameChanged = !StringsEqual(existingSection.SectionName, cleverSection.Name);
        var periodChanged = !StringsEqual(existingSection.Period, cleverSection.Period);
        var subjectChanged = !StringsEqual(existingSection.Subject, cleverSection.Subject);
        var cleverCourseIdChanged = !StringsEqual(existingSection.CleverCourseId, cleverCourseId);
        var wasDeleted = existingSection.DeletedAt != null;

        // Check if this is a workshop-linked section with changes
        bool isWorkshopLinked = workshopLinkedSectionIds.Contains(existingSection.SectionId);

        if (sectionNumberChanged || sectionNameChanged || periodChanged ||
            subjectChanged || cleverCourseIdChanged || wasDeleted)
        {
            hasChanges = true;

            // If workshop-linked and has significant changes, generate warning
            if (isWorkshopLinked && (sectionNumberChanged || sectionNameChanged))
            {
                await GenerateWorkshopWarningAsync(
                    schoolDb, existingSection, syncId, "SectionModified",
                    $"Section '{existingSection.SectionName}' (ID: {existingSection.SectionId}) linked to workshops has been modified. " +
                    $"Changes: {GetChangeSummary(existingSection, cleverSection, sectionNumberChanged, sectionNameChanged)}",
                    result, cancellationToken);
            }

            // Capture old state for change tracking
            var oldSection = new Section
            {
                SectionId = existingSection.SectionId,
                CleverSectionId = existingSection.CleverSectionId,
                CleverCourseId = existingSection.CleverCourseId,
                SectionNumber = existingSection.SectionNumber,
                SectionName = existingSection.SectionName,
                Period = existingSection.Period,
                Subject = existingSection.Subject
            };

            // Update section
            existingSection.SectionNumber = sectionNumber;
            existingSection.SectionName = cleverSection.Name;
            existingSection.Period = cleverSection.Period;
            existingSection.Subject = cleverSection.Subject;
            existingSection.CleverCourseId = cleverCourseId;
            existingSection.DeletedAt = null; // Reactivate if was deleted
            existingSection.UpdatedAt = now;
            existingSection.LastSyncedAt = now;

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
            EntityName = section.SectionName ?? $"Section {section.SectionNumber}",
            Message = message,
            AffectedWorkshops = workshopJson,
            AffectedWorkshopCount = affectedWorkshops.Count,
            IsAcknowledged = false,
            CreatedAt = DateTime.UtcNow
        };

        _sessionDb.SyncWarnings.Add(warning);
        await _sessionDb.SaveChangesAsync(cancellationToken);

        // Add to result for immediate visibility
        var sectionDisplayName = section.SectionName ?? $"Section {section.SectionNumber}";
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
    private static string GetChangeSummary(Section existing, CleverApi.Models.CleverSection clever, bool sectionNumberChanged, bool sectionNameChanged)
    {
        var changes = new List<string>();
        if (sectionNumberChanged)
            changes.Add($"SectionNumber: '{existing.SectionNumber}' → '{clever.SectionNumber}'");
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
    /// </summary>
    private async Task SyncSectionStudentsAsync(
        SchoolDbContext schoolDb,
        Section section,
        string[] cleverStudentIds,
        ISchoolTimeContext timeContext,
        CancellationToken cancellationToken)
    {
        var now = timeContext.Now;
        var incomingStudentIds = cleverStudentIds ?? Array.Empty<string>();

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
        schoolDb.StudentSections.RemoveRange(enrollmentsToRemove);

        await schoolDb.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Compares two strings treating null, empty string, and whitespace as equivalent.
    /// </summary>
    private static bool StringsEqual(string? a, string? b)
    {
        // Treat null, empty string, and whitespace as equivalent
        var normalizedA = string.IsNullOrWhiteSpace(a) ? null : a.Trim();
        var normalizedB = string.IsNullOrWhiteSpace(b) ? null : b.Trim();
        return normalizedA == normalizedB;
    }
}
