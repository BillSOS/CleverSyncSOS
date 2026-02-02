using System.ComponentModel.DataAnnotations.Schema;

namespace CleverSyncSOS.Core.Database.SessionDb.Entities;

/// <summary>
/// Represents a school district with Clever API credentials.
/// </summary>
public class District
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public int DistrictId { get; set; }

    /// <summary>
    /// Clever's district identifier.
    /// </summary>
    public string CleverDistrictId { get; set; } = string.Empty;

    /// <summary>
    /// District display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Prefix for Key Vault secrets (e.g., "Lincoln-District").
    /// Used to construct secret names: CleverSyncSOS--{DistrictPrefix}--{Property}
    /// DEPRECATED: Use KeyVaultDistrictPrefix instead for v2.0 naming convention.
    /// </summary>
    [NotMapped]
    public string? DistrictPrefix { get; set; } = string.Empty;

    /// <summary>
    /// Prefix used for Key Vault secret naming (e.g., "NorthCentral").
    /// Secrets follow pattern: {KeyVaultDistrictPrefix}--{FunctionalName}
    /// This is the v2.0 naming convention.
    /// </summary>
    public string KeyVaultDistrictPrefix { get; set; } = string.Empty;

    /// <summary>
    /// DEPRECATED: Legacy prefix field. Use KeyVaultDistrictPrefix instead.
    /// Kept for migration compatibility only.
    /// </summary>
    [NotMapped]
    [Obsolete("Use KeyVaultDistrictPrefix instead. This property will be removed in a future version.")]
    public string? KeyVaultSecretPrefix { get; set; }

    /// <summary>
    /// Timestamp of record creation.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Timestamp of last update.
    /// </summary>
    public DateTime UpdatedAt { get; set; }

    /// <summary>
    /// Local time zone for the district (e.g., "Eastern Standard Time").
    /// Used to display timestamps in the district's local time.
    /// </summary>
    public string LocalTimeZone { get; set; } = "Eastern Standard Time";

    /// <summary>
    /// Navigation property for schools in this district.
    /// </summary>
    public ICollection<School> Schools { get; set; } = new List<School>();
}
