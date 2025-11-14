namespace CleverSyncSOS.Core.Database.SessionDb.Entities;

/// <summary>
/// Represents a school within a district, with reference to its dedicated database.
/// </summary>
public class School
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public int SchoolId { get; set; }

    /// <summary>
    /// Clever's district identifier (references District.CleverDistrictId).
    /// </summary>
    public string DistrictId { get; set; } = string.Empty;

    /// <summary>
    /// Clever's school identifier.
    /// </summary>
    public string CleverSchoolId { get; set; } = string.Empty;

    /// <summary>
    /// School display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Name of school's dedicated database (e.g., "School_Lincoln_Db").
    /// </summary>
    public string? DatabaseName { get; set; }

    /// <summary>
    /// Key Vault secret name for connection string (e.g., "School-Lincoln-ConnectionString").
    /// </summary>
    public string? KeyVaultConnectionStringSecretName { get; set; }

    /// <summary>
    /// Whether school is actively syncing.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Flag to force a full sync on next run (e.g., for beginning of school year).
    /// Reset to false after full sync completes.
    /// </summary>
    public bool RequiresFullSync { get; set; } = false;

    /// <summary>
    /// Timestamp of record creation.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Timestamp of last update.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Navigation property to district.
    /// </summary>
    public District? District { get; set; }

    /// <summary>
    /// Navigation property for sync history records.
    /// </summary>
    public ICollection<SyncHistory> SyncHistories { get; set; } = new List<SyncHistory>();
}
