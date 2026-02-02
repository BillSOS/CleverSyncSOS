using System.ComponentModel.DataAnnotations.Schema;

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
    /// Prefix for Key Vault secrets (e.g., "Lincoln-Elementary").
    /// Used to construct secret names: CleverSyncSOS--{SchoolPrefix}--{Property}
    /// DEPRECATED: Use KeyVaultSchoolPrefix instead for v2.0 naming convention.
    /// </summary>
    [NotMapped]
    public string? SchoolPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Prefix used for Key Vault secret naming (e.g., "CityHighSchool").
    /// Secrets follow pattern: {KeyVaultSchoolPrefix}--{FunctionalName}
    /// This is the v2.0 naming convention.
    /// </summary>
    public string KeyVaultSchoolPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Name of school's dedicated database (e.g., "School_Lincoln_Db").
    /// </summary>
    public string? DatabaseName { get; set; }

    /// <summary>
    /// DEPRECATED: Legacy connection string secret name. Use SchoolPrefix instead.
    /// Kept for migration compatibility only.
    /// </summary>
    [NotMapped]
    [Obsolete("Use SchoolPrefix instead. This property will be removed in a future version.")]
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
