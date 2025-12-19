namespace CleverSyncSOS.Core.Database.SessionDb.Entities;

/// <summary>
/// Records warnings generated during sync operations.
/// Used to alert administrators about sections linked to workshops that are being modified or deleted.
/// </summary>
public class SyncWarning
{
    /// <summary>
    /// Primary key, auto-increment
    /// </summary>
    public int SyncWarningId { get; set; }

    /// <summary>
    /// Foreign key to SyncHistory
    /// </summary>
    public int SyncId { get; set; }

    /// <summary>
    /// Type of warning: SectionDeleted, SectionModified, SectionDeactivated
    /// </summary>
    public string WarningType { get; set; } = string.Empty;

    /// <summary>
    /// Entity type affected (e.g., "Section")
    /// </summary>
    public string EntityType { get; set; } = string.Empty;

    /// <summary>
    /// The entity's ID in our database (e.g., SectionId)
    /// </summary>
    public int EntityId { get; set; }

    /// <summary>
    /// The entity's Clever ID (e.g., CleverSectionId)
    /// </summary>
    public string CleverEntityId { get; set; } = string.Empty;

    /// <summary>
    /// Name of the affected entity for display
    /// </summary>
    public string EntityName { get; set; } = string.Empty;

    /// <summary>
    /// Detailed description of the warning
    /// </summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>
    /// JSON array of affected workshop names/IDs
    /// </summary>
    public string? AffectedWorkshops { get; set; }

    /// <summary>
    /// Number of workshops affected
    /// </summary>
    public int AffectedWorkshopCount { get; set; }

    /// <summary>
    /// Whether the warning has been acknowledged by an administrator
    /// </summary>
    public bool IsAcknowledged { get; set; }

    /// <summary>
    /// When the warning was acknowledged
    /// </summary>
    public DateTime? AcknowledgedAt { get; set; }

    /// <summary>
    /// Who acknowledged the warning
    /// </summary>
    public string? AcknowledgedBy { get; set; }

    /// <summary>
    /// When the warning was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation property: Related sync history
    /// </summary>
    public SyncHistory? SyncHistory { get; set; }
}
