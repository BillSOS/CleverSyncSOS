using System.Text.Json.Serialization;

namespace CleverSyncSOS.Core.CleverApi.Models;

/// <summary>
/// Represents a term from Clever API v3.0
/// Source: https://dev.clever.com/docs/terms
/// </summary>
/// <remarks>
/// <para>Terms are district-level entities representing academic periods (semesters, quarters, etc.).</para>
/// <para>The /terms endpoint is district-level, not school-filtered.</para>
/// </remarks>
public class CleverTerm
{
    /// <summary>
    /// Clever's unique term identifier (24-character ObjectID)
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Parent district ID
    /// </summary>
    [JsonPropertyName("district")]
    public string District { get; set; } = string.Empty;

    /// <summary>
    /// Term name (optional, provided by district)
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Term start date in ISO 8601 format (YYYY-MM-DD)
    /// </summary>
    [JsonPropertyName("start_date")]
    public string? StartDate { get; set; }

    /// <summary>
    /// Term end date in ISO 8601 format (YYYY-MM-DD)
    /// </summary>
    [JsonPropertyName("end_date")]
    public string? EndDate { get; set; }

    /// <summary>
    /// Timestamp when created in Clever
    /// </summary>
    [JsonPropertyName("created")]
    public DateTime? Created { get; set; }

    /// <summary>
    /// Timestamp when last modified in Clever
    /// </summary>
    [JsonPropertyName("last_modified")]
    public DateTime? LastModified { get; set; }

    /// <summary>
    /// Navigation links
    /// </summary>
    [JsonPropertyName("links")]
    public CleverLink[]? Links { get; set; }
}

/// <summary>
/// Response from /terms endpoint
/// </summary>
public class CleverTermsResponse
{
    /// <summary>
    /// Array of terms
    /// </summary>
    [JsonPropertyName("data")]
    public CleverTerm[] Data { get; set; } = Array.Empty<CleverTerm>();

    /// <summary>
    /// Pagination links
    /// </summary>
    [JsonPropertyName("links")]
    public CleverLink[]? Links { get; set; }
}
