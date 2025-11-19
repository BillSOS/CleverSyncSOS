// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-011, NFR-001
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Health & Observability
//   version: 1.0.0
// ---

using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Functions;

/// <summary>
/// HTTP-triggered Azure Function for health checks.
/// Source: FR-011 - Health Check Endpoint
/// NFR-001: Response time must be < 100ms
/// Spec: SpecKit/Specs/001-clever-api-auth/spec-1.md
/// </summary>
public class HealthCheckFunction
{
    private readonly HealthCheckService _healthCheckService;
    private readonly ILogger<HealthCheckFunction> _logger;

    public HealthCheckFunction(
        HealthCheckService healthCheckService,
        ILogger<HealthCheckFunction> logger)
    {
        _healthCheckService = healthCheckService;
        _logger = logger;
    }

    /// <summary>
    /// HTTP-triggered function for health checks.
    /// Source: FR-011 - Health Check Endpoint
    ///
    /// Endpoint: GET /api/health/clever-auth
    /// Returns: Healthy (200), Degraded (200), Unhealthy (503)
    /// NFR-001: Must respond in < 100ms
    /// </summary>
    [Function("HealthCheck")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "health/clever-auth")] HttpRequestData req,
        FunctionContext context)
    {
        var startTime = DateTime.UtcNow;

        try
        {
            _logger.LogDebug("Health check endpoint called");

            // FR-011: Execute all registered health checks
            var healthReport = await _healthCheckService.CheckHealthAsync();

            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;

            // NFR-001: Warn if response time exceeds 100ms
            if (duration > 100)
            {
                _logger.LogWarning("Health check response time exceeded 100ms: {Duration}ms", duration);
            }

            // FR-011: Map health status to HTTP status codes
            // Healthy: 200 OK
            // Degraded: 200 OK (with warning in response)
            // Unhealthy: 503 Service Unavailable
            var statusCode = healthReport.Status switch
            {
                HealthStatus.Healthy => HttpStatusCode.OK,
                HealthStatus.Degraded => HttpStatusCode.OK,
                HealthStatus.Unhealthy => HttpStatusCode.ServiceUnavailable,
                _ => HttpStatusCode.InternalServerError
            };

            var response = req.CreateResponse(statusCode);

            // FR-011: Return structured health check response
            await response.WriteAsJsonAsync(new
            {
                status = healthReport.Status.ToString(),
                totalDuration = duration,
                checks = healthReport.Entries.Select(entry => new
                {
                    name = entry.Key,
                    status = entry.Value.Status.ToString(),
                    description = entry.Value.Description,
                    data = entry.Value.Data,
                    exception = entry.Value.Exception?.Message
                }),
                timestamp = DateTime.UtcNow
            });

            _logger.LogInformation(
                "Health check completed in {Duration}ms with overall status: {Status}",
                duration, healthReport.Status);

            return response;
        }
        catch (Exception ex)
        {
            var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
            _logger.LogError(ex, "Health check endpoint failed after {Duration}ms", duration);

            var errorResponse = req.CreateResponse(HttpStatusCode.InternalServerError);
            await errorResponse.WriteAsJsonAsync(new
            {
                status = "Unhealthy",
                error = "Health check failed with exception",
                message = ex.Message,
                timestamp = DateTime.UtcNow
            });
            return errorResponse;
        }
    }
}
