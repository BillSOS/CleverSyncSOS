namespace CleverSyncSOS.Core.Database.SessionDb.Entities;

/// <summary>
/// Stores detailed change information for individual records during sync operations.
/// Tracks which specific fields were updated and their old/new values.
/// </summary>
public class SyncChangeDetail
{
    /// <summary>
    /// Unique identifier for this change detail record.
    /// </summary>
    public int ChangeDetailId { get; set; }

    /// <summary>
    /// Foreign key to SyncHistory table.
    /// </summary>
    public int SyncId { get; set; }

    /// <summary>
    /// Type of entity that was changed (Student, Teacher, Section).
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// Clever ID of the entity that was changed.
    /// </summary>
    public string EntityId { get; set; } = string.Empty;

    /// <summary>
    /// Display name of the entity (e.g., student/teacher name).
    /// </summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>
    /// Type of change operation (Created, Updated, Deleted).
    /// </summary>
    public string ChangeType { get; set; } = string.Empty;

    /// <summary>
    /// Comma-separated list of field names that were changed.
    /// Example: "Email,Grade,LastModified"
    /// </summary>
    public string FieldsChanged { get; set; } = string.Empty;

    /// <summary>
    /// JSON representation of old values for changed fields.
    /// Example: {"Email":"old@email.com","Grade":"9"}
    /// </summary>
    public string? OldValues { get; set; }

    /// <summary>
    /// JSON representation of new values for changed fields.
    /// Example: {"Email":"new@email.com","Grade":"10"}
    /// </summary>
    public string? NewValues { get; set; }

    /// <summary>
    /// Timestamp when this change was processed.
    /// </summary>
    public DateTime ChangedAt { get; set; }

    /// <summary>
    /// Navigation property to sync history.
    /// </summary>
    public SyncHistory? SyncHistory { get; set; }
}
