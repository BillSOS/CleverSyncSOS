using CleverSyncSOS.Core.Database.SessionDb.Entities;

namespace CleverSyncSOS.Core.Services;

/// <summary>
/// Service for managing sync schedules.
/// </summary>
public interface ISyncScheduleService
{
    /// <summary>
    /// Gets all sync schedules with district information.
    /// </summary>
    Task<List<SyncSchedule>> GetAllSchedulesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets schedules for a specific district.
    /// </summary>
    Task<List<SyncSchedule>> GetSchedulesByDistrictAsync(int districtId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a schedule by its ID.
    /// </summary>
    Task<SyncSchedule?> GetScheduleByIdAsync(int scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a new sync schedule.
    /// </summary>
    Task<SyncSchedule> CreateScheduleAsync(SyncSchedule schedule, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing sync schedule.
    /// </summary>
    Task<SyncSchedule> UpdateScheduleAsync(SyncSchedule schedule, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes a sync schedule.
    /// </summary>
    Task DeleteScheduleAsync(int scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Toggles the enabled state of a schedule.
    /// </summary>
    Task<bool> ToggleScheduleAsync(int scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets schedules that are due to run now.
    /// Considers the schedule time, day of week, and whether it has already run in the current window.
    /// </summary>
    /// <param name="windowMinutes">The time window in minutes (default 5 minutes)</param>
    Task<List<SyncSchedule>> GetDueSchedulesAsync(int windowMinutes = 5, CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks a schedule as having been triggered.
    /// Updates LastTriggeredUtc to prevent duplicate runs.
    /// </summary>
    Task MarkScheduleTriggeredAsync(int scheduleId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the next scheduled run time for a schedule in the district's local timezone.
    /// </summary>
    DateTime? GetNextRunTime(SyncSchedule schedule);
}
