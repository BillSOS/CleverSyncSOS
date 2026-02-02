using System.Text.Json.Serialization;

namespace CleverSyncSOS.Core.CleverApi.Models;

/// <summary>
/// Represents a course from Clever API v3.0
/// Source: https://dev.clever.com/docs/courses
/// </summary>
public class CleverCourse
{
    /// <summary>
    /// Clever's unique course identifier (ObjectID)
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Parent district ID
    /// </summary>
    [JsonPropertyName("district")]
    public string District { get; set; } = string.Empty;

    /// <summary>
    /// Course name (optional, provided by district)
    /// </summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>
    /// Course number/code (optional, provided by district)
    /// Note: Courses don't have sis_id field like other entities
    /// </summary>
    [JsonPropertyName("number")]
    public string? Number { get; set; }

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
/// Response from /schools/{id}/courses endpoint
/// </summary>
public class CleverCoursesResponse
{
    /// <summary>
    /// Array of courses
    /// </summary>
    [JsonPropertyName("data")]
    public CleverCourse[] Data { get; set; } = Array.Empty<CleverCourse>();

    /// <summary>
    /// Pagination links
    /// </summary>
    [JsonPropertyName("links")]
    public CleverLink[]? Links { get; set; }
}
