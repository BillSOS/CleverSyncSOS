namespace CleverSyncSOS.Core.Database.SchoolDb.Entities;

/// <summary>
/// Audit record for workshop sync operations performed by the stored procedure.
/// Tracks student workshop assignment changes during sync operations.
/// </summary>
public class WorkshopSyncAudit
{
    /// <summary>
    /// Primary key, auto-increment
    /// </summary>
    public int WorkshopSyncAuditId { get; set; }

    /// <summary>
    /// Foreign key to Student table - the student whose assignment changed
    /// </summary>
    public int StudentId { get; set; }

    /// <summary>
    /// Foreign key to WorkshopSchedule table - the student's previous workshop schedule
    /// </summary>
    public int OldWorkshopScheduleId { get; set; }

    /// <summary>
    /// Foreign key to WorkshopSchedule table - the student's new workshop schedule
    /// </summary>
    public int NewWorkshopScheduleId { get; set; }

    /// <summary>
    /// Date of the workshop being changed
    /// </summary>
    public DateTime WorkshopDate { get; set; }

    /// <summary>
    /// Type of action: e.g., "Moved", "Added", "Removed"
    /// </summary>
    public string ActionType { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key to SyncHistory (ImportLogId maps to SyncId)
    /// Links this audit record to the sync operation that triggered it
    /// </summary>
    public int ImportLogId { get; set; }

    /// <summary>
    /// When this audit record was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation property: Related student
    /// </summary>
    public Student? Student { get; set; }

    /// <summary>
    /// Navigation property: Previous workshop schedule
    /// </summary>
    public WorkshopSchedule? OldWorkshopSchedule { get; set; }

    /// <summary>
    /// Navigation property: New workshop schedule
    /// </summary>
    public WorkshopSchedule? NewWorkshopSchedule { get; set; }
}
