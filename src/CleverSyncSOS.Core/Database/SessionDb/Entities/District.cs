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
    /// Prefix for Key Vault secrets (e.g., "District-ABC").
    /// </summary>
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
    /// Navigation property for schools in this district.
    /// </summary>
    public ICollection<School> Schools { get; set; } = new List<School>();
}
