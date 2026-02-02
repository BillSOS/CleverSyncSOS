using CleverSyncSOS.Core.Database.SessionDb.Entities;

namespace CleverSyncSOS.AdminPortal.Services;

/// <summary>
/// Service for checking and logging Clever Events API status
/// </summary>
public interface IEventsCheckService
{
    /// <summary>
    /// Checks Clever Events API and logs the results to the database
    /// </summary>
    /// <param name="checkedBy">User identifier who initiated the check</param>
    /// <returns>The events log entry with check results</returns>
    Task<EventsLog> CheckAndLogEventsAsync(string? checkedBy = null);

    /// <summary>
    /// Gets recent events check history
    /// </summary>
    /// <param name="count">Number of records to retrieve</param>
    /// <returns>List of recent events log entries</returns>
    Task<List<EventsLog>> GetRecentChecksAsync(int count = 20);

    /// <summary>
    /// Gets the most recent successful check that found events
    /// </summary>
    /// <returns>The most recent check with events, or null if none found</returns>
    Task<EventsLog?> GetLastCheckWithEventsAsync();

    /// <summary>
    /// Initializes the event baseline for a school by storing the current latest event ID.
    /// This marks all current events as "processed" so future incremental syncs only process new events.
    /// </summary>
    /// <param name="schoolId">The school ID to initialize baseline for</param>
    /// <param name="initializedBy">User who initiated the action</param>
    /// <returns>The latest event ID that was stored as baseline, or null if no events exist</returns>
    Task<string?> InitializeEventBaselineAsync(int schoolId, string? initializedBy = null);

    /// <summary>
    /// Gets the current event baseline status for all schools
    /// </summary>
    /// <returns>Dictionary of school ID to their last stored event ID (null if no baseline)</returns>
    Task<Dictionary<int, string?>> GetEventBaselineStatusAsync();
}
