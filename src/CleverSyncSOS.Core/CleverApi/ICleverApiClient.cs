using CleverSyncSOS.Core.CleverApi.Models;

namespace CleverSyncSOS.Core.CleverApi;

/// <summary>
/// Interface for Clever API client operations.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Defines the contract for all Clever API v3.0 communication, including:</para>
/// <list type="bullet">
///   <item><description>Data retrieval (students, teachers, sections, courses, schools)</description></item>
///   <item><description>Events API for incremental synchronization</description></item>
///   <item><description>Pagination handling</description></item>
/// </list>
/// 
/// <para><b>Clever API v3.0 Overview:</b></para>
/// <para>Clever provides a REST API for accessing school district data. Key characteristics:</para>
/// <list type="bullet">
///   <item><description><b>Authentication:</b> OAuth 2.0 Client Credentials flow (district-app tokens)</description></item>
///   <item><description><b>Base URL:</b> <c>https://api.clever.com/v3.0</c></description></item>
///   <item><description><b>Pagination:</b> Cursor-based using <c>starting_after</c> parameter</description></item>
///   <item><description><b>Rate Limiting:</b> Responds with HTTP 429 and <c>Retry-After</c> header</description></item>
///   <item><description><b>Events API:</b> Provides change stream for incremental sync</description></item>
/// </list>
/// 
/// <para><b>Data API vs Events API:</b></para>
/// <list type="bullet">
///   <item><description><b>Data API</b> (<see cref="GetStudentsAsync"/>, <see cref="GetTeachersAsync"/>, etc.):
///     Returns current state of all records. Use for full sync or initial data load.</description></item>
///   <item><description><b>Events API</b> (<see cref="GetEventsAsync"/>):
///     Returns a stream of changes (created, updated, deleted). Use for efficient incremental sync.</description></item>
/// </list>
/// 
/// <para><b>Implementation Notes:</b></para>
/// <list type="bullet">
///   <item><description>Implementations must handle pagination automatically (all methods return complete datasets)</description></item>
///   <item><description>Rate limiting should be handled with exponential backoff</description></item>
///   <item><description>Authentication tokens should be cached and refreshed as needed</description></item>
/// </list>
/// 
/// <para><b>Specification References:</b></para>
/// <list type="bullet">
///   <item><description>FR-001: Clever API Authentication</description></item>
///   <item><description>FR-005: Data Retrieval</description></item>
///   <item><description>FR-007: Events API Integration</description></item>
/// </list>
/// 
/// <para><b>Clever API Documentation:</b></para>
/// <list type="bullet">
///   <item><description><see href="https://dev.clever.com/docs/api-overview"/></description></item>
///   <item><description><see href="https://dev.clever.com/docs/events-api"/></description></item>
/// </list>
/// </remarks>
/// <seealso cref="CleverApiClient"/>
/// <seealso cref="ICleverAuthenticationService"/>
public interface ICleverApiClient
{
    /// <summary>
    /// Retrieves all schools for a district.
    /// </summary>
    /// <remarks>
    /// <para><b>API Endpoint:</b> <c>GET /v3.0/schools</c></para>
    /// <para><b>Authentication:</b> Requires district-app bearer token.</para>
    /// <para><b>Note:</b> Returns all schools the district token has access to, not filtered by <paramref name="districtId"/>.</para>
    /// </remarks>
    /// <param name="districtId">Clever district identifier (used for logging, not API filtering)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of all schools accessible to the district token</returns>
    Task<CleverSchool[]> GetSchoolsAsync(string districtId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves students for a school, optionally filtered by last modified date.
    /// </summary>
    /// <remarks>
    /// <para><b>API Endpoint:</b> <c>GET /v3.0/schools/{schoolId}/users?role=student</c></para>
    /// <para><b>Pagination:</b> Automatically fetches all pages using cursor-based pagination.</para>
    /// <para><b>Note:</b> The <paramref name="lastModified"/> filter is NOT supported by Clever API v3.0.
    /// When provided, a warning is logged and all records are fetched. Use Events API for true incremental sync.</para>
    /// </remarks>
    /// <param name="schoolId">Clever school identifier</param>
    /// <param name="lastModified">Optional: Ignored by API (logged as warning). Use Events API instead.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of all students in the school</returns>
    Task<CleverStudent[]> GetStudentsAsync(
        string schoolId,
        DateTime? lastModified = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves teachers for a school, optionally filtered by last modified date.
    /// </summary>
    /// <remarks>
    /// <para><b>API Endpoint:</b> <c>GET /v3.0/schools/{schoolId}/users?role=teacher</c></para>
    /// <para><b>Pagination:</b> Automatically fetches all pages using cursor-based pagination.</para>
    /// <para><b>Note:</b> The <paramref name="lastModified"/> filter is NOT supported by Clever API v3.0.
    /// When provided, a warning is logged and all records are fetched. Use Events API for true incremental sync.</para>
    /// </remarks>
    /// <param name="schoolId">Clever school identifier</param>
    /// <param name="lastModified">Optional: Ignored by API (logged as warning). Use Events API instead.</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of all teachers in the school</returns>
    Task<CleverTeacher[]> GetTeachersAsync(
        string schoolId,
        DateTime? lastModified = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves events from Clever's Events API for incremental sync.
    /// </summary>
    /// <remarks>
    /// <para><b>API Endpoint:</b> <c>GET /v3.0/events</c></para>
    /// 
    /// <para><b>Purpose:</b> The Events API provides a stream of changes (created, updated, deleted)
    /// to district data, enabling efficient incremental synchronization without fetching all records.</para>
    /// 
    /// <para><b>Event Types:</b></para>
    /// <list type="bullet">
    ///   <item><description><c>users.created</c>, <c>users.updated</c>, <c>users.deleted</c></description></item>
    ///   <item><description><c>sections.created</c>, <c>sections.updated</c>, <c>sections.deleted</c></description></item>
    ///   <item><description><c>courses.created</c>, <c>courses.updated</c>, <c>courses.deleted</c></description></item>
    /// </list>
    /// 
    /// <para><b>Pagination:</b> Uses cursor-based pagination via <paramref name="startingAfter"/>. 
    /// Pass the last processed event ID to get subsequent events.</para>
    /// 
    /// <para><b>School Filtering:</b> When <paramref name="schoolId"/> is provided, only events
    /// for that school are returned. District-level events (courses) are excluded.</para>
    /// 
    /// <para><b>Documentation:</b> <see href="https://dev.clever.com/docs/events-api"/></para>
    /// </remarks>
    /// <param name="startingAfter">Event ID to start after (for pagination). Null for initial request.</param>
    /// <param name="schoolId">Optional: Filter events for a specific school</param>
    /// <param name="recordType">Optional: Filter by record type (e.g., "users", "sections")</param>
    /// <param name="limit">Maximum events to return per page (default: 1000, max: 1000)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of events since the specified event ID</returns>
    Task<CleverEvent[]> GetEventsAsync(
        string? startingAfter = null,
        string? schoolId = null,
        string? recordType = null,
        int limit = 1000,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the most recent event ID for initializing incremental sync tracking.
    /// </summary>
    /// <remarks>
    /// <para><b>Purpose:</b> Call this once after a full sync to establish the baseline for future
    /// incremental syncs. The returned event ID is stored in <see cref="SyncHistory.LastEventId"/>.</para>
    /// 
    /// <para><b>School Filtering:</b> When <paramref name="schoolId"/> is provided, returns the latest
    /// event for that school only. This is important because school-filtered events exclude district-level
    /// events, so the baseline must match the filter that will be used for incremental syncs.</para>
    /// 
    /// <para><b>Initial State:</b> Returns <c>null</c> if no events exist (Events API just enabled or
    /// brand new district). In this case, incremental syncs will fall back to timestamp-based change detection.</para>
    /// </remarks>
    /// <param name="schoolId">Optional: Clever school ID to filter events (recommended)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Most recent event ID, or null if no events exist</returns>
    Task<string?> GetLatestEventIdAsync(string? schoolId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves courses for a school.
    /// </summary>
    /// <remarks>
    /// <para><b>API Endpoint:</b> <c>GET /v3.0/courses</c></para>
    /// <para><b>Note:</b> Courses are district-level entities, not school-specific.
    /// The <paramref name="schoolId"/> is used for logging context only.</para>
    /// <para><b>Authentication:</b> Requires Secure Sync district bearer token (user SSO tokens cannot access courses).</para>
    /// <para><b>Documentation:</b> <see href="https://dev.clever.com/docs/courses"/></para>
    /// </remarks>
    /// <param name="schoolId">Clever school identifier (used for logging context)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of all courses in the district</returns>
    Task<CleverCourse[]> GetCoursesAsync(
        string schoolId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves sections for a school.
    /// </summary>
    /// <remarks>
    /// <para><b>API Endpoint:</b> <c>GET /v3.0/schools/{schoolId}/sections</c></para>
    /// <para><b>Pagination:</b> Automatically fetches all pages using cursor-based pagination.</para>
    /// <para><b>Includes:</b> Section data includes <c>students[]</c> (enrolled student IDs) and
    /// <c>teachers[]</c> (assigned teacher IDs with primary teacher first).</para>
    /// <para><b>Authentication:</b> Requires Secure Sync district bearer token (user SSO tokens cannot access sections).</para>
    /// <para><b>Documentation:</b> <see href="https://dev.clever.com/docs/sections"/></para>
    /// </remarks>
    /// <param name="schoolId">Clever school identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of all sections in the school</returns>
    Task<CleverSection[]> GetSectionsAsync(
        string schoolId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves terms for a district.
    /// </summary>
    /// <remarks>
    /// <para><b>API Endpoint:</b> <c>GET /v3.0/terms</c></para>
    /// <para><b>Note:</b> Terms are district-level entities, not school-specific.
    /// The <paramref name="schoolId"/> is used for logging context only.</para>
    /// <para><b>Authentication:</b> Requires Secure Sync district bearer token.</para>
    /// <para><b>Documentation:</b> <see href="https://dev.clever.com/docs/terms"/></para>
    /// </remarks>
    /// <param name="schoolId">Clever school identifier (used for logging context)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of all terms in the district</returns>
    Task<CleverTerm[]> GetTermsAsync(
        string schoolId,
        CancellationToken cancellationToken = default);
}
