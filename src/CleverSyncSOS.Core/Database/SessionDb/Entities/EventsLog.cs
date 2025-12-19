namespace CleverSyncSOS.Core.Database.SessionDb.Entities;

/// <summary>
/// Logs Clever Events API checks to track when events become available.
/// Helps administrators know when CSV changes have been processed by Clever.
/// </summary>
public class EventsLog
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public int EventsLogId { get; set; }

    /// <summary>
    /// Timestamp when the check was performed.
    /// </summary>
    public DateTime CheckedAt { get; set; }

    /// <summary>
    /// User who initiated the check (email or identifier).
    /// </summary>
    public string? CheckedBy { get; set; }

    /// <summary>
    /// Whether the Events API was accessible.
    /// </summary>
    public bool ApiAccessible { get; set; }

    /// <summary>
    /// Total number of events found.
    /// </summary>
    public int EventCount { get; set; }

    /// <summary>
    /// Number of 'created' events found.
    /// </summary>
    public int CreatedCount { get; set; }

    /// <summary>
    /// Number of 'updated' events found.
    /// </summary>
    public int UpdatedCount { get; set; }

    /// <summary>
    /// Number of 'deleted' events found.
    /// </summary>
    public int DeletedCount { get; set; }

    /// <summary>
    /// Latest event ID from the check (for reference).
    /// </summary>
    public string? LatestEventId { get; set; }

    /// <summary>
    /// Earliest event timestamp in the batch.
    /// </summary>
    public DateTime? EarliestEventTime { get; set; }

    /// <summary>
    /// Latest event timestamp in the batch.
    /// </summary>
    public DateTime? LatestEventTime { get; set; }

    /// <summary>
    /// Summary of object types affected (e.g., "students: 5, teachers: 2, sections: 3").
    /// </summary>
    public string? ObjectTypeSummary { get; set; }

    /// <summary>
    /// Error message if the check failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// JSON snapshot of sample events (first few events for debugging).
    /// </summary>
    public string? SampleEventsJson { get; set; }
}
