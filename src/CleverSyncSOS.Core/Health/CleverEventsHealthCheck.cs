// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-005, NFR-001
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Health & Observability
//   version: 1.0.0
// ---

using CleverSyncSOS.Core.CleverApi;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Core.Health;

/// <summary>
/// Health check for Clever Events API availability.
/// Verifies that the Events API is accessible and returns the latest event ID.
/// </summary>
public class CleverEventsHealthCheck : IHealthCheck
{
    private readonly ICleverApiClient _cleverClient;
    private readonly ILogger<CleverEventsHealthCheck> _logger;

    // Cache health status for 5 minutes (Events API status doesn't change frequently)
    private static HealthCheckResult? _cachedResult;
    private static DateTime _lastCheckTime = DateTime.MinValue;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim _cacheLock = new(1, 1);

    public CleverEventsHealthCheck(
        ICleverApiClient cleverClient,
        ILogger<CleverEventsHealthCheck> logger)
    {
        _cleverClient = cleverClient;
        _logger = logger;
    }

    /// <summary>
    /// Checks the health of Clever Events API.
    /// </summary>
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            // Return cached result if still valid
            if (_cachedResult.HasValue &&
                DateTime.UtcNow - _lastCheckTime < CacheDuration)
            {
                _logger.LogDebug("Returning cached Events API health check result (age: {Age}s)",
                    (DateTime.UtcNow - _lastCheckTime).TotalSeconds);
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
                _logger.LogInformation("Events API health check completed in {Duration}ms with status: {Status}",
                    duration, result.Status);

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
            _logger.LogError(ex, "Events API health check failed after {Duration}ms", duration);

            return HealthCheckResult.Unhealthy(
                "Events API health check failed with exception",
                ex,
                new Dictionary<string, object>
                {
                    { "duration_ms", duration },
                    { "error", ex.Message }
                });
        }
    }

    /// <summary>
    /// Performs the actual Events API health check.
    /// </summary>
    private async Task<HealthCheckResult> PerformHealthCheckAsync(CancellationToken cancellationToken)
    {
        var data = new Dictionary<string, object>();

        try
        {
            // Try to fetch the latest event ID
            var latestEventId = await _cleverClient.GetLatestEventIdAsync(cancellationToken);

            data["events_api_accessible"] = true;
            data["checked_at_utc"] = DateTime.UtcNow;

            if (!string.IsNullOrEmpty(latestEventId))
            {
                data["latest_event_id"] = latestEventId;
                data["events_available"] = true;

                return HealthCheckResult.Healthy(
                    "Events API is accessible and has events available for incremental sync",
                    data);
            }
            else
            {
                data["events_available"] = false;

                return HealthCheckResult.Degraded(
                    "Events API is accessible but no events are available yet. " +
                    "This is normal for new districts or if Events API was recently enabled. " +
                    "Incremental sync will fall back to timestamp-based change detection.",
                    data: data);
            }
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            data["events_api_accessible"] = false;
            data["error"] = "403 Forbidden - Events scope not granted";
            data["recommendation"] = "Request the 'read:events' scope in your Clever app settings and ensure the district has approved it.";

            return HealthCheckResult.Degraded(
                "Events API is not accessible (403 Forbidden). The Events scope may not be granted. " +
                "Incremental sync will use data API with client-side change detection.",
                data: data);
        }
        catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            data["events_api_accessible"] = false;
            data["error"] = "404 Not Found - Events API not available for this app type";
            data["recommendation"] = "Events API may not be available for your Clever app type.";

            return HealthCheckResult.Degraded(
                "Events API is not available (404 Not Found). " +
                "Incremental sync will use data API with client-side change detection.",
                data: data);
        }
        catch (Exception ex)
        {
            data["events_api_accessible"] = false;
            data["error"] = ex.Message;

            return HealthCheckResult.Unhealthy(
                $"Failed to check Events API: {ex.Message}",
                ex,
                data);
        }
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
