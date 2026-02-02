using System.Net;
using System.Text.Json;
using CleverSyncSOS.Core.Authentication;
using CleverSyncSOS.Core.CleverApi.Models;
using CleverSyncSOS.Core.Configuration;
using CleverSyncSOS.Core.Logging;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleverSyncSOS.Core.CleverApi;

/// <summary>
/// Production implementation of <see cref="ICleverApiClient"/> for Clever API v3.0.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> This client handles all communication with Clever's REST API, including:</para>
/// <list type="bullet">
///   <item><description>Authentication via OAuth 2.0 bearer tokens</description></item>
///   <item><description>Automatic pagination (cursor-based)</description></item>
///   <item><description>Rate limiting with retry logic</description></item>
///   <item><description>Error handling with exponential backoff</description></item>
/// </list>
/// 
/// <para><b>Clever API v3.0 Base URL:</b></para>
/// <para>Configured via <see cref="CleverApiConfiguration.BaseUrl"/>, defaults to <c>https://api.clever.com/v3.0</c></para>
/// 
/// <para><b>Authentication Flow:</b></para>
/// <list type="number">
///   <item><description>On each request, calls <see cref="ICleverAuthenticationService.GetTokenAsync"/> to get a valid token</description></item>
///   <item><description>The authentication service handles token caching and refresh</description></item>
///   <item><description>Bearer token is added to Authorization header</description></item>
/// </list>
/// 
/// <para><b>Pagination Strategy:</b></para>
/// <para>Clever uses cursor-based pagination with <c>starting_after</c> parameter and <c>links</c> in response:</para>
/// <list type="number">
///   <item><description>First request: Send base endpoint with <c>limit</c> parameter</description></item>
///   <item><description>Check response <c>links[]</c> for <c>rel="next"</c></description></item>
///   <item><description>If next link exists, follow its URI for the next page</description></item>
///   <item><description>Repeat until no next link is returned</description></item>
/// </list>
/// 
/// <para><b>Rate Limiting:</b></para>
/// <para>Clever returns HTTP 429 when rate limited:</para>
/// <list type="bullet">
///   <item><description>Check <c>Retry-After</c> header for recommended wait time</description></item>
///   <item><description>If no header, use <see cref="CleverApiConfiguration.RateLimitDelaySeconds"/></description></item>
///   <item><description>Wait the specified duration and retry the request</description></item>
/// </list>
/// 
/// <para><b>Error Handling:</b></para>
/// <para>Uses exponential backoff for transient failures:</para>
/// <list type="bullet">
///   <item><description>First retry: Wait <see cref="CleverApiConfiguration.BaseDelaySeconds"/> seconds</description></item>
///   <item><description>Subsequent retries: Double the delay each time</description></item>
///   <item><description>Maximum attempts: <see cref="CleverApiConfiguration.MaxRetries"/></description></item>
///   <item><description>Errors are logged with sanitized URLs (no query parameters exposed)</description></item>
/// </list>
/// 
/// <para><b>Configuration:</b></para>
/// <para>All settings come from <see cref="CleverApiConfiguration"/> (injected via IOptions):</para>
/// <list type="bullet">
///   <item><description><c>BaseUrl</c>: API base URL</description></item>
///   <item><description><c>TimeoutSeconds</c>: HTTP request timeout</description></item>
///   <item><description><c>PageSize</c>: Records per page (default: 1000)</description></item>
///   <item><description><c>MaxRetries</c>: Maximum retry attempts</description></item>
///   <item><description><c>BaseDelaySeconds</c>: Initial backoff delay</description></item>
///   <item><description><c>RateLimitDelaySeconds</c>: Default rate limit wait</description></item>
/// </list>
/// 
/// <para><b>Specification References:</b></para>
/// <list type="bullet">
///   <item><description>FR-001: Clever API Authentication</description></item>
///   <item><description>FR-005: Data Retrieval with Pagination</description></item>
///   <item><description>FR-007: Events API Integration</description></item>
///   <item><description>FR-010: Rate Limiting and Retry Logic</description></item>
/// </list>
/// </remarks>
/// <seealso cref="ICleverApiClient"/>
/// <seealso cref="ICleverAuthenticationService"/>
/// <seealso cref="CleverApiConfiguration"/>
public class CleverApiClient : ICleverApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ICleverAuthenticationService _authService;
    private readonly CleverApiConfiguration _config;
    private readonly ILogger<CleverApiClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// Initializes a new instance of the <see cref="CleverApiClient"/> class.
    /// </summary>
    /// <remarks>
    /// <para>Dependencies are injected via the DI container configured in <c>ServiceCollectionExtensions.AddCleverSync()</c>.</para>
    /// <para>The <see cref="HttpClient"/> should be provided via <c>IHttpClientFactory</c> for proper lifetime management.</para>
    /// </remarks>
    /// <param name="httpClient">HTTP client for making requests (configured with base address and timeout)</param>
    /// <param name="authService">Authentication service for obtaining OAuth tokens</param>
    /// <param name="config">API configuration options</param>
    /// <param name="logger">Logger for diagnostic output</param>
    public CleverApiClient(
        HttpClient httpClient,
        ICleverAuthenticationService authService,
        IOptions<CleverApiConfiguration> config,
        ILogger<CleverApiClient> logger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _config = config.Value;
        _logger = logger;

        _httpClient.BaseAddress = new Uri(_config.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromSeconds(_config.TimeoutSeconds);

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
    }

    /// <inheritdoc />
    public async Task<CleverSchool[]> GetSchoolsAsync(string districtId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching schools for district {DistrictId}", districtId);

        // District-app tokens use /schools endpoint (no district ID in path)
        // No leading slash - BaseAddress already ends with /v3.0
        var endpoint = "schools";
        var schools = await GetPagedDataAsync<CleverDataWrapper<CleverSchool>>(endpoint, null, null, cancellationToken);

        _logger.LogInformation("Retrieved {Count} schools for district {DistrictId}", schools.Length, districtId);
        return schools.Select(w => w.Data).ToArray();
    }

    /// <inheritdoc />
    public async Task<CleverStudent[]> GetStudentsAsync(
        string schoolId,
        DateTime? lastModified = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Fetching students for school {SchoolId} (lastModified: {LastModified})",
            schoolId,
            lastModified?.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? "null");

        // Clever API v3.0 uses /schools/{id}/users with role filter
        // No leading slash - BaseAddress already ends with /v3.0
        var endpoint = $"schools/{schoolId}/users";
        var students = await GetPagedDataAsync<CleverDataWrapper<CleverStudent>>(endpoint, lastModified, "student", cancellationToken);

        _logger.LogInformation("Retrieved {Count} students for school {SchoolId}", students.Length, schoolId);
        return students.Select(w => w.Data).ToArray();
    }

    /// <inheritdoc />
    public async Task<CleverTeacher[]> GetTeachersAsync(
        string schoolId,
        DateTime? lastModified = null,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Fetching teachers for school {SchoolId} (lastModified: {LastModified})",
            schoolId,
            lastModified?.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? "null");

        // Clever API v3.0 uses /schools/{id}/users with role filter
        // No leading slash - BaseAddress already ends with /v3.0
        var endpoint = $"schools/{schoolId}/users";
        var teachers = await GetPagedDataAsync<CleverDataWrapper<CleverTeacher>>(endpoint, lastModified, "teacher", cancellationToken);

        _logger.LogInformation("Retrieved {Count} teachers for school {SchoolId}", teachers.Length, schoolId);
        return teachers.Select(w => w.Data).ToArray();
    }

    /// <summary>
    /// Generic method to fetch paged data from Clever API with automatic pagination.
    /// </summary>
    /// <remarks>
    /// <para>Handles cursor-based pagination using <c>starting_after</c> and <c>links</c> in responses.</para>
    /// <para>Continues fetching until no <c>rel="next"</c> link is present or cancellation is requested.</para>
    /// </remarks>
    /// <typeparam name="T">The type of data items to deserialize</typeparam>
    /// <param name="endpoint">API endpoint relative to base URL</param>
    /// <param name="lastModified">Optional date filter (NOT supported by Clever API - logged as warning)</param>
    /// <param name="role">Optional role filter for users endpoint (e.g., "student", "teacher")</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Array of all items across all pages</returns>
    private async Task<T[]> GetPagedDataAsync<T>(
        string endpoint,
        DateTime? lastModified,
        string? role,
        CancellationToken cancellationToken)
    {
        var allData = new List<T>();
        string? nextUrl = null;
        var pageCount = 0;

        do
        {
            pageCount++;

            // Build URL - first page uses base endpoint with query params, subsequent pages use next link URI
            string url;
            if (nextUrl == null)
            {
                // First page: build query parameters
                var queryParams = new List<string>
                {
                    $"limit={_config.PageSize}"
                };

                if (!string.IsNullOrEmpty(role))
                {
                    queryParams.Add($"role={role}");
                }

                // NOTE: Clever API v3.0 does NOT support filtering by lastModified date via query parameters.
                // The starting_after parameter is for cursor-based pagination (record IDs), not timestamps.
                // According to Clever docs, use the Events API for incremental changes.
                // For now, we fetch all records and filter client-side (less efficient but functional).
                // See: https://dev.clever.com/docs/events-api
                // TODO: Implement Events API for true incremental syncing
                if (lastModified.HasValue)
                {
                    // Intentionally not adding query parameter - will filter client-side
                    _logger.LogWarning("lastModified filtering requested but not supported by Clever API. Fetching all records.");
                }

                // Handle endpoints that may already have query parameters
                var separator = endpoint.Contains("?") ? "&" : "?";
                url = $"{endpoint}{separator}{string.Join("&", queryParams)}";
            }
            else
            {
                // Subsequent pages: use the next link URI (remove leading /v3.0 if present)
                url = nextUrl.StartsWith("/v3.0/") ? nextUrl.Substring(6) : nextUrl;
            }

            _logger.LogDebug("Fetching page {Page} from {Endpoint}", pageCount, url);

            var response = await FetchWithRetryAsync<CleverApiResponse<T>>(url, cancellationToken);

            if (response.Data != null && response.Data.Length > 0)
            {
                allData.AddRange(response.Data);
                _logger.LogDebug("Page {Page}: Retrieved {Count} records", pageCount, response.Data.Length);
            }

            // Check for next page link
            nextUrl = response.Links?
                .FirstOrDefault(link => link.Rel == "next")?
                .Uri;

            if (nextUrl == null)
            {
                _logger.LogDebug("Pagination complete: {TotalPages} pages, {TotalRecords} records",
                    pageCount, allData.Count);
            }

        } while (nextUrl != null && !cancellationToken.IsCancellationRequested);

        return allData.ToArray();
    }

    /// <inheritdoc />
    public async Task<CleverEvent[]> GetEventsAsync(
        string? startingAfter = null,
        string? schoolId = null,
        string? recordType = null,
        int limit = 1000,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation(
            "Fetching events (startingAfter: {StartingAfter}, school: {SchoolId}, recordType: {RecordType}, limit: {Limit})",
            startingAfter ?? "null",
            schoolId ?? "null",
            recordType ?? "null",
            limit);

        var allEvents = new List<CleverEvent>();
        string? nextUrl = null;
        int pageCount = 0;
        var pageLimit = Math.Min(limit, 1000); // Clever's max per page is 1000

        do
        {
            pageCount++;
            string url;

            if (nextUrl == null)
            {
                // First page: build the initial URL with query parameters
                var queryParams = new List<string>
                {
                    $"limit={pageLimit}"
                };

                if (!string.IsNullOrEmpty(startingAfter))
                {
                    queryParams.Add($"starting_after={startingAfter}");
                }

                if (!string.IsNullOrEmpty(schoolId))
                {
                    queryParams.Add($"school={schoolId}");
                }

                if (!string.IsNullOrEmpty(recordType))
                {
                    queryParams.Add($"record_type={recordType}");
                }

                url = $"events?{string.Join("&", queryParams)}";
            }
            else
            {
                // Subsequent pages: use the next link URI (remove leading /v3.0 if present)
                url = nextUrl.StartsWith("/v3.0/") ? nextUrl.Substring(6) : nextUrl;
            }

            _logger.LogDebug("Fetching events page {Page} from {Url}", pageCount, url);

            var response = await FetchWithRetryAsync<CleverEventsResponse>(url, cancellationToken);

            // Unwrap events from the wrapper objects (Clever Events API wraps each event in { "data": {...} })
            if (response.Data != null && response.Data.Length > 0)
            {
                var pageEvents = response.Data.Select(w => w.Event).ToArray();
                allEvents.AddRange(pageEvents);
                _logger.LogDebug("Events page {Page}: Retrieved {Count} events", pageCount, pageEvents.Length);

                // DIAGNOSTIC: Log first event structure details
                if (pageEvents.Length > 0)
                {
                    var firstEvent = pageEvents[0];
                    _logger.LogInformation("DIAGNOSTIC: First event structure - Id={EventId}, Type={Type}, Data.Id={DataId}, Data.Object={DataObject}, Data.RawData={HasRawData}",
                        firstEvent.Id, firstEvent.Type, firstEvent.Data.Id, firstEvent.Data.Object,
                        firstEvent.Data.RawData.HasValue ? "present" : "null");
                }
            }

            // Check for next page link
            nextUrl = response.Links?
                .FirstOrDefault(link => link.Rel == "next")?
                .Uri;

            if (nextUrl == null)
            {
                _logger.LogDebug("Events pagination complete: {TotalPages} pages, {TotalEvents} events",
                    pageCount, allEvents.Count);
            }

        } while (nextUrl != null && !cancellationToken.IsCancellationRequested);

        _logger.LogInformation("Retrieved {Count} total events across {Pages} page(s)", allEvents.Count, pageCount);

        if (allEvents.Count > 0)
        {
            _logger.LogDebug("First event - Id: {Id}, Type: {Type}, Created: {Created}",
                allEvents[0].Id, allEvents[0].Type, allEvents[0].Created);
            _logger.LogDebug("Last event - Id: {Id}, Type: {Type}, Created: {Created}",
                allEvents[^1].Id, allEvents[^1].Type, allEvents[^1].Created);
        }

        return allEvents.ToArray();
    }

    /// <inheritdoc />
    public async Task<CleverCourse[]> GetCoursesAsync(
        string schoolId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching courses for school {SchoolId}", schoolId);

        // Clever API v3.0: Courses are at district level, not school level
        // Use /courses endpoint - courses are NOT wrapped in inner data objects
        var endpoint = "courses";
        var courses = await GetPagedDataAsync<CleverCourse>(endpoint, null, null, cancellationToken);

        _logger.LogInformation("Retrieved {Count} courses (district-level)", courses.Length);
        return courses;
    }

    /// <inheritdoc />
    public async Task<CleverSection[]> GetSectionsAsync(
        string schoolId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching sections for school {SchoolId}", schoolId);

        // Clever API v3.0: Use /schools/{id}/sections nested endpoint
        // This returns all sections whose primary school is the specified ID
        var endpoint = $"schools/{schoolId}/sections";
        var sections = await GetPagedDataAsync<CleverDataWrapper<CleverSection>>(endpoint, null, null, cancellationToken);

        var result = sections.Select(w => w.Data).ToArray();
        _logger.LogInformation("Retrieved {Count} sections for school {SchoolId}", result.Length, schoolId);
        return result;
    }

    /// <inheritdoc />
    public async Task<CleverTerm[]> GetTermsAsync(
        string schoolId,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching terms for school {SchoolId}", schoolId);

        // Clever API v3.0: Terms are at district level, not school level
        // Use /terms endpoint - terms are NOT wrapped in inner data objects (like courses)
        var endpoint = "terms";
        var terms = await GetPagedDataAsync<CleverTerm>(endpoint, null, null, cancellationToken);

        _logger.LogInformation("Retrieved {Count} terms (district-level)", terms.Length);
        return terms;
    }

    /// <inheritdoc />
    public async Task<string?> GetLatestEventIdAsync(string? schoolId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Fetching latest event ID for baseline (school: {SchoolId})", schoolId ?? "all");

        // Get the single most recent event, optionally filtered by school
        // Note: Clever's Events API returns events in reverse chronological order by default (newest first)
        // IMPORTANT: When setting a baseline for incremental sync, we must filter by school
        // because the Events API returns different results when filtered by school vs. not.
        // District-level events (courses, etc.) are excluded when filtering by school.
        var url = string.IsNullOrEmpty(schoolId)
            ? "events?limit=1"
            : $"events?limit=1&school={schoolId}";
        var response = await FetchWithRetryAsync<CleverEventsResponse>(url, cancellationToken);

        var latestEventId = response.Data?.FirstOrDefault()?.Event.Id;
        _logger.LogInformation("Latest event ID: {EventId}", latestEventId ?? "null");

        return latestEventId;
    }

    /// <summary>
    /// Fetches data with automatic retry and rate limit handling.
    /// </summary>
    /// <remarks>
    /// <para><b>Retry Strategy:</b></para>
    /// <list type="bullet">
    ///   <item><description>HTTP 429 (Rate Limited): Wait for <c>Retry-After</c> header duration, then retry</description></item>
    ///   <item><description>Transient failures: Exponential backoff (base delay � 2^attempt)</description></item>
    ///   <item><description>Maximum attempts controlled by <see cref="CleverApiConfiguration.MaxRetries"/></description></item>
    /// </list>
    /// </remarks>
    /// <typeparam name="T">Response type to deserialize</typeparam>
    /// <param name="url">Request URL (relative to base address)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Deserialized response</returns>
    /// <exception cref="InvalidOperationException">Thrown after maximum retries exceeded</exception>
    private async Task<T> FetchWithRetryAsync<T>(string url, CancellationToken cancellationToken)
    {
        var attempt = 0;

        while (attempt < _config.MaxRetries)
        {
            attempt++;

            try
            {
                // Get authentication token
                var authToken = await _authService.GetTokenAsync(cancellationToken);

                // Create request with bearer token
                var request = new HttpRequestMessage(HttpMethod.Get, url);
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", authToken.AccessToken);

                // Send request
                var response = await _httpClient.SendAsync(request, cancellationToken);

                // Handle rate limiting (HTTP 429)
                if (response.StatusCode == HttpStatusCode.TooManyRequests)
                {
                    var retryAfter = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(_config.RateLimitDelaySeconds);

                    _logger.LogWarning(
                        "Rate limited by Clever API. Waiting {RetryAfter} seconds before retry (attempt {Attempt}/{MaxRetries})",
                        retryAfter.TotalSeconds,
                        attempt,
                        _config.MaxRetries);

                    await Task.Delay(retryAfter, cancellationToken);
                    continue; // Retry
                }

                // Throw for other error status codes
                response.EnsureSuccessStatusCode();

                // Deserialize response
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                var result = JsonSerializer.Deserialize<T>(content, _jsonOptions);

                if (result == null)
                {
                    throw new InvalidOperationException($"Failed to deserialize response from {url}");
                }

                return result;
            }
            catch (HttpRequestException ex) when (attempt < _config.MaxRetries)
            {
                var delay = TimeSpan.FromSeconds(_config.BaseDelaySeconds * Math.Pow(2, attempt - 1));

                _logger.LogWarning(
                    ex,
                    "HTTP request failed (attempt {Attempt}/{MaxRetries}). Retrying in {Delay} seconds. URL: {Url}",
                    attempt,
                    _config.MaxRetries,
                    delay.TotalSeconds,
                    url);

                await Task.Delay(delay, cancellationToken);
            }
            catch (Exception ex)
            {
                // FR-010: Sanitize URL to prevent query parameter exposure
                var sanitizedUrl = SensitiveDataSanitizer.SanitizeUrl(url);
                _logger.LogSanitizedError(ex, "Failed to fetch data from Clever API: {SanitizedUrl}", null, sanitizedUrl);
                throw;
            }
        }

        throw new InvalidOperationException($"Failed to fetch data after {_config.MaxRetries} attempts: {url}");
    }
}
