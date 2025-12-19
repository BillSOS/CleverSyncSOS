using CleverSyncSOS.AdminPortal.Hubs;
using CleverSyncSOS.AdminPortal.Models.ViewModels;
using CleverSyncSOS.Core.Database.SessionDb;
using CleverSyncSOS.Core.Sync;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SyncProgress = CleverSyncSOS.Core.Sync.SyncProgress;

namespace CleverSyncSOS.AdminPortal.Services;

/// <summary>
/// Implementation of ISyncCoordinatorService for orchestrating sync operations with SignalR progress.
/// Based on manual-sync-feature-plan.md
/// </summary>
public class SyncCoordinatorService : ISyncCoordinatorService
{
    private readonly ISyncService _syncService;
    private readonly IHubContext<SyncProgressHub> _hubContext;
    private readonly IAuditLogService _auditLogService;
    private readonly ILogger<SyncCoordinatorService> _logger;
    private readonly SessionDbContext _dbContext;
    private readonly ActiveSyncTracker _activeSyncTracker;

    public SyncCoordinatorService(
        ISyncService syncService,
        IHubContext<SyncProgressHub> hubContext,
        IAuditLogService auditLogService,
        ILogger<SyncCoordinatorService> logger,
        SessionDbContext dbContext,
        ActiveSyncTracker activeSyncTracker)
    {
        _syncService = syncService;
        _hubContext = hubContext;
        _auditLogService = auditLogService;
        _logger = logger;
        _dbContext = dbContext;
        _activeSyncTracker = activeSyncTracker;
    }

    public Task<bool> IsSyncInProgressAsync(string scope)
    {
        return Task.FromResult(_activeSyncTracker.ContainsKey(scope));
    }

