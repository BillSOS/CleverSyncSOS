// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-001, FR-003, FR-004, FR-008, FR-011
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Core Implementation
//   version: 1.0.0
// ---

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using CleverSyncSOS.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CleverSyncSOS.Core.Authentication;

/// <summary>
/// Implementation of Clever API authentication using OAuth 2.0 client credentials flow.
/// Source: SpecKit/Specs/001-clever-api-auth/spec-1.md (FR-001, FR-003, FR-004)
/// Implements: OAuth authentication, token management, retry logic, rate limiting
/// </summary>
public class CleverAuthenticationService : ICleverAuthenticationService
{
    private readonly ICredentialStore _credentialStore;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly CleverAuthConfiguration _configuration;
    private readonly ILogger<CleverAuthenticationService> _logger;

    // FR-003: Cache tokens in memory
    private CleverAuthToken? _cachedToken;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    // FR-005: Track last successful authentication and errors
    private DateTime? _lastSuccessfulAuth;
    private string? _lastError;

    public CleverAuthenticationService(
        ICredentialStore credentialStore,
        IHttpClientFactory httpClientFactory,
        IOptions<CleverAuthConfiguration> configuration,
        ILogger<CleverAuthenticationService> logger)
    {
        _credentialStore = credentialStore;
        _httpClientFactory = httpClientFactory;
        _configuration = configuration.Value;
        _logger = logger;

        // FR-010: Structured logging
        _logger.LogInformation("CleverAuthenticationService initialized with endpoint: {TokenEndpoint}",
            _configuration.TokenEndpoint);
    }

    /// <summary>
    /// Authenticates with Clever API using OAuth 2.0 client credentials flow.
    /// Source: FR-001 - OAuth Authentication with retry logic from FR-004
    /// </summary>
    public async Task<CleverAuthToken> AuthenticateAsync(CancellationToken cancellationToken = default)
    {
        // NFR-001: Authentication must complete within 5 seconds of startup
        var authStartTime = DateTime.UtcNow;
        _logger.LogInformation("Starting Clever API authentication");

        try
        {
            // FR-002: Retrieve credentials from secure storage
            var clientId = await _credentialStore.GetClientIdAsync(cancellationToken);
            var clientSecret = await _credentialStore.GetClientSecretAsync(cancellationToken);

            // FR-001: OAuth 2.0 client credentials flow
            var token = await AuthenticateWithRetryAsync(clientId, clientSecret, cancellationToken);

            // FR-005: Track last successful authentication
            _lastSuccessfulAuth = DateTime.UtcNow;
            _lastError = null;

            var authDuration = (DateTime.UtcNow - authStartTime).TotalSeconds;
            _logger.LogInformation(
                "Successfully authenticated with Clever API in {Duration:F2}s. Token expires in {ExpiresIn}s",
                authDuration, token.ExpiresIn);

            return token;
        }
        catch (Exception ex)
        {
            // FR-005: Track last error
            _lastError = ex.Message;

            // FR-010: Structured logging with sanitization
            _logger.LogError(ex, "Failed to authenticate with Clever API");
            throw;
        }
    }

    /// <summary>
    /// Authenticates with retry logic for transient failures.
    /// Source: FR-004 - Exponential backoff retry logic
    /// </summary>
    private async Task<CleverAuthToken> AuthenticateWithRetryAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        Exception? lastException = null;

