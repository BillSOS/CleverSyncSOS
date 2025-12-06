using CleverSyncSOS.Core.CleverApi.Models;

namespace CleverSyncSOS.Core.CleverApi;

/// <summary>
/// Interface for Clever API client operations.
/// Handles data retrieval from Clever API v3.0 with pagination and rate limiting.
/// </summary>
public interface ICleverApiClient
{
    /// <summary>
    /// Retrieves all schools for a district.
    /// </summary>
    /// <param name="districtId">Clever district identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of schools</returns>
    Task<CleverSchool[]> GetSchoolsAsync(string districtId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves students for a school, optionally filtered by last modified date.
    /// </summary>
    /// <param name="schoolId">Clever school identifier</param>
    /// <param name="lastModified">Optional: Only return students modified after this date (for incremental sync)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of students</returns>
    Task<CleverStudent[]> GetStudentsAsync(
        string schoolId,
        DateTime? lastModified = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves teachers for a school, optionally filtered by last modified date.
    /// </summary>
    /// <param name="schoolId">Clever school identifier</param>
    /// <param name="lastModified">Optional: Only return teachers modified after this date (for incremental sync)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of teachers</returns>
    Task<CleverTeacher[]> GetTeachersAsync(
        string schoolId,
        DateTime? lastModified = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves events from Clever's Events API for incremental sync.
    /// Events represent changes (created, updated, deleted) to district data.
    /// Documentation: https://dev.clever.com/docs/events-api
    /// </summary>
    /// <param name="startingAfter">Event ID to start after (for pagination). Use null or empty for initial request.</param>
    /// <param name="schoolId">Optional: Filter events for a specific school</param>
    /// <param name="recordType">Optional: Filter events for specific record type (e.g., "users", "sections")</param>
    /// <param name="limit">Maximum number of events to return (default: 1000, max: 1000)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of events</returns>
    Task<CleverEvent[]> GetEventsAsync(
        string? startingAfter = null,
        string? schoolId = null,
        string? recordType = null,
        int limit = 1000,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent event ID for initializing incremental sync tracking.
    /// This should be called once before the first sync to establish a baseline.
    /// Documentation: https://dev.clever.com/docs/events-api
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Most recent event ID, or null if no events exist</returns>
    Task<string?> GetLatestEventIdAsync(CancellationToken cancellationToken = default);
}
