using CleverSyncSOS.Core.Database.SessionDb;
using CleverSyncSOS.Core.Database.SessionDb.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Core.Services;

/// <summary>
/// Service for managing sync schedules.
/// Handles CRUD operations and schedule execution logic.
/// </summary>
public class SyncScheduleService : ISyncScheduleService
{
    private readonly SessionDbContext _sessionDb;
    private readonly ILogger<SyncScheduleService> _logger;

    public SyncScheduleService(SessionDbContext sessionDb, ILogger<SyncScheduleService> logger)
    {
        _sessionDb = sessionDb;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<List<SyncSchedule>> GetAllSchedulesAsync(CancellationToken cancellationToken = default)
    {
        return await _sessionDb.SyncSchedules
            .Include(s => s.District)
            .OrderBy(s => s.District!.Name)
            .ThenBy(s => s.LocalHour)
            .ThenBy(s => s.LocalMinute)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<List<SyncSchedule>> GetSchedulesByDistrictAsync(int districtId, CancellationToken cancellationToken = default)
    {
        return await _sessionDb.SyncSchedules
            .Include(s => s.District)
            .Where(s => s.DistrictId == districtId)
            .OrderBy(s => s.LocalHour)
            .ThenBy(s => s.LocalMinute)
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SyncSchedule?> GetScheduleByIdAsync(int scheduleId, CancellationToken cancellationToken = default)
    {
        return await _sessionDb.SyncSchedules
            .Include(s => s.District)
            .FirstOrDefaultAsync(s => s.SyncScheduleId == scheduleId, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<SyncSchedule> CreateScheduleAsync(SyncSchedule schedule, CancellationToken cancellationToken = default)
    {
        schedule.CreatedAt = DateTime.UtcNow;
        schedule.UpdatedAt = DateTime.UtcNow;

        _sessionDb.SyncSchedules.Add(schedule);
        await _sessionDb.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Created sync schedule {ScheduleId}: {ScheduleName} at {Hour}:{Minute} for district {DistrictId}",
            schedule.SyncScheduleId, schedule.ScheduleName, schedule.LocalHour, schedule.LocalMinute, schedule.DistrictId);

        return schedule;
    }

    /// <inheritdoc />
    public async Task<SyncSchedule> UpdateScheduleAsync(SyncSchedule schedule, CancellationToken cancellationToken = default)
    {
        var existing = await _sessionDb.SyncSchedules.FindAsync(new object[] { schedule.SyncScheduleId }, cancellationToken)
            ?? throw new InvalidOperationException($"Schedule {schedule.SyncScheduleId} not found");

        existing.ScheduleName = schedule.ScheduleName;
        existing.LocalHour = schedule.LocalHour;
        existing.LocalMinute = schedule.LocalMinute;
        existing.DaysOfWeek = schedule.DaysOfWeek;
        existing.IsEnabled = schedule.IsEnabled;
        existing.UpdatedAt = DateTime.UtcNow;

        await _sessionDb.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Updated sync schedule {ScheduleId}: {ScheduleName} at {Hour}:{Minute}",
            existing.SyncScheduleId, existing.ScheduleName, existing.LocalHour, existing.LocalMinute);

        return existing;
    }

    /// <inheritdoc />
    public async Task DeleteScheduleAsync(int scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = await _sessionDb.SyncSchedules.FindAsync(new object[] { scheduleId }, cancellationToken);
        if (schedule != null)
        {
            _sessionDb.SyncSchedules.Remove(schedule);
            await _sessionDb.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Deleted sync schedule {ScheduleId}: {ScheduleName}", scheduleId, schedule.ScheduleName);
        }
    }

    /// <inheritdoc />
    public async Task<bool> ToggleScheduleAsync(int scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = await _sessionDb.SyncSchedules.FindAsync(new object[] { scheduleId }, cancellationToken)
            ?? throw new InvalidOperationException($"Schedule {scheduleId} not found");

        schedule.IsEnabled = !schedule.IsEnabled;
        schedule.UpdatedAt = DateTime.UtcNow;

        await _sessionDb.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Toggled sync schedule {ScheduleId} to {Status}",
            scheduleId, schedule.IsEnabled ? "enabled" : "disabled");

        return schedule.IsEnabled;
    }

    /// <inheritdoc />
    public async Task<List<SyncSchedule>> GetDueSchedulesAsync(int windowMinutes = 5, CancellationToken cancellationToken = default)
    {
        var dueSchedules = new List<SyncSchedule>();
        var utcNow = DateTime.UtcNow;

        // Get all enabled schedules with district info
        var enabledSchedules = await _sessionDb.SyncSchedules
            .Include(s => s.District)
            .Where(s => s.IsEnabled)
            .ToListAsync(cancellationToken);

        foreach (var schedule in enabledSchedules)
        {
            if (schedule.District == null)
            {
                _logger.LogWarning("Schedule {ScheduleId} has no district, skipping", schedule.SyncScheduleId);
                continue;
            }

            // Convert UTC now to the district's local time
            var timeZone = GetTimeZoneInfo(schedule.District.LocalTimeZone);
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(utcNow, timeZone);

            // Check if schedule should run today
            if (!schedule.ShouldRunOnDay(localNow.DayOfWeek))
            {
                continue;
            }

            // Check if we're within the time window
            var scheduledTime = new TimeSpan(schedule.LocalHour, schedule.LocalMinute, 0);
            var currentTime = localNow.TimeOfDay;

            // Schedule is due if current time is within [scheduledTime, scheduledTime + windowMinutes)
            var windowEnd = scheduledTime.Add(TimeSpan.FromMinutes(windowMinutes));

            if (currentTime >= scheduledTime && currentTime < windowEnd)
            {
                // Check if we've already run this schedule today
                if (schedule.LastTriggeredUtc.HasValue)
                {
                    var lastTriggeredLocal = TimeZoneInfo.ConvertTimeFromUtc(schedule.LastTriggeredUtc.Value, timeZone);

                    // If last triggered was today and within this schedule window, skip
                    if (lastTriggeredLocal.Date == localNow.Date &&
                        lastTriggeredLocal.TimeOfDay >= scheduledTime)
                    {
                        _logger.LogDebug(
                            "Schedule {ScheduleId} already ran today at {LastRun}, skipping",
                            schedule.SyncScheduleId, lastTriggeredLocal);
                        continue;
                    }
                }

                _logger.LogInformation(
                    "Schedule {ScheduleId} ({ScheduleName}) is due: local time {LocalTime}, scheduled for {ScheduledTime}",
                    schedule.SyncScheduleId, schedule.ScheduleName, localNow.ToString("HH:mm"),
                    $"{schedule.LocalHour:D2}:{schedule.LocalMinute:D2}");

                dueSchedules.Add(schedule);
            }
        }

        return dueSchedules;
    }

    /// <inheritdoc />
    public async Task MarkScheduleTriggeredAsync(int scheduleId, CancellationToken cancellationToken = default)
    {
        var schedule = await _sessionDb.SyncSchedules.FindAsync(new object[] { scheduleId }, cancellationToken);
        if (schedule != null)
        {
            schedule.LastTriggeredUtc = DateTime.UtcNow;
            schedule.UpdatedAt = DateTime.UtcNow;
            await _sessionDb.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Marked schedule {ScheduleId} as triggered at {Time}",
                scheduleId, schedule.LastTriggeredUtc);
        }
    }

    /// <inheritdoc />
    public DateTime? GetNextRunTime(SyncSchedule schedule)
    {
        if (!schedule.IsEnabled || schedule.District == null)
            return null;

        var timeZone = GetTimeZoneInfo(schedule.District.LocalTimeZone);
        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, timeZone);

        // Start checking from today
        var checkDate = localNow.Date;
        var scheduledTimeOfDay = new TimeSpan(schedule.LocalHour, schedule.LocalMinute, 0);

        // Check up to 8 days ahead (to handle weekly schedules)
        for (int i = 0; i < 8; i++)
        {
            var candidateDateTime = checkDate.Add(scheduledTimeOfDay);

            // Skip if this time has already passed today
            if (candidateDateTime <= localNow)
            {
                checkDate = checkDate.AddDays(1);
                continue;
            }

            // Check if schedule runs on this day
            if (schedule.ShouldRunOnDay(checkDate.DayOfWeek))
            {
                return candidateDateTime;
            }

            checkDate = checkDate.AddDays(1);
        }

        return null;
    }

    /// <summary>
    /// Gets TimeZoneInfo from a Windows timezone ID string.
    /// Falls back to UTC if the timezone is not found.
    /// </summary>
    private TimeZoneInfo GetTimeZoneInfo(string timeZoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            _logger.LogWarning("Timezone '{TimeZone}' not found, using UTC", timeZoneId);
            return TimeZoneInfo.Utc;
        }
        catch (InvalidTimeZoneException)
        {
            _logger.LogWarning("Invalid timezone '{TimeZone}', using UTC", timeZoneId);
            return TimeZoneInfo.Utc;
        }
    }
}
