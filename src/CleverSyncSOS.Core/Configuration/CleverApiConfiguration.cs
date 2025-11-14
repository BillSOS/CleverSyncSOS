namespace CleverSyncSOS.Core.Configuration;

/// <summary>
/// Configuration settings for Clever API client.
/// </summary>
public class CleverApiConfiguration
{
    /// <summary>
    /// Base URL for Clever API (default: https://api.clever.com/v3.0)
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.clever.com/v3.0";

    /// <summary>
    /// Number of records to fetch per page (default: 100, max: 100)
    /// </summary>
    public int PageSize { get; set; } = 100;

    /// <summary>
    /// Maximum number of retry attempts for transient failures (default: 5)
    /// </summary>
    public int MaxRetries { get; set; } = 5;

    /// <summary>
    /// Base delay in seconds for exponential backoff (default: 2)
    /// </summary>
    public int BaseDelaySeconds { get; set; } = 2;

    /// <summary>
    /// Timeout in seconds for HTTP requests (default: 30)
    /// </summary>
    public int TimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Additional delay in seconds when rate limited (HTTP 429) beyond Retry-After header
    /// </summary>
    public int RateLimitDelaySeconds { get; set; } = 5;
}
