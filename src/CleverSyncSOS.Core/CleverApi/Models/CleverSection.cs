using System.Text.Json.Serialization;

namespace CleverSyncSOS.Core.CleverApi.Models;

/// <summary>
/// Represents a section (class) from Clever API v3.0
/// Source: https://dev.clever.com/docs/sections
/// </summary>
public class CleverSection
{
    /// <summary>
    /// Clever's unique section identifier (ObjectID)
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Parent district ID
    /// </summary>
    [JsonPropertyName("district")]
    public string District { get; set; } = string.Empty;

    /// <summary>
    /// Parent school ID (required)
    /// </summary>
    [JsonPropertyName("school")]
    public string School { get; set; } = string.Empty;

    /// <summary>
    /// Associated course ID (optional)
    /// </summary>
    [JsonPropertyName("course")]
    public string? Course { get; set; }

    /// <summary>
    /// Associated term ID (optional)
    /// </summary>
    [JsonPropertyName("term_id")]
    public string? TermId { get; set; }

    /// <summary>
    /// Section name (required, auto-generated or from SIS)
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Internal SIS identifier (required)
    /// </summary>
    [JsonPropertyName("sis_id")]
    public string SisId { get; set; } = string.Empty;

    /// <summary>
    /// School/district section number (optional)
    /// </summary>
    [JsonPropertyName("section_number")]
    public string? SectionNumber { get; set; }

    /// <summary>
    /// Bell schedule period information (optional)
    /// </summary>
    [JsonPropertyName("period")]
    public string? Period { get; set; }

    /// <summary>
    /// Subject (required)
    /// </summary>
    [JsonPropertyName("subject")]
    public string Subject { get; set; } = string.Empty;

    /// <summary>
    /// Normalized grade level (optional)
    /// </summary>
    [JsonPropertyName("grade")]
    public string? Grade { get; set; }

    /// <summary>
    /// Array of enrolled student IDs (required)
    /// </summary>
    [JsonPropertyName("students")]
    public string[] Students { get; set; } = Array.Empty<string>();

    /// <summary>
    /// Primary teacher ID (required)
    /// </summary>
    [JsonPropertyName("teacher")]
    public string Teacher { get; set; } = string.Empty;

    /// <summary>
    /// Array of all teacher IDs, with primary teacher first (required)
    /// </summary>
    [JsonPropertyName("teachers")]
    public string[] Teachers { get; set; } = Array.Empty<string>();

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
    /// Custom extension fields from the district
    /// </summary>
    [JsonPropertyName("ext")]
    public Dictionary<string, object>? Extension { get; set; }

    /// <summary>
    /// Navigation links
    /// </summary>
    [JsonPropertyName("links")]
    public CleverLink[]? Links { get; set; }
}

/// <summary>
/// Response from /schools/{id}/sections endpoint
/// </summary>
public class CleverSectionsResponse
{
    /// <summary>
    /// Array of sections
    /// </summary>
    [JsonPropertyName("data")]
    public CleverSection[] Data { get; set; } = Array.Empty<CleverSection>();

    /// <summary>
    /// Pagination links
    /// </summary>
    [JsonPropertyName("links")]
    public CleverLink[]? Links { get; set; }
}