        // FR-004: Retry up to 5 times with exponential backoff (2s, 4s, 8s, 16s, 32s)
        for (int attempt = 0; attempt < _configuration.MaxRetryAttempts; attempt++)
        {
            try
            {
                return await PerformAuthenticationAsync(clientId, clientSecret, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < _configuration.MaxRetryAttempts - 1)
            {
                lastException = ex;

                // FR-008: Detect and handle HTTP 429 (rate limiting)
                if (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    _logger.LogWarning("Rate limit exceeded (HTTP 429) on attempt {Attempt}. Will retry.",
                        attempt + 1);
                }

                // FR-004: Calculate exponential backoff delay
                var delaySeconds = _configuration.InitialRetryDelaySeconds * Math.Pow(2, attempt);
                _logger.LogWarning(ex,
                    "Authentication attempt {Attempt} failed. Retrying in {Delay}s",
                    attempt + 1, delaySeconds);

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        // All retries exhausted
        _logger.LogError(lastException, "Authentication failed after {MaxAttempts} attempts",
            _configuration.MaxRetryAttempts);
        throw new InvalidOperationException(
            $"Failed to authenticate after {_configuration.MaxRetryAttempts} attempts", lastException);
    }

    /// <summary>
    /// Performs a single authentication attempt.
    /// Source: FR-001 - OAuth 2.0 client credentials flow
    /// </summary>
    private async Task<CleverAuthToken> PerformAuthenticationAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient("CleverAuth");

        // FR-011: Enforce TLS 1.2+ (configured in Infrastructure layer)
        // FR-001: OAuth 2.0 client credentials - Basic authentication
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        // FR-001: Request token with client credentials grant type
        var requestBody = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            { "grant_type", "client_credentials" }
        });

        _logger.LogDebug("Sending OAuth token request to {Endpoint}", _configuration.TokenEndpoint);

        var response = await httpClient.PostAsync(_configuration.TokenEndpoint, requestBody, cancellationToken);

        // FR-008: Handle rate limiting
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Rate limit exceeded (HTTP 429) from Clever API");
            throw new HttpRequestException("Rate limit exceeded", null, System.Net.HttpStatusCode.TooManyRequests);
        }

        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);
        var tokenResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(responseContent);

        if (tokenResponse == null || string.IsNullOrEmpty(tokenResponse.AccessToken))
        {
            throw new InvalidOperationException("Invalid token response from Clever API");
        }

        // FR-003: Create token with issuance timestamp for lifetime tracking
        return new CleverAuthToken
        {
            AccessToken = tokenResponse.AccessToken,
            TokenType = tokenResponse.TokenType ?? "Bearer",
            ExpiresIn = tokenResponse.ExpiresIn,
            IssuedAt = DateTime.UtcNow
        };
    }

    /// <summary>
    /// Gets the current cached token, refreshing if necessary.
    /// Source: FR-003 - Refresh tokens proactively at 75% of their lifetime
    /// </summary>
    public async Task<CleverAuthToken> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        // FR-003: Check if token needs refresh
        if (_cachedToken == null || _cachedToken.ShouldRefresh(_configuration.TokenRefreshThresholdPercent))
        {
            await _tokenLock.WaitAsync(cancellationToken);
            try
            {
                // Double-check after acquiring lock
                if (_cachedToken == null || _cachedToken.ShouldRefresh(_configuration.TokenRefreshThresholdPercent))
                {
                    _logger.LogInformation("Token refresh required. Current token expired or past refresh threshold.");
                    _cachedToken = await AuthenticateAsync(cancellationToken);
                }
            }
            finally
            {
                _tokenLock.Release();
            }
        }

        return _cachedToken;
    }

    /// <summary>
    /// Gets the current cached token without forcing refresh.
    /// Source: FR-003 - Token Management
    /// </summary>
    public CleverAuthToken? GetCurrentToken()
    {
        return _cachedToken;
    }

    /// <summary>
    /// Gets the timestamp of last successful authentication.
    /// Source: FR-005 - Health check endpoint requirements
    /// </summary>
    public DateTime? GetLastSuccessfulAuthTime()
    {
        return _lastSuccessfulAuth;
    }

    /// <summary>
    /// Gets the last error encountered during authentication.
    /// Source: FR-005 - Health check endpoint requirements
    /// </summary>
    public string? GetLastError()
    {
        return _lastError;
    }

    /// <summary>
    /// OAuth token response model for JSON deserialization.
    /// Source: FR-001 - OAuth 2.0 token response
    /// </summary>
    private class OAuthTokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("token_type")]
        public string? TokenType { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
    }
}
