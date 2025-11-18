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
/// Client for interacting with Clever API v3.0.
/// Handles authentication, pagination, rate limiting, and retries.
/// </summary>
public class CleverApiClient : ICleverApiClient
{
    private readonly HttpClient _httpClient;
    private readonly ICleverAuthenticationService _authService;
    private readonly CleverApiConfiguration _config;
    private readonly ILogger<CleverApiClient> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

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
    /// Generic method to fetch paged data from Clever API.
    /// Handles cursor-based pagination using 'starting_after' and 'links', rate limiting, and authentication.
    /// </summary>
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

                if (lastModified.HasValue)
                {
                    var lastModifiedUtc = lastModified.Value.ToUniversalTime();
                    queryParams.Add($"starting_after={lastModifiedUtc:yyyy-MM-ddTHH:mm:ssZ}");
                }

                url = $"{endpoint}?{string.Join("&", queryParams)}";
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

    /// <summary>
    /// Fetches data with automatic retry and rate limit handling.
    /// </summary>
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