    public async Task<SyncResultViewModel> StartSyncAsync(
        int userId,
        string scope,
        SyncMode syncMode,
        string connectionId)
    {
        // Check for concurrent sync
        if (_activeSyncTracker.ContainsKey(scope))
        {
            throw new InvalidOperationException($"Sync already in progress for scope: {scope}");
        }

        // Initialize progress tracking
        var progress = new SyncProgressUpdate
        {
            SyncId = Guid.NewGuid().ToString(),
            Scope = scope,
            PercentComplete = 0,
            CurrentOperation = "Initializing sync...",
            StartTime = DateTime.UtcNow
        };
        _activeSyncTracker.TryAdd(scope, progress);

        // Audit log: Sync started
        await _auditLogService.LogEventAsync("TriggerSync", true, userId, null, scope,
            $"Started {syncMode} sync for {scope}");

        try
        {
            // Parse scope
            var parts = scope.Split(':');
            var scopeType = parts[0];
            var forceFullSync = syncMode == SyncMode.Full;

            // Send initial progress update
            await BroadcastProgressAsync(scope, progress);

            // Create progress reporter that converts Core.Sync.SyncProgress to AdminPortal SyncProgressUpdate
            var progressReporter = new Progress<SyncProgress>(p =>
            {
                progress.PercentComplete = p.PercentComplete;
                progress.CurrentOperation = p.CurrentOperation;
                progress.StudentsProcessed = p.StudentsProcessed;
                progress.StudentsUpdated = p.StudentsUpdated;
                progress.StudentsFailed = p.StudentsFailed;
                progress.TeachersProcessed = p.TeachersProcessed;
                progress.TeachersUpdated = p.TeachersUpdated;
                progress.TeachersFailed = p.TeachersFailed;
                progress.CoursesProcessed = p.CoursesProcessed;
                progress.CoursesUpdated = p.CoursesUpdated;
                progress.CoursesFailed = p.CoursesFailed;
                progress.SectionsProcessed = p.SectionsProcessed;
                progress.SectionsUpdated = p.SectionsUpdated;
                progress.SectionsFailed = p.SectionsFailed;
                progress.EstimatedTimeRemaining = p.EstimatedTimeRemaining;

                // Incremental sync breakdown
                progress.IsIncrementalSync = p.IsIncrementalSync;
                progress.StudentsCreated = p.StudentsCreated;
                progress.StudentsDeleted = p.StudentsDeleted;
                progress.TeachersCreated = p.TeachersCreated;
                progress.TeachersDeleted = p.TeachersDeleted;
                progress.SectionsCreated = p.SectionsCreated;
                progress.SectionsDeleted = p.SectionsDeleted;
                progress.EventsProcessed = p.EventsProcessed;
                progress.EventsSkipped = p.EventsSkipped;

                // Broadcast progress update synchronously to ensure delivery
                Task.Run(async () =>
                {
                    try
                    {
                        await BroadcastProgressAsync(scope, progress);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to broadcast progress update for scope {Scope}", scope);
                    }
                }).Wait(TimeSpan.FromSeconds(1)); // Wait briefly to ensure broadcast completes
            });

            SyncResultViewModel result;

            // Execute sync based on scope type
            switch (scopeType)
            {
                case "school":
                    // Parse school ID
                    var schoolId = parts.Length > 1 ? int.Parse(parts[1]) : 0;

                    var schoolResult = await _syncService.SyncSchoolAsync(schoolId, forceFullSync, progressReporter);
                    result = MapSchoolResult(schoolResult, scope, syncMode);
                    break;

                case "district":
                    // Convert Clever District ID to database District ID
                    var cleverDistrictId = parts.Length > 1 ? parts[1] : string.Empty;
                    var district = await _dbContext.Districts
                        .FirstOrDefaultAsync(d => d.CleverDistrictId == cleverDistrictId);

                    if (district == null)
                    {
                        throw new InvalidOperationException($"District not found with Clever ID: {cleverDistrictId}");
                    }

                    var districtSummary = await _syncService.SyncDistrictAsync(district.DistrictId, forceFullSync, progressReporter);
                    result = MapDistrictSummary(districtSummary, scope, syncMode);
                    break;

                case "all":
                    var allSummary = await _syncService.SyncAllDistrictsAsync(forceFullSync, progressReporter);
                    result = MapAllDistrictsSummary(allSummary, scope, syncMode);
                    break;

                default:
                    throw new ArgumentException($"Invalid scope: {scope}");
            }

            // Final progress update
            progress.PercentComplete = 100;
            progress.CurrentOperation = "Sync completed";
            await BroadcastProgressAsync(scope, progress);

            // Audit log: Sync completed
            await _auditLogService.LogEventAsync("SyncCompleted", result.Success, userId, null, scope,
                $"{syncMode} sync {(result.Success ? "succeeded" : "failed")} for {scope}");

            // Broadcast completion to SignalR clients
            await _hubContext.Clients.Group($"sync-{scope}").SendAsync("ReceiveCompletion", result);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed for scope {Scope}", scope);

            await _auditLogService.LogEventAsync("SyncFailed", false, userId, null, scope,
                $"Sync failed: {ex.Message}");

            // Build detailed error message including inner exceptions
            var errorDetails = ex.Message;
            if (ex.InnerException != null)
            {
                errorDetails += $" | Inner: {ex.InnerException.Message}";
            }
            // Add exception type for better debugging
            errorDetails = $"[{ex.GetType().Name}] {errorDetails}";

            var errorResult = new SyncResultViewModel
            {
                Success = false,
                Scope = scope,
                ErrorMessage = errorDetails,
                CompletedAt = DateTime.UtcNow
            };

            await _hubContext.Clients.Group($"sync-{scope}").SendAsync("ReceiveCompletion", errorResult);

            return errorResult;
        }
        finally
        {
            // Remove from active syncs
            _activeSyncTracker.TryRemove(scope);
        }
    }

    public Task<SyncProgressUpdate?> GetCurrentProgressAsync(string scope)
    {
        _activeSyncTracker.TryGetValue(scope, out var progress);
        return Task.FromResult(progress);
    }

    public Task<IReadOnlyDictionary<string, SyncProgressUpdate>> GetAllActiveSyncsAsync()
    {
        return Task.FromResult(_activeSyncTracker.GetAllActiveSyncs());
    }

