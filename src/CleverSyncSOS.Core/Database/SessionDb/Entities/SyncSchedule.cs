using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CleverSyncSOS.Core.Database.SessionDb.Entities;

/// <summary>
/// Represents a scheduled sync time configuration.
/// Schedules are stored in local time and converted to UTC for execution.
/// </summary>
[Table("SyncSchedule")]
public class SyncSchedule
{
    /// <summary>
    /// Unique identifier for the schedule.
    /// </summary>
    [Key]
    public int SyncScheduleId { get; set; }

    /// <summary>
    /// The district this schedule applies to.
    /// </summary>
    public int DistrictId { get; set; }

    /// <summary>
    /// User-friendly name for the schedule (e.g., "Morning Sync", "Nightly Sync").
    /// </summary>
    [Required]
    [MaxLength(100)]
    public string ScheduleName { get; set; } = string.Empty;

    /// <summary>
    /// Hour of day in local time (0-23).
    /// </summary>
    [Range(0, 23)]
    public int LocalHour { get; set; }

    /// <summary>
    /// Minute of hour in local time (0-59).
    /// </summary>
    [Range(0, 59)]
    public int LocalMinute { get; set; }

    /// <summary>
    /// Days of week when this schedule should run.
    /// Comma-separated values: "Mon,Tue,Wed,Thu,Fri" or "Daily" for every day.
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string DaysOfWeek { get; set; } = "Daily";

    /// <summary>
    /// Whether this schedule is currently active.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Last time this schedule triggered a sync (UTC).
    /// Used to prevent duplicate runs within the same time window.
    /// </summary>
    public DateTime? LastTriggeredUtc { get; set; }

    /// <summary>
    /// When this schedule was created.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When this schedule was last updated.
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User who created this schedule (for audit trail).
    /// </summary>
    [MaxLength(255)]
    public string? CreatedBy { get; set; }

    /// <summary>
    /// Navigation property to the district.
    /// </summary>
    [ForeignKey(nameof(DistrictId))]
    public District? District { get; set; }

    /// <summary>
    /// Gets the display time in 12-hour format (e.g., "6:30 AM").
    /// </summary>
    [NotMapped]
    public string DisplayTime
    {
        get
        {
            var hour12 = LocalHour == 0 ? 12 : (LocalHour > 12 ? LocalHour - 12 : LocalHour);
            var amPm = LocalHour < 12 ? "AM" : "PM";
            return $"{hour12}:{LocalMinute:D2} {amPm}";
        }
    }

    /// <summary>
    /// Gets a friendly display of the days (e.g., "Weekdays", "Daily", "Mon, Wed, Fri").
    /// </summary>
    [NotMapped]
    public string DisplayDays
    {
        get
        {
            if (string.IsNullOrEmpty(DaysOfWeek) || DaysOfWeek.Equals("Daily", StringComparison.OrdinalIgnoreCase))
                return "Daily";

            var days = DaysOfWeek.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // Check for weekdays
            var weekdays = new[] { "Mon", "Tue", "Wed", "Thu", "Fri" };
            if (days.Length == 5 && weekdays.All(d => days.Contains(d, StringComparer.OrdinalIgnoreCase)))
                return "Weekdays";

            // Check for weekends
            var weekends = new[] { "Sat", "Sun" };
            if (days.Length == 2 && weekends.All(d => days.Contains(d, StringComparer.OrdinalIgnoreCase)))
                return "Weekends";

            return string.Join(", ", days);
        }
    }

    /// <summary>
    /// Checks if the schedule should run on the specified day of week.
    /// </summary>
    public bool ShouldRunOnDay(DayOfWeek dayOfWeek)
    {
        if (string.IsNullOrEmpty(DaysOfWeek) || DaysOfWeek.Equals("Daily", StringComparison.OrdinalIgnoreCase))
            return true;

        var dayAbbrev = dayOfWeek switch
        {
            DayOfWeek.Sunday => "Sun",
            DayOfWeek.Monday => "Mon",
            DayOfWeek.Tuesday => "Tue",
            DayOfWeek.Wednesday => "Wed",
            DayOfWeek.Thursday => "Thu",
            DayOfWeek.Friday => "Fri",
            DayOfWeek.Saturday => "Sat",
            _ => ""
        };

        return DaysOfWeek.Contains(dayAbbrev, StringComparison.OrdinalIgnoreCase);
    }
}
