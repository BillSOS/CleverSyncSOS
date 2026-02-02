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
using CleverSyncSOS.Core.Logging;
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
            // Use global secrets for system-wide Clever API access
            var clientId = await _credentialStore.GetGlobalSecretAsync(
                Configuration.KeyVaultSecretNaming.Global.ClientId,
                cancellationToken);
            var clientSecret = await _credentialStore.GetGlobalSecretAsync(
                Configuration.KeyVaultSecretNaming.Global.ClientSecret,
                cancellationToken);

            // FR-001: OAuth 2.0 client credentials flow
            var token = await AuthenticateWithRetryAsync(clientId, clientSecret, cancellationToken);

            // FR-005: Track last successful authentication
            _lastSuccessfulAuth = DateTime.UtcNow;
            _lastError = null;

            var authDuration = (DateTime.UtcNow - authStartTime).TotalSeconds;

            // FR-010: Structured logging per SpecKit Observability.md
            _logger.LogCleverAuthTokenAcquired(
                expiresAt: token.IssuedAt.AddSeconds(token.ExpiresIn),
                scope: "district");

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
            _logger.LogCleverAuthFailure(ex, retryCount: 0);
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
        // FR-002: Check if a pre-generated access token exists in Key Vault first
        // Clever district-app tokens can be generated once in dashboard and used directly
        try
        {
            // Check for pre-generated access token (optional, for districts with bearer tokens)
            var preGeneratedToken = await _credentialStore.GetGlobalSecretAsync(
                Configuration.KeyVaultSecretNaming.Global.AccessToken,
                cancellationToken);
            if (!string.IsNullOrEmpty(preGeneratedToken))
            {
                _logger.LogInformation("Using pre-generated Clever access token from Key Vault");

                // Clever district tokens don't expire, use sentinel value 0
                return new CleverAuthToken
                {
                    AccessToken = preGeneratedToken,
                    TokenType = "Bearer",
                    ExpiresIn = 0, // Sentinel: non-expiring token
                    IssuedAt = DateTime.UtcNow
                };
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No pre-generated token found in Key Vault (CleverSyncSOS--Clever--AccessToken), falling back to OAuth flow");
        }

        // Fall back to OAuth authentication flow with retries
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
                _logger.LogSanitizedWarning(
                    "Authentication attempt {Attempt} failed. Retrying in {Delay}s",
                    null, // correlationId
                    attempt + 1, delaySeconds);

                await Task.Delay(TimeSpan.FromSeconds(delaySeconds), cancellationToken);
            }
        }

        // All retries exhausted
        if (lastException != null)
        {
            _logger.LogCleverAuthFailure(lastException, _configuration.MaxRetryAttempts);
        }
        throw new InvalidOperationException(
            $"Failed to authenticate after {_configuration.MaxRetryAttempts} attempts", lastException);
    }

    /// <summary>
    /// Retrieves district-app tokens from Clever API.
    /// Source: Clever API uses district-app tokens, not OAuth client credentials flow
    /// See: https://dev.clever.com/docs/onboarding#obtaining-district-app-tokens
    /// </summary>
    private async Task<CleverAuthToken> PerformAuthenticationAsync(
        string clientId,
        string clientSecret,
        CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient("CleverAuth");

        // FR-011: Enforce TLS 1.2+ (configured in Infrastructure layer)
        // Clever district-app tokens use Basic authentication with GET request
        // See: https://dev.clever.com/docs/onboarding#obtaining-district-app-tokens
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));
        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);

        _logger.LogDebug("Retrieving district-app tokens from {Endpoint}", _configuration.TokenEndpoint);
        // FR-010: REMOVED client ID logging to prevent credential leakage

        // GET request for district-app tokens (not OAuth flow)
        var response = await httpClient.GetAsync(_configuration.TokenEndpoint, cancellationToken);

        // FR-008: Handle rate limiting
        if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        {
            _logger.LogWarning("Rate limit exceeded (HTTP 429) from Clever API");
            throw new HttpRequestException("Rate limit exceeded", null, System.Net.HttpStatusCode.TooManyRequests);
        }

        // Log detailed error information for non-success responses
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync(cancellationToken);
            // FR-010: Sanitize HTTP response to prevent credential leakage
            var sanitizedResponse = SensitiveDataSanitizer.SanitizeHttpResponse(errorContent);
            _logger.LogError(
                "Token retrieval failed with status {StatusCode}. Response: {SanitizedResponse}",
                response.StatusCode, sanitizedResponse);
        }

        response.EnsureSuccessStatusCode();

        var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

        // Try parsing as standard OAuth 2.0 response first
        try
        {
            var oauthResponse = JsonSerializer.Deserialize<OAuthTokenResponse>(responseContent);
            if (oauthResponse != null && !string.IsNullOrEmpty(oauthResponse.AccessToken))
            {
                _logger.LogInformation("Retrieved OAuth token (expires in {ExpiresIn}s)", oauthResponse.ExpiresIn);

                return new CleverAuthToken
                {
                    AccessToken = oauthResponse.AccessToken,
                    TokenType = oauthResponse.TokenType ?? "Bearer",
                    ExpiresIn = oauthResponse.ExpiresIn,
                    IssuedAt = DateTime.UtcNow
                };
            }
        }
        catch (JsonException)
        {
            // Fall back to district token response format
        }

        // Try parsing as district token response (old format)
        var tokenResponse = JsonSerializer.Deserialize<DistrictTokenResponse>(responseContent);

        if (tokenResponse == null || tokenResponse.Data == null || tokenResponse.Data.Count == 0)
        {
            throw new InvalidOperationException("No district tokens found for this application");
        }

        // Use the first active token
        var firstToken = tokenResponse.Data[0];

        _logger.LogInformation("Retrieved district token for district {DistrictId}",
            firstToken.District ?? "unknown");

        // Clever district tokens don't expire, use sentinel value 0
        return new CleverAuthToken
        {
            AccessToken = firstToken.AccessToken,
            TokenType = "Bearer",
            ExpiresIn = 0, // Sentinel: non-expiring token
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
    /// Standard OAuth 2.0 token response model.
    /// Used for client_credentials grant type.
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

    /// <summary>
    /// District token response model for JSON deserialization.
    /// Source: Clever API GET /oauth/tokens response
    /// See: https://dev.clever.com/docs/onboarding#obtaining-district-app-tokens
    /// </summary>
    private class DistrictTokenResponse
    {
        [System.Text.Json.Serialization.JsonPropertyName("data")]
        public List<DistrictToken> Data { get; set; } = new();
    }

    /// <summary>
    /// Individual district token model.
    /// </summary>
    private class DistrictToken
    {
        [System.Text.Json.Serialization.JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("district")]
        public string? District { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;

        [System.Text.Json.Serialization.JsonPropertyName("created")]
        public string? Created { get; set; }
    }
}
