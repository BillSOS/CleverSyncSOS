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
using CleverSyncSOS.Core.CleverApi;
using CleverSyncSOS.Core.Configuration;
using CleverSyncSOS.Core.Database.SchoolDb;
using CleverSyncSOS.Core.Database.SessionDb;
using CleverSyncSOS.Core.Services;
using CleverSyncSOS.Core.Sync;
using CleverSyncSOS.Core.Sync.Workshop;
using Microsoft.EntityFrameworkCore;
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

        // FR-004: Configure HTTP client
        // Plan: IHttpClientFactory with typed clients
        // Note: Retry logic is handled in CleverAuthenticationService, not via Polly
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
            });

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

    /// <summary>
    /// Adds Clever API client services to the dependency injection container.
    /// Source: Stage 2 - FR-012 through FR-022 (Database Sync)
    /// Handles data retrieval from Clever API with pagination, rate limiting, and retries.
    /// </summary>
    public static IServiceCollection AddCleverApiClient(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // FR-020: Configuration for Clever API settings
        services.Configure<CleverApiConfiguration>(
            configuration.GetSection("CleverApi"));

        // FR-012: Configure HTTP client for Clever API
        // FR-018: Retry logic with exponential backoff
        services.AddHttpClient<ICleverApiClient, CleverApiClient>()
            .ConfigureHttpClient((sp, client) =>
            {
                var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<CleverApiConfiguration>>().Value;
                client.BaseAddress = new Uri(config.BaseUrl);
                client.Timeout = TimeSpan.FromSeconds(config.TimeoutSeconds);
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
            });

        return services;
    }

    /// <summary>
    /// Adds database synchronization services to the dependency injection container.
    /// Source: Stage 2 - FR-012 through FR-025 (Database Sync)
    /// Registers SessionDb, SchoolDb factory, and SyncService for orchestration.
    /// </summary>
    public static IServiceCollection AddCleverSync(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register SessionDb (orchestration database)
        // Connection string: CleverSyncSOS:SessionDb:ConnectionString
        var sessionDbConnectionString = configuration.GetConnectionString("SessionDb");
        if (string.IsNullOrEmpty(sessionDbConnectionString))
        {
            throw new InvalidOperationException(
                "SessionDb connection string is not configured. " +
                "Please set ConnectionStrings:SessionDb in appsettings.json or environment variables.");
        }

        // Use DbContextFactory for better concurrent access (used by SyncLockService and other services)
        services.AddPooledDbContextFactory<SessionDbContext>(options =>
        {
            options.UseSqlServer(sessionDbConnectionString);
        });

        // Also register scoped SessionDbContext for backwards compatibility
        services.AddScoped(sp => sp.GetRequiredService<IDbContextFactory<SessionDbContext>>().CreateDbContext());

        // Register SchoolDatabaseConnectionFactory for per-school databases
        // School connection strings are retrieved from Azure Key Vault via ICredentialStore
        services.AddSingleton<SchoolDatabaseConnectionFactory>();

        // Register LocalTimeService for timezone-aware timestamp handling
        // Converts UTC to local time based on district's LocalTimeZone setting
        services.AddScoped<ILocalTimeService, LocalTimeService>();

        // Register SyncScheduleService for managing scheduled sync times
        // Allows Super Admins to configure when syncs run via the Admin Portal
        services.AddScoped<ISyncScheduleService, SyncScheduleService>();

        // Register SyncLockService for distributed locking across Admin Portal and Azure Functions
        // Uses database row-level locking to prevent concurrent sync operations
        services.AddScoped<ISyncLockService, SyncLockService>();

        // Register WorkshopSyncService for executing workshop sync stored procedure
        // Separated from main SyncService to improve code organization and testability
        services.AddScoped<IWorkshopSyncService, WorkshopSyncService>();

        // Register SyncService for orchestration
        services.AddScoped<ISyncService, SyncService>();

        return services;
    }
}
