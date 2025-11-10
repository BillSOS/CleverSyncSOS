// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-007 Configuration
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Core Implementation
//   version: 1.0.0
// ---

namespace CleverSyncSOS.Core.Configuration;

/// <summary>
/// Configuration model for Clever API authentication.
/// Source: SpecKit/Specs/001-clever-api-auth/spec-1.md (FR-007)
/// Requirement: Retry intervals, timeouts, and Clever endpoints must be externally configurable.
/// </summary>
public class CleverAuthConfiguration
{
    /// <summary>
    /// Azure Key Vault URI where credentials are stored.
    /// Source: FR-002 - Store Client ID and Client Secret in Azure Key Vault.
    /// </summary>
    public string KeyVaultUri { get; set; } = string.Empty;

    /// <summary>
    /// Key Vault secret name for Clever Client ID.
    /// Source: FR-002 - Credential Storage
    /// </summary>
    public string ClientIdSecretName { get; set; } = "CleverClientId";

    /// <summary>
    /// Key Vault secret name for Clever Client Secret.
    /// Source: FR-002 - Credential Storage
    /// </summary>
    public string ClientSecretSecretName { get; set; } = "CleverClientSecret";

    /// <summary>
    /// Clever OAuth token endpoint URL.
    /// Source: FR-001 - OAuth Authentication, FR-007 - Configuration
    /// </summary>
    public string TokenEndpoint { get; set; } = "https://clever.com/oauth/tokens";

    /// <summary>
    /// Clever API base URL.
    /// Source: FR-007 - Configurable endpoints
    /// </summary>
    public string ApiBaseUrl { get; set; } = "https://api.clever.com";

    /// <summary>
    /// Token refresh threshold as percentage of lifetime (default 75%).
    /// Source: FR-003 - Refresh tokens proactively at 75% of their lifetime.
    /// </summary>
    public double TokenRefreshThresholdPercent { get; set; } = 75.0;

    /// <summary>
    /// Maximum number of retry attempts for transient failures.
    /// Source: FR-004 - Retry up to 5 times with increasing delay.
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 5;

    /// <summary>
    /// Initial retry delay in seconds.
    /// Source: FR-004 - Exponential backoff starting at 2s.
    /// </summary>
    public int InitialRetryDelaySeconds { get; set; } = 2;

    /// <summary>
    /// HTTP request timeout in seconds.
    /// Source: FR-007 - Timeouts must be externally configurable, NFR-001 - Authentication within 5 seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Connection timeout in seconds for HTTP requests.
    /// Source: FR-007 - Timeouts must be externally configurable
    /// </summary>
    public int ConnectionTimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Health check cache duration in seconds.
    /// Source: Plan.md - Cached health status (updated every 30s)
    /// </summary>
    public int HealthCheckCacheDurationSeconds { get; set; } = 30;
}
