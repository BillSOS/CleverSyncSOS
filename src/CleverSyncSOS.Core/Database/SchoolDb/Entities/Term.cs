namespace CleverSyncSOS.Core.Database.SchoolDb.Entities;

/// <summary>
/// Represents a term/semester from Clever API.
/// Terms are district-level entities in Clever but stored per-school for data isolation.
/// </summary>
/// <remarks>
/// <para><b>Clever API Reference:</b></para>
/// <list type="bullet">
///   <item><description>Endpoint: GET /v3.0/terms (district-level, not school-filtered)</description></item>
///   <item><description>Events: terms.created, terms.updated, terms.deleted</description></item>
/// </list>
///
/// <para><b>Sync Behavior:</b></para>
/// <list type="bullet">
///   <item><description>Full Sync: Fetch all terms, soft-delete orphans not seen in Clever</description></item>
///   <item><description>Incremental Sync: Process term events from Events API</description></item>
///   <item><description>Restoration: If a deleted term reappears in Clever, clear DeletedAt to restore it</description></item>
/// </list>
/// </remarks>
public class Term
{
    /// <summary>
    /// Primary key, auto-increment
    /// </summary>
    public int TermId { get; set; }

    /// <summary>
    /// Unique identifier from Clever API (24-character ObjectID)
    /// </summary>
    public string CleverTermId { get; set; } = string.Empty;

    /// <summary>
    /// District identifier from Clever API
    /// </summary>
    public string CleverDistrictId { get; set; } = string.Empty;

    /// <summary>
    /// Term display name (e.g., "Fall 2025", "Spring Semester")
    /// </summary>
    public string? Name { get; set; }

    /// <summary>
    /// Start date of the term (parsed from ISO 8601 date string)
    /// </summary>
    public DateTime? StartDate { get; set; }

    /// <summary>
    /// End date of the term (parsed from ISO 8601 date string)
    /// </summary>
    public DateTime? EndDate { get; set; }

    /// <summary>
    /// When the record was first created in our database
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the record's data last changed
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When soft-deleted (null = active).
    /// Cleared if the term reappears in Clever to support restoration.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Timestamp of the last Clever event received for this record (for incremental sync)
    /// </summary>
    public DateTime? LastEventReceivedAt { get; set; }

    /// <summary>
    /// When we last saw this record in Clever (for full sync/orphan detection)
    /// </summary>
    public DateTime? LastSyncedAt { get; set; }
}
