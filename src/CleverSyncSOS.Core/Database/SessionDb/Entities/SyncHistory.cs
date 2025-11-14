namespace CleverSyncSOS.Core.Database.SessionDb.Entities;

/// <summary>
/// Tracks sync operations for auditing and incremental sync logic.
/// </summary>
public class SyncHistory
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public int SyncId { get; set; }

    /// <summary>
    /// Foreign key to Schools table.
    /// </summary>
    public int SchoolId { get; set; }

    /// <summary>
    /// Type of entity synced ("Student", "Teacher", "Section").
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Type of synchronization operation (Full, Incremental, Reconciliation).
    /// </summary>
    public SyncType SyncType { get; set; } = SyncType.Incremental;

    /// <summary>
    /// Sync start timestamp.
    /// </summary>
    public DateTime SyncStartTime { get; set; }

    /// <summary>
    /// Sync completion timestamp.
    /// </summary>
    public DateTime? SyncEndTime { get; set; }

    /// <summary>
    /// Sync status ("Success", "Failed", "Partial", "InProgress").
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Number of records successfully synced.
    /// </summary>
    public int RecordsProcessed { get; set; }

    /// <summary>
    /// Number of records that failed.
    /// </summary>
    public int RecordsFailed { get; set; }

    /// <summary>
    /// Error details if sync failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Timestamp for incremental sync (Clever's last_modified).
    /// </summary>
    public DateTime? LastSyncTimestamp { get; set; }

    /// <summary>
    /// Navigation property to school.
    /// </summary>
    public School? School { get; set; }
}
