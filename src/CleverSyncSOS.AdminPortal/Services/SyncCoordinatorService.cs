using CleverSyncSOS.AdminPortal.Hubs;
using CleverSyncSOS.AdminPortal.Models.ViewModels;
using CleverSyncSOS.Core.Database.SessionDb;
using CleverSyncSOS.Core.Services;
using CleverSyncSOS.Core.Sync;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using SyncProgress = CleverSyncSOS.Core.Sync.SyncProgress;

namespace CleverSyncSOS.AdminPortal.Services;

/// <summary>
/// Implementation of ISyncCoordinatorService for orchestrating sync operations with SignalR progress.
/// Uses database-based distributed locking to prevent concurrent syncs across Admin Portal and Azure Functions.
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
    private readonly ISyncLockService _syncLockService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public SyncCoordinatorService(
        ISyncService syncService,
        IHubContext<SyncProgressHub> hubContext,
        IAuditLogService auditLogService,
        ILogger<SyncCoordinatorService> logger,
        SessionDbContext dbContext,
        ActiveSyncTracker activeSyncTracker,
        ISyncLockService syncLockService,
        IHttpContextAccessor httpContextAccessor)
    {
        _syncService = syncService;
        _hubContext = hubContext;
        _auditLogService = auditLogService;
        _logger = logger;
        _dbContext = dbContext;
        _activeSyncTracker = activeSyncTracker;
        _syncLockService = syncLockService;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<bool> IsSyncInProgressAsync(string scope)
    {
        // Check database-based distributed lock first (cross-process)
        var lockInfo = await _syncLockService.GetLockInfoAsync(scope);
        if (lockInfo != null && lockInfo.IsLocked)
        {
            return true;
        }

        // Also check local in-memory tracker for UI updates
        return _activeSyncTracker.ContainsKey(scope);
    }

    public async Task<SyncResultViewModel> StartSyncAsync(
        int userId,
        string scope,
        SyncMode syncMode,
        string connectionId)
    {
        // Get HTTP context info for audit logging
        var httpContext = _httpContextAccessor.HttpContext;
        var ipAddress = httpContext?.Connection.RemoteIpAddress?.ToString();
        var userAgent = httpContext?.Request.Headers["User-Agent"].ToString();

        // Get user info for lock tracking and audit logging
        string initiatedBy;
        string userIdentifier;

        if (userId <= 0)
        {
            // No database user ID - check if this is a Super Admin bypass login
            var userName = httpContext?.User?.Identity?.Name;
            var isSuperAdmin = httpContext?.User?.IsInRole("SuperAdmin") ?? false;

            if (isSuperAdmin && !string.IsNullOrEmpty(userName))
            {
                // Use the claims-based name for Super Admin bypass login
                initiatedBy = userName;
                userIdentifier = userName;
            }
            else
            {
                initiatedBy = "Unknown User";
                userIdentifier = "Unknown User";
            }
        }
        else
        {
            // Normal user with database ID - look up their info
            var user = await _dbContext.Users.FindAsync(userId);
            initiatedBy = user?.DisplayName ?? $"User:{userId}";
            userIdentifier = user?.DisplayName ?? user?.Email ?? $"User:{userId}";
        }

        // Acquire database-based distributed lock (works across Admin Portal + Azure Functions)
        var lockResult = await _syncLockService.TryAcquireLockAsync(
            scope,
            acquiredBy: "AdminPortal",
            initiatedBy: initiatedBy,
            durationMinutes: 60); // 60 minutes max sync duration

        if (!lockResult.Success)
        {
            var holder = lockResult.CurrentHolder ?? "unknown";
            var holderInitiatedBy = lockResult.CurrentHolderInitiatedBy ?? "unknown";
            var message = $"Sync already in progress for scope: {scope}. Currently held by {holder} (initiated by {holderInitiatedBy})";

            if (lockResult.CurrentHolderAcquiredAt.HasValue)
            {
                var duration = DateTime.UtcNow - lockResult.CurrentHolderAcquiredAt.Value;
                message += $" for {duration.TotalMinutes:F1} minutes";
            }

            _logger.LogWarning(message);
            throw new InvalidOperationException(message);
        }

        var lockId = lockResult.LockId!;

        // Initialize progress tracking (local in-memory tracker for UI updates)
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
        await _auditLogService.LogEventAsync("TriggerSync", true, userId, userIdentifier, scope,
            $"Started {syncMode} sync for {scope}", ipAddress, userAgent);

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

                // Admin counters (always fetched fresh)
                progress.AdminsProcessed = p.AdminsProcessed;
                progress.AdminsUpdated = p.AdminsUpdated;
                progress.AdminsFailed = p.AdminsFailed;

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
            await _auditLogService.LogEventAsync("SyncCompleted", result.Success, userId, userIdentifier, scope,
                $"{syncMode} sync {(result.Success ? "succeeded" : "failed")} for {scope}", ipAddress, userAgent);

            // Broadcast completion to SignalR clients (scope-specific for ManualSync page)
            await _hubContext.Clients.Group($"sync-{scope}").SendAsync("ReceiveCompletion", result);

            // Broadcast to all clients that sync history has been updated (for Sync/SyncHistory pages)
            await _hubContext.Clients.All.SendAsync("SyncHistoryUpdated", scope);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync failed for scope {Scope}", scope);

            await _auditLogService.LogEventAsync("SyncFailed", false, userId, userIdentifier, scope,
                $"Sync failed: {ex.Message}", ipAddress, userAgent);

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

            // Broadcast to all clients that sync history has been updated (for Sync/SyncHistory pages)
            await _hubContext.Clients.All.SendAsync("SyncHistoryUpdated", scope);

            return errorResult;
        }
        finally
        {
            // Release database-based distributed lock
            var released = await _syncLockService.ReleaseLockAsync(scope, lockId);
            if (!released)
            {
                _logger.LogWarning("Failed to release lock for scope {Scope} with lockId {LockId}", scope, lockId);
            }

            // Remove from local in-memory tracker (for UI updates)
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
            AdminsProcessed = syncResult.AdminsProcessed,
            AdminsUpdated = syncResult.AdminsUpdated,
            AdminsFailed = syncResult.AdminsFailed,
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
                SectionsProcessed = s.SectionsProcessed,
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
                SectionsProcessed = s.SectionsProcessed,
                ErrorMessage = s.ErrorMessage
            }).ToList()
        };
    }
}
