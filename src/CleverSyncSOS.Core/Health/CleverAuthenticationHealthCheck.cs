// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-005, NFR-001
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Health & Observability
//   version: 1.0.0
// ---

using CleverSyncSOS.Core.Authentication;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Core.Health;

/// <summary>
/// Health check for Clever API authentication status.
/// Source: SpecKit/Specs/001-clever-api-auth/spec-1.md (FR-005)
/// NFR-001: Health check must respond in < 100ms
/// </summary>
public class CleverAuthenticationHealthCheck : IHealthCheck
{
    private readonly ICleverAuthenticationService _authService;
    private readonly ILogger<CleverAuthenticationHealthCheck> _logger;

    // FR-005: Cache health status and update every 30 seconds
    private static HealthCheckResult? _cachedResult;
    private static DateTime _lastCheckTime = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(30);
    private static readonly SemaphoreSlim _cacheLock = new(1, 1);

    public CleverAuthenticationHealthCheck(
        ICleverAuthenticationService authService,
        ILogger<CleverAuthenticationHealthCheck> logger)
    {
        _authService = authService;
        _logger = logger;
    }

    /// <summary>
    /// Checks the health of Clever API authentication.
    /// Source: FR-005 - Health Check Endpoint
    /// NFR-001: Must respond in < 100ms
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            // FR-005: Return cached result if still valid (< 30 seconds old)
            if (_cachedResult.HasValue &&
                DateTime.UtcNow - _lastCheckTime < CacheDuration)
            {
                var cachedDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogDebug("Returning cached health check result (age: {Age}s, duration: {Duration}ms)",
                    (DateTime.UtcNow - _lastCheckTime).TotalSeconds, cachedDuration);
                return _cachedResult.Value;
            }

            await _cacheLock.WaitAsync(cancellationToken);
            try
            {
                // Double-check after acquiring lock
                if (_cachedResult.HasValue &&
                    DateTime.UtcNow - _lastCheckTime < CacheDuration)
                {
                    return _cachedResult.Value;
                }

                // Perform actual health check
                var result = await PerformHealthCheckAsync(cancellationToken);

                // Update cache
                _cachedResult = result;
                _lastCheckTime = DateTime.UtcNow;

                var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogInformation("Health check completed in {Duration}ms with status: {Status}",
                    duration, result.Status);

                // NFR-001: Warn if health check took longer than 100ms
                if (duration > 100)
                {
                    _logger.LogWarning("Health check exceeded 100ms target: {Duration}ms", duration);
                }

                return result;
            }
            finally
            {
                _cacheLock.Release();
            }
        }
        catch (Exception ex)
        {
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Health check failed after {Duration}ms", duration);

            return HealthCheckResult.Unhealthy(
                "Health check failed with exception",
                ex,
                new Dictionary<string, object>
                {
                    { "duration_ms", duration },
                    { "error", ex.Message }
                });
        }
    }

    /// <summary>
    /// Performs the actual health check logic.
    /// Source: FR-005 - Return last successful authentication timestamp and error status
    /// </summary>
    private async Task<HealthCheckResult> PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        var lastAuthTime = _authService.GetLastSuccessfulAuthTime();
        var lastError = _authService.GetLastError();
        var currentToken = _authService.GetCurrentToken();

        var data = new Dictionary<string, object>();

        // FR-005: Return last successful authentication timestamp
        if (lastAuthTime.HasValue)
        {
            data["last_auth_time_utc"] = lastAuthTime.Value;
            data["time_since_last_auth_seconds"] = (DateTime.UtcNow - lastAuthTime.Value).TotalSeconds;
        }
        else
        {
            data["last_auth_time_utc"] = "Never";
            data["time_since_last_auth_seconds"] = -1;
        }

        // FR-005: Return error status
        if (!string.IsNullOrEmpty(lastError))
        {
            data["last_error"] = lastError;
        }

        // Check token status
        if (currentToken != null)
        {
            data["has_token"] = true;
            data["token_expires_at_utc"] = currentToken.ExpiresAt;
            data["token_is_expired"] = currentToken.IsExpired;
            data["token_should_refresh"] = currentToken.ShouldRefresh(75.0);

            if (currentToken.ExpiresIn > 0)
            {
                data["token_time_until_expiration_seconds"] = currentToken.TimeUntilExpiration.TotalSeconds;
            }
            else
            {
                data["token_type"] = "non-expiring";
            }
        }
        else
        {
            data["has_token"] = false;
        }

        // Determine health status
        HealthStatus status;
        string description;

        if (currentToken == null)
        {
            // No token - try to get one
            try
            {
                _logger.LogDebug("No cached token found, attempting authentication for health check");
                await _authService.GetTokenAsync(cancellationToken);

                status = HealthStatus.Healthy;
                description = "Authentication successful";
            }
            catch (Exception ex)
            {
                status = HealthStatus.Unhealthy;
                description = $"Authentication failed: {ex.Message}";
                data["auth_error"] = ex.Message;
            }
        }
        else if (currentToken.IsExpired)
        {
            status = HealthStatus.Degraded;
            description = "Token is expired but will be refreshed on next request";
        }
        else if (!string.IsNullOrEmpty(lastError))
        {
            // Had an error but have a valid token
            status = HealthStatus.Degraded;
            description = $"Previous error: {lastError}, but current token is valid";
        }
        else
        {
            status = HealthStatus.Healthy;
            description = "Authentication is healthy";
        }

        data["status"] = status.ToString();
        data["description"] = description;

        return new HealthCheckResult(status, description, data: data);
    }

    /// <summary>
    /// Clears the cached health check result.
    /// Useful for testing or forcing an immediate refresh.
    /// </summary>
    public static void ClearCache()
    {
        _cachedResult = null;
        _lastCheckTime = DateTime.MinValue;
    }
}
