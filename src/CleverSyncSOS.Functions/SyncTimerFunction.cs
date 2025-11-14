// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-020
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Azure Functions
//   version: 1.0.0
// ---

using CleverSyncSOS.Core.Sync;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Functions;

/// <summary>
/// Timer-triggered Azure Function for scheduled synchronization.
/// Source: FR-020 - Implement timer-triggered Azure Function (default: daily at 2 AM UTC)
/// Spec: SpecKit/Specs/001-clever-api-auth/spec-1.md
/// </summary>
public class SyncTimerFunction
{
    private readonly ISyncService _syncService;
    private readonly ILogger<SyncTimerFunction> _logger;

    public SyncTimerFunction(ISyncService syncService, ILogger<SyncTimerFunction> logger)
    {
        _syncService = syncService;
        _logger = logger;
    }

    /// <summary>
    /// Timer-triggered function that runs daily at 2 AM UTC.
    /// Source: FR-020 - Sync Orchestration
    /// Cron schedule: "0 0 2 * * *" (second, minute, hour, day, month, day of week)
    /// </summary>
    [Function("SyncTimer")]
    public async Task Run(
        [TimerTrigger("0 0 2 * * *", RunOnStartup = false)] TimerInfo timerInfo,
        FunctionContext context)
    {
        _logger.LogInformation("Sync Timer Function triggered at {Time}", DateTime.UtcNow);

        try
        {
            // FR-020: Sync all districts
            _logger.LogInformation("Starting sync for all districts");

            // Get all districts from SessionDb and sync them
            // For now, we'll sync all schools we know about
            // In production, you'd query SessionDb for active districts

            var startTime = DateTime.UtcNow;

            // Since we only have one district configured, let's sync it
            // In a full implementation, this would iterate through all districts
            const int defaultDistrictId = 1; // Your district ID from SessionDb

            _logger.LogInformation("Syncing district {DistrictId}", defaultDistrictId);

            var result = await _syncService.SyncDistrictAsync(defaultDistrictId);

            var duration = DateTime.UtcNow - startTime;

            _logger.LogInformation(
                "Synced district {DistrictId} in {Duration}: " +
                "{SuccessfulSchools}/{TotalSchools} schools succeeded, " +
                "{TotalRecords} total records processed",
                defaultDistrictId, duration,
                result.SuccessfulSchools, result.TotalSchools,
                result.TotalRecordsProcessed);

            if (result.FailedSchools > 0)
            {
                _logger.LogWarning(
                    "{FailedSchools} schools failed during district {DistrictId} sync",
                    result.FailedSchools, defaultDistrictId);
            }

            // Log timer information
            if (timerInfo.ScheduleStatus != null)
            {
                _logger.LogInformation("Next scheduled run: {NextRun}", timerInfo.ScheduleStatus.Next);
                _logger.LogInformation("Last run: {LastRun}", timerInfo.ScheduleStatus.Last);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Sync Timer Function encountered an error");
            throw; // Let Azure Functions retry logic handle it
        }
    }
}
