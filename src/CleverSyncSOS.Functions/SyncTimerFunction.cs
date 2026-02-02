// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-020
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Azure Functions
//   version: 2.1.0
// ---

using CleverSyncSOS.Core.Services;
using CleverSyncSOS.Core.Sync;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Functions;

/// <summary>
/// Timer-triggered Azure Function for scheduled synchronization.
/// Source: FR-020 - Implement timer-triggered Azure Function
/// Runs every 5 minutes and checks database for schedules that are due to execute.
/// Schedules are configured by Super Admins via the Admin Portal.
/// Uses database-based distributed locking to prevent concurrent syncs with Admin Portal.
/// </summary>
public class SyncTimerFunction
{
    private readonly ISyncService _syncService;
    private readonly ISyncScheduleService _scheduleService;
    private readonly ISyncLockService _syncLockService;
    private readonly ILogger<SyncTimerFunction> _logger;

    public SyncTimerFunction(
        ISyncService syncService,
        ISyncScheduleService scheduleService,
        ISyncLockService syncLockService,
        ILogger<SyncTimerFunction> logger)
    {
        _syncService = syncService;
        _scheduleService = scheduleService;
        _syncLockService = syncLockService;
        _logger = logger;
    }

    /// <summary>
    /// Timer-triggered function that runs every 5 minutes to check for due schedules.
    /// Cron schedule: "0 */5 * * * *" (every 5 minutes)
    /// </summary>
    [Function("SyncTimer")]
    public async Task Run(
        [TimerTrigger("0 */5 * * * *", RunOnStartup = false)] TimerInfo timerInfo,
        FunctionContext context)
    {
        _logger.LogDebug("Sync Timer Function checking for due schedules at {Time}", DateTime.UtcNow);

        try
        {
            // Get schedules that are due to run now (within 5-minute window)
            var dueSchedules = await _scheduleService.GetDueSchedulesAsync(windowMinutes: 5);

            if (dueSchedules.Count == 0)
            {
                _logger.LogDebug("No schedules due at this time");
                return;
            }

            _logger.LogInformation("Found {Count} schedule(s) due to run", dueSchedules.Count);

            // First, cleanup any expired locks
            var expiredLocks = await _syncLockService.CleanupExpiredLocksAsync();
            if (expiredLocks > 0)
            {
                _logger.LogInformation("Cleaned up {Count} expired sync locks", expiredLocks);
            }

            foreach (var schedule in dueSchedules)
            {
                // Use district scope for locking
                var scope = $"district:{schedule.DistrictId}";
                string? lockId = null;

                try
                {
                    // Acquire database-based distributed lock
                    var lockResult = await _syncLockService.TryAcquireLockAsync(
                        scope,
                        acquiredBy: "AzureFunction",
                        initiatedBy: $"Schedule:{schedule.ScheduleName}",
                        durationMinutes: 60); // 60 minutes max sync duration

                    if (!lockResult.Success)
                    {
                        _logger.LogWarning(
                            "Skipping schedule {ScheduleId}: {ScheduleName} - sync lock already held by {Holder} (initiated by {InitiatedBy})",
                            schedule.SyncScheduleId, schedule.ScheduleName,
                            lockResult.CurrentHolder, lockResult.CurrentHolderInitiatedBy);
                        continue; // Skip this schedule, another sync is in progress
                    }

                    lockId = lockResult.LockId;

                    _logger.LogInformation(
                        "Executing schedule {ScheduleId}: {ScheduleName} for district {DistrictId}",
                        schedule.SyncScheduleId, schedule.ScheduleName, schedule.DistrictId);

                    var startTime = DateTime.UtcNow;

                    // Mark as triggered first to prevent duplicate runs
                    await _scheduleService.MarkScheduleTriggeredAsync(schedule.SyncScheduleId);

                    // Execute sync for the district
                    var result = await _syncService.SyncDistrictAsync(schedule.DistrictId);

                    var duration = DateTime.UtcNow - startTime;

                    _logger.LogInformation(
                        "Schedule {ScheduleId} completed in {Duration}: " +
                        "{SuccessfulSchools}/{TotalSchools} schools succeeded, " +
                        "{TotalRecords} total records processed",
                        schedule.SyncScheduleId, duration,
                        result.SuccessfulSchools, result.TotalSchools,
                        result.TotalRecordsProcessed);

                    if (result.FailedSchools > 0)
                    {
                        _logger.LogWarning(
                            "{FailedSchools} schools failed during schedule {ScheduleId} sync",
                            result.FailedSchools, schedule.SyncScheduleId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Error executing schedule {ScheduleId}: {ScheduleName}",
                        schedule.SyncScheduleId, schedule.ScheduleName);
                    // Continue with other schedules even if one fails
                }
                finally
                {
                    // Release the lock if we acquired one
                    if (!string.IsNullOrEmpty(lockId))
                    {
                        var released = await _syncLockService.ReleaseLockAsync(scope, lockId);
                        if (!released)
                        {
                            _logger.LogWarning("Failed to release lock for scope {Scope} with lockId {LockId}", scope, lockId);
                        }
                    }
                }
            }

            // Log timer information
            if (timerInfo.ScheduleStatus != null)
            {
                _logger.LogDebug("Next timer check: {NextRun}", timerInfo.ScheduleStatus.Next);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync Timer Function encountered an error checking schedules");
            throw; // Let Azure Functions retry logic handle it
        }
    }
}
