// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-005
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Health & Observability
//   version: 1.0.0
// ---

using CleverSyncSOS.Core.Health;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace CleverSyncSOS.Infrastructure.Extensions;

/// <summary>
/// Extension methods for registering health checks.
/// Source: SpecKit/Specs/001-clever-api-auth/spec-1.md (FR-005)
/// </summary>
public static class HealthCheckExtensions
{
    /// <summary>
    /// Adds CleverSyncSOS health checks to the service collection.
    /// Source: FR-005 - Health Check Endpoint
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <returns>IHealthChecksBuilder for fluent configuration</returns>
    public static IHealthChecksBuilder AddCleverHealthChecks(this IServiceCollection services)
    {
        return services.AddHealthChecks()
            .AddCheck<CleverAuthenticationHealthCheck>(
                name: "clever_authentication",
                failureStatus: HealthStatus.Unhealthy,
                tags: new[] { "clever", "authentication", "ready" })
            .AddCheck<CleverEventsHealthCheck>(
                name: "clever_events_api",
                failureStatus: HealthStatus.Degraded, // Degraded, not Unhealthy - sync still works without Events API
                tags: new[] { "clever", "events", "sync" });
    }

    /// <summary>
    /// Adds database health checks for SessionDb and SchoolDb.
    /// </summary>
    /// <param name="builder">The health checks builder</param>
    /// <returns>IHealthChecksBuilder for fluent configuration</returns>
    public static IHealthChecksBuilder AddDatabaseHealthChecks(this IHealthChecksBuilder builder)
    {
        // TODO: Add database health checks when we create a dedicated health check class
        // This would check SessionDb and SchoolDb connectivity
        return builder;
    }
}
