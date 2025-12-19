namespace CleverSyncSOS.Core.Database.SchoolDb.Entities;

/// <summary>
/// Represents a scheduled workshop instance.
/// Links workshops to specific dates and tracks scheduling information.
/// </summary>
public class WorkshopSchedule
{
    /// <summary>
    /// Primary key, auto-increment
    /// </summary>
    public int WorkshopScheduleId { get; set; }

    /// <summary>
    /// Foreign key to Workshop table
    /// </summary>
    public int WorkshopId { get; set; }

    /// <summary>
    /// Date of the scheduled workshop
    /// </summary>
    public DateTime WorkshopDate { get; set; }

    /// <summary>
    /// Navigation property: Related workshop
    /// </summary>
    public Workshop? Workshop { get; set; }
}
