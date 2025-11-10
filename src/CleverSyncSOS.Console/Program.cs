// ---
// speckit:
//   type: implementation
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Core Implementation
//   version: 1.0.0
// ---

using CleverSyncSOS.Core.Authentication;
using CleverSyncSOS.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Console;

/// <summary>
/// Entry point for CleverSyncSOS console application.
/// Source: SpecKit/Plans/001-clever-api-auth/plan.md (Stage 1)
/// Demonstrates: OAuth authentication with Clever API
/// </summary>
class Program
{
    static async Task<int> Main(string[] args)
    {
        try
        {
            // Constitution: Use dependency injection for all services
            // FR-007: Configuration via Azure App Configuration or secure settings
            var host = CreateHostBuilder(args).Build();

            // Demonstrate authentication
            await DemonstrateAuthenticationAsync(host.Services);

            return 0;
        }
        catch (Exception ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"Fatal error: {ex.Message}");
            System.Console.ResetColor();
            return 1;
        }
    }

    /// <summary>
    /// Creates and configures the host builder.
    /// Source: Constitution - Dependency injection, Configuration management
    /// </summary>
    static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((context, config) =>
            {
                // FR-007: Configuration from multiple sources
                config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);
                config.AddJsonFile($"appsettings.{context.HostingEnvironment.EnvironmentName}.json",
                    optional: true, reloadOnChange: true);
                config.AddEnvironmentVariables("CLEVERSYNC_");
                config.AddCommandLine(args);

                // Constitution: Store configuration in Azure App Configuration (optional)
                // Uncomment to enable Azure App Configuration:
                // var settings = config.Build();
                // var azureAppConfigConnection = settings["AzureAppConfiguration:ConnectionString"];
                // if (!string.IsNullOrEmpty(azureAppConfigConnection))
                // {
                //     config.AddAzureAppConfiguration(azureAppConfigConnection);
                // }
            })
            .ConfigureServices((context, services) =>
            {
                // Add CleverSyncSOS authentication services
                services.AddCleverAuthentication(context.Configuration);

                // FR-010: Add Application Insights (Stage 3)
                // Uncomment when Application Insights is configured:
                // services.AddCleverObservability(context.Configuration);
            })
            .ConfigureLogging((context, logging) =>
            {
                // FR-010: Structured logging
                logging.ClearProviders();
                logging.AddConsole();
                logging.AddDebug();

                // Set log level from configuration
                var logLevel = context.Configuration.GetValue<LogLevel>("Logging:LogLevel:Default", LogLevel.Information);
                logging.SetMinimumLevel(logLevel);
            });

    /// <summary>
    /// Demonstrates Clever API authentication.
    /// Source: FR-001, FR-003 - OAuth authentication and token management
    /// </summary>
    static async Task DemonstrateAuthenticationAsync(IServiceProvider services)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        var authService = services.GetRequiredService<ICleverAuthenticationService>();

        System.Console.WriteLine();
        System.Console.WriteLine("==============================================");
        System.Console.WriteLine("CleverSyncSOS - Clever API Authentication Demo");
        System.Console.WriteLine("==============================================");
        System.Console.WriteLine();

        try
        {
            // NFR-001: Authentication must complete within 5 seconds
            System.Console.WriteLine("Authenticating with Clever API...");
            var token = await authService.AuthenticateAsync();

            System.Console.ForegroundColor = ConsoleColor.Green;
            System.Console.WriteLine("✓ Authentication successful!");
            System.Console.ResetColor();
            System.Console.WriteLine();

            // Display token information (sanitized - not showing actual token)
            System.Console.WriteLine("Token Information:");
            System.Console.WriteLine($"  Token Type: {token.TokenType}");
            System.Console.WriteLine($"  Expires In: {token.ExpiresIn} seconds");
            System.Console.WriteLine($"  Issued At: {token.IssuedAt:yyyy-MM-dd HH:mm:ss} UTC");
            System.Console.WriteLine($"  Expires At: {token.ExpiresAt:yyyy-MM-dd HH:mm:ss} UTC");
            System.Console.WriteLine($"  Time Until Expiration: {token.TimeUntilExpiration:hh\\:mm\\:ss}");
            System.Console.WriteLine($"  Should Refresh (75% threshold): {token.ShouldRefresh(75.0)}");
            System.Console.WriteLine();

            // Demonstrate token retrieval (should use cached token)
            System.Console.WriteLine("Retrieving token from cache...");
            var cachedToken = await authService.GetTokenAsync();

            if (cachedToken.AccessToken == token.AccessToken)
            {
                System.Console.ForegroundColor = ConsoleColor.Green;
                System.Console.WriteLine("✓ Retrieved token from cache (no re-authentication needed)");
                System.Console.ResetColor();
            }

            System.Console.WriteLine();

            // Display health status
            var lastAuthTime = authService.GetLastSuccessfulAuthTime();
            var lastError = authService.GetLastError();

            System.Console.WriteLine("Health Status:");
            System.Console.WriteLine($"  Last Successful Auth: {lastAuthTime:yyyy-MM-dd HH:mm:ss} UTC");
            System.Console.WriteLine($"  Last Error: {lastError ?? "None"}");

            System.Console.WriteLine();
            System.Console.ForegroundColor = ConsoleColor.Cyan;
            System.Console.WriteLine("Stage 1 (Core Implementation) demonstration complete!");
            System.Console.WriteLine("Next steps: Stage 2 (Database Sync) and Stage 3 (Health Endpoints)");
            System.Console.ResetColor();
        }
        catch (Exception ex)
        {
            System.Console.ForegroundColor = ConsoleColor.Red;
            System.Console.WriteLine($"✗ Authentication failed: {ex.Message}");
            System.Console.ResetColor();

            logger.LogError(ex, "Authentication demonstration failed");
            throw;
        }
    }
}