    private async Task BroadcastProgressAsync(string scope, SyncProgressUpdate progress)
    {
        _activeSyncTracker.UpdateProgress(scope, progress);
        await _hubContext.Clients.Group($"sync-{scope}").SendAsync("ReceiveProgress", progress);
    }

    private SyncResultViewModel MapSchoolResult(SyncResult syncResult, string scope, SyncMode syncMode)
    {
        var result = new SyncResultViewModel
        {
            Success = syncResult.Success,
            Scope = "school",
            SchoolId = syncResult.SchoolId,
            SchoolName = syncResult.SchoolName,
            SyncMode = syncMode,
            Duration = syncResult.Duration,
            StudentsProcessed = syncResult.StudentsProcessed,
            StudentsFailed = syncResult.StudentsFailed,
            StudentsDeleted = syncResult.StudentsDeleted,
            TeachersProcessed = syncResult.TeachersProcessed,
            TeachersFailed = syncResult.TeachersFailed,
            TeachersDeleted = syncResult.TeachersDeleted,
            ErrorMessage = syncResult.ErrorMessage
        };

        // Map EventsSummary for incremental sync results
        if (syncResult.EventsSummary != null)
        {
            result.IsIncrementalSync = true;
            result.StudentsCreated = syncResult.EventsSummary.StudentCreated;
            result.StudentsUpdated = syncResult.EventsSummary.StudentUpdated;
            result.StudentsDeleted = syncResult.EventsSummary.StudentDeleted;
            result.TeachersCreated = syncResult.EventsSummary.TeacherCreated;
            result.TeachersUpdated = syncResult.EventsSummary.TeacherUpdated;
            result.TeachersDeleted = syncResult.EventsSummary.TeacherDeleted;
            result.SectionsCreated = syncResult.EventsSummary.SectionCreated;
            result.SectionsUpdated = syncResult.EventsSummary.SectionUpdated;
            result.SectionsDeleted = syncResult.EventsSummary.SectionDeleted;
            result.EventsProcessed = syncResult.EventsSummary.TotalEventsProcessed;
            result.EventsSkipped = syncResult.EventsSummary.EventsSkipped;
        }

        return result;
    }

    private SyncResultViewModel MapDistrictSummary(SyncSummary summary, string scope, SyncMode syncMode)
    {
        return new SyncResultViewModel
        {
            Success = summary.FailedSchools == 0,
            Scope = "district",
            DistrictId = scope.Split(':')[1], // Clever District ID (string)
            SyncMode = syncMode,
            Duration = summary.Duration,
            TotalSchools = summary.TotalSchools,
            SuccessfulSchools = summary.SuccessfulSchools,
            FailedSchools = summary.FailedSchools,
            SchoolResults = summary.SchoolResults.Select(s => new SchoolSyncResult
            {
                SchoolId = s.SchoolId,
                SchoolName = s.SchoolName,
                Success = s.Success,
                StudentsProcessed = s.StudentsProcessed,
                TeachersProcessed = s.TeachersProcessed,
                ErrorMessage = s.ErrorMessage
            }).ToList()
        };
    }

    private SyncResultViewModel MapAllDistrictsSummary(SyncSummary summary, string scope, SyncMode syncMode)
    {
        return new SyncResultViewModel
        {
            Success = summary.FailedSchools == 0,
            Scope = "all",
            SyncMode = syncMode,
            Duration = summary.Duration,
            TotalSchools = summary.TotalSchools,
            SuccessfulSchools = summary.SuccessfulSchools,
            FailedSchools = summary.FailedSchools,
            SchoolResults = summary.SchoolResults.Select(s => new SchoolSyncResult
            {
                SchoolId = s.SchoolId,
                SchoolName = s.SchoolName,
                Success = s.Success,
                StudentsProcessed = s.StudentsProcessed,
                TeachersProcessed = s.TeachersProcessed,
                ErrorMessage = s.ErrorMessage
            }).ToList()
        };
    }
}
