using System.Text.Json;
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
    /// Event type in format "object.action" (e.g., "users.created", "sections.updated", "courses.deleted")
    /// The object part indicates the type (users, sections, courses, terms, etc.)
    /// The action part indicates what happened (created, updated, deleted)
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
    public DateTime? Created { get; set; }

    /// <summary>
    /// URI links for the event (self, data references, etc.)
    /// </summary>
    [JsonPropertyName("links")]
    public CleverLink[]? Links { get; set; }

    /// <summary>
    /// Gets the object type from the event type (e.g., "users" from "users.created")
    /// </summary>
    public string ObjectType => Type.Contains('.') ? Type.Split('.')[0] : Type;

    /// <summary>
    /// Gets the action type from the event type (e.g., "created" from "users.created")
    /// </summary>
    public string ActionType => Type.Contains('.') ? Type.Split('.')[1] : Type;
}

/// <summary>
/// Event data wrapper containing the affected object and metadata.
/// The actual structure varies by object type - use JsonElement for flexibility.
/// </summary>
public class CleverEventData
{
    /// <summary>
    /// The object ID (e.g., Clever student ID, teacher ID)
    /// </summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    /// <summary>
    /// Object type - can be a string like "student" or a complex object depending on event type.
    /// Using JsonElement to handle both cases.
    /// </summary>
    [JsonPropertyName("object")]
    public JsonElement? ObjectElement { get; set; }

    /// <summary>
    /// Gets the object type as a string (extracts from JsonElement if needed).
    /// Used by SyncService to determine how to process the event.
    /// </summary>
    [JsonIgnore]
    public string Object
    {
        get
        {
            if (ObjectElement == null) return string.Empty;
            if (ObjectElement.Value.ValueKind == JsonValueKind.String)
                return ObjectElement.Value.GetString() ?? string.Empty;
            // If it's an object, try to get a "type" property
            if (ObjectElement.Value.ValueKind == JsonValueKind.Object &&
                ObjectElement.Value.TryGetProperty("type", out var typeElement))
                return typeElement.GetString() ?? string.Empty;
            return string.Empty;
        }
    }

    /// <summary>
    /// Complete object data as JSON element for flexible deserialization.
    /// Contains the actual record (student, teacher, section, etc.)
    /// </summary>
    [JsonPropertyName("data")]
    public JsonElement? RawData { get; set; }

    /// <summary>
    /// For "updated" events: hash of fields that changed (previous values)
    /// </summary>
    [JsonPropertyName("previous_attributes")]
    public JsonElement? PreviousAttributes { get; set; }
}

/// <summary>
/// Response from Events API endpoint.
/// Clever Events API v3.0 wraps each event in a data object:
/// { "data": [ { "data": { id, type, created, data: {...} } }, ... ] }
/// </summary>
public class CleverEventsResponse
{
    /// <summary>
    /// Array of event wrappers (each containing the actual event in a "data" property)
    /// </summary>
    [JsonPropertyName("data")]
    public CleverEventWrapper[] Data { get; set; } = Array.Empty<CleverEventWrapper>();

    /// <summary>
    /// Pagination links (next, prev)
    /// </summary>
    [JsonPropertyName("links")]
    public CleverLink[]? Links { get; set; }
}

/// <summary>
/// Wrapper for each event in the Events API response.
/// The Events API returns: { "data": [ { "data": {...event...} }, ... ] }
/// </summary>
public class CleverEventWrapper
{
    /// <summary>
    /// The actual event data
    /// </summary>
    [JsonPropertyName("data")]
    public CleverEvent Event { get; set; } = new();
}
