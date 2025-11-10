// ---
// speckit:
//   type: implementation
//   source: SpecKit/Plans/001-clever-api-auth/plan.md
//   section: Development Standards
//   constitution: SpecKit/Constitution/constitution.md
//   phase: Core Implementation
//   version: 1.0.0
// ---

using CleverSyncSOS.Core.Authentication;
using CleverSyncSOS.Core.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly;
using Polly.Extensions.Http;

namespace CleverSyncSOS.Infrastructure.Extensions;

/// <summary>
/// Extension methods for configuring CleverSyncSOS services in dependency injection container.
/// Source: SpecKit/Plans/001-clever-api-auth/plan.md (Development Standards)
/// Constitution: Use dependency injection for all services and configuration.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds CleverSyncSOS authentication services to the dependency injection container.
    /// Source: Constitution - Dependency injection for all services
    /// Spec: FR-004 - Exponential backoff, FR-011 - TLS 1.2+
    /// </summary>
    public static IServiceCollection AddCleverAuthentication(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Constitution: Store configuration in Azure App Configuration or secure settings
        // FR-007: Externally configurable retry intervals, timeouts, and endpoints
        services.Configure<CleverAuthConfiguration>(
            configuration.GetSection("CleverAuth"));

        // FR-002: Credential storage with Azure Key Vault
        services.AddSingleton<ICredentialStore, AzureKeyVaultCredentialStore>();

        // FR-001, FR-003: OAuth authentication service with token management
        services.AddSingleton<ICleverAuthenticationService, CleverAuthenticationService>();

        // FR-004: Configure HTTP client with Polly retry policy
        // Plan: IHttpClientFactory with typed clients
        services.AddHttpClient("CleverAuth")
            .ConfigureHttpClient((sp, client) =>
            {
                var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CleverAuthConfiguration>>().Value;

                // FR-007: Configurable timeouts
                client.Timeout = TimeSpan.FromSeconds(config.RequestTimeoutSeconds);

                // FR-011: TLS 1.2+ is enforced by default in .NET 9
            })
            .ConfigurePrimaryHttpMessageHandler(() =>
            {
                // FR-011: Enforce TLS 1.2+
                var handler = new HttpClientHandler
                {
                    SslProtocols = System.Security.Authentication.SslProtocols.Tls12 |
                                   System.Security.Authentication.SslProtocols.Tls13
                };
                return handler;
            })
            .AddPolicyHandler(GetRetryPolicy());

        return services;
    }

    /// <summary>
    /// Creates a retry policy for HTTP requests with exponential backoff.
    /// Source: FR-004 - Exponential backoff retry logic (2s, 4s, 8s, 16s, 32s)
    /// </summary>
    private static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy()
    {
        // FR-004: Retry up to 5 times with exponential backoff
        // FR-008: Handle rate limiting (HTTP 429)
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(response => response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            .WaitAndRetryAsync(
                retryCount: 5,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)),
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    // FR-010: Structured logging for retry events
                    // Logging is handled by the service layer, this is for policy-level tracking
                    Console.WriteLine($"Retry {retryCount} after {timespan.TotalSeconds}s delay");
                });
    }

    /// <summary>
    /// Adds Application Insights telemetry for observability.
    /// Source: FR-010 - Integrate with Azure Application Insights
    /// Constitution: Observability - Centralized logging with Application Insights
    /// Note: Full implementation in Stage 3 (Health & Observability)
    /// </summary>
    public static IServiceCollection AddCleverObservability(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // FR-010: Azure Application Insights integration
        // This would be fully implemented in Stage 3
        services.AddApplicationInsightsTelemetry(options =>
        {
            options.ConnectionString = configuration["ApplicationInsights:ConnectionString"];
        });

        return services;
    }
}
