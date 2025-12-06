using System.Text.Json.Serialization;

namespace CleverSyncSOS.Core.CleverApi.Models;

/// <summary>
/// Represents an event from Clever's Events API.
/// Events describe changes to district data (created, updated, deleted).
/// Documentation: https://dev.clever.com/docs/events-api
/// </summary>
public class CleverEvent
{
    /// <summary>
    /// Unique identifier for this event (NOT the same as the data object's ID).
    /// Use this ID for pagination with starting_after parameter.
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Event type: "created", "updated", or "deleted"
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    /// <summary>
    /// Complete JSON representation of the affected object (student, teacher, etc.)
    /// For "updated" events, includes previous_attributes hash.
    /// For "deleted" events, contains the object as it existed before deletion.
    /// </summary>
    [JsonPropertyName("data")]
    public CleverEventData Data { get; set; } = new();

    /// <summary>
    /// Timestamp when the event was generated (informational only).
    /// Use event ID for ordering, not this timestamp.
    /// </summary>
    [JsonPropertyName("created")]
    public DateTime Created { get; set; }

    /// <summary>
    /// URI links for the event (self, data references, etc.)
    /// </summary>
    [JsonPropertyName("links")]
    public CleverLink[]? Links { get; set; }
}

/// <summary>
/// Event data wrapper containing the affected object and metadata.
/// </summary>
public class CleverEventData
{
    /// <summary>
    /// The object ID (e.g., Clever student ID, teacher ID)
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Object type (e.g., "student", "teacher", "section")
    /// </summary>
    [JsonPropertyName("object")]
    public string Object { get; set; } = string.Empty;

    /// <summary>
    /// Complete object data as JSON
    /// Must be deserialized to specific type (CleverStudent, CleverTeacher, etc.)
    /// </summary>
    [JsonPropertyName("data")]
    public object? RawData { get; set; }

    /// <summary>
    /// For "updated" events: hash of fields that changed (previous values)
    /// </summary>
    [JsonPropertyName("previous_attributes")]
    public Dictionary<string, object>? PreviousAttributes { get; set; }
}

/// <summary>
/// Response from Events API endpoint
/// </summary>
public class CleverEventsResponse
{
    /// <summary>
    /// Array of events
    /// </summary>
    [JsonPropertyName("data")]
    public CleverEvent[] Data { get; set; } = Array.Empty<CleverEvent>();

    /// <summary>
    /// Pagination links (next, prev)
    /// </summary>
    [JsonPropertyName("links")]
    public CleverLink[]? Links { get; set; }
}
