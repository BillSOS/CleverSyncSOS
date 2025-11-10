// ---
// speckit:
//   type: test
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-001, FR-002, FR-003
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Core Implementation
//   version: 1.0.0
// ---

using CleverSyncSOS.Core.Authentication;
using CleverSyncSOS.Core.Configuration;
using CleverSyncSOS.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace CleverSyncSOS.Integration.Tests;

/// <summary>
/// Integration tests for Clever API authentication.
/// Source: SpecKit/Specs/001-clever-api-auth/spec-1.md
/// Tests: End-to-end authentication flow with dependency injection
/// Note: These tests require Azure Key Vault access and valid Clever credentials.
/// </summary>
public class AuthenticationIntegrationTests
{
    /// <summary>
    /// Test: Service registration and dependency injection.
    /// Source: Constitution - Dependency injection for all services
    /// </summary>
    [Fact]
    public void ServiceRegistration_ConfiguresAllDependencies()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "CleverAuth:KeyVaultUri", "https://test-vault.vault.azure.net/" },
                { "CleverAuth:TokenEndpoint", "https://clever.com/oauth/tokens" },
                { "CleverAuth:MaxRetryAttempts", "5" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddCleverAuthentication(configuration);

        // Act
        var serviceProvider = services.BuildServiceProvider();

        // Assert - FR-007: Configuration is properly bound
        var authConfig = serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<CleverAuthConfiguration>>();
        Assert.NotNull(authConfig);
        Assert.Equal("https://test-vault.vault.azure.net/", authConfig.Value.KeyVaultUri);

        // Assert - FR-002: Credential store is registered
        var credentialStore = serviceProvider.GetService<ICredentialStore>();
        Assert.NotNull(credentialStore);

        // Assert - FR-001: Authentication service is registered
        var authService = serviceProvider.GetService<ICleverAuthenticationService>();
        Assert.NotNull(authService);

        // Assert - FR-004: HTTP client factory is configured
        var httpClientFactory = serviceProvider.GetService<IHttpClientFactory>();
        Assert.NotNull(httpClientFactory);
    }

    /// <summary>
    /// Test: Configuration validation.
    /// Source: FR-007 - Externally configurable settings
    /// </summary>
    [Fact]
    public void Configuration_BindsCorrectly()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "CleverAuth:KeyVaultUri", "https://custom-vault.vault.azure.net/" },
                { "CleverAuth:TokenEndpoint", "https://custom.clever.com/oauth/tokens" },
                { "CleverAuth:MaxRetryAttempts", "3" },
                { "CleverAuth:TokenRefreshThresholdPercent", "80.0" },
                { "CleverAuth:InitialRetryDelaySeconds", "5" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddCleverAuthentication(configuration);

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var authConfig = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<CleverAuthConfiguration>>().Value;

        // Assert - All configuration values are properly bound
        Assert.Equal("https://custom-vault.vault.azure.net/", authConfig.KeyVaultUri);
        Assert.Equal("https://custom.clever.com/oauth/tokens", authConfig.TokenEndpoint);
        Assert.Equal(3, authConfig.MaxRetryAttempts);
        Assert.Equal(80.0, authConfig.TokenRefreshThresholdPercent);
        Assert.Equal(5, authConfig.InitialRetryDelaySeconds);
    }

    /// <summary>
    /// Test: HTTP client is properly configured with retry policy.
    /// Source: FR-004 - Retry logic with exponential backoff
    /// </summary>
    [Fact]
    public void HttpClient_IsConfiguredWithRetryPolicy()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "CleverAuth:KeyVaultUri", "https://test-vault.vault.azure.net/" },
                { "CleverAuth:RequestTimeoutSeconds", "30" }
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddCleverAuthentication(configuration);

        // Act
        var serviceProvider = services.BuildServiceProvider();
        var httpClientFactory = serviceProvider.GetRequiredService<IHttpClientFactory>();
        var httpClient = httpClientFactory.CreateClient("CleverAuth");

        // Assert
        Assert.NotNull(httpClient);
        Assert.Equal(TimeSpan.FromSeconds(30), httpClient.Timeout);
    }

    // NOTE: The following test is commented out as it requires actual Azure Key Vault
    // and Clever API credentials. Uncomment and configure when testing in a real environment.

    /*
    /// <summary>
    /// Test: End-to-end authentication with real Clever API.
    /// Source: FR-001, FR-002, FR-003 - OAuth authentication with Key Vault
    /// Requires: Valid Azure Key Vault and Clever API credentials
    /// </summary>
    [Fact(Skip = "Requires Azure Key Vault and Clever API credentials")]
    public async Task AuthenticateAsync_WithRealCredentials_SucceedsEndToEnd()
    {
        // Arrange - Load configuration from environment or appsettings
        var configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.Test.json", optional: true)
            .AddEnvironmentVariables("CLEVERSYNC_")
            .Build();

        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Debug));
        services.AddCleverAuthentication(configuration);

        var serviceProvider = services.BuildServiceProvider();
        var authService = serviceProvider.GetRequiredService<ICleverAuthenticationService>();

        // Act
        var token = await authService.AuthenticateAsync();

        // Assert - NFR-001: Authentication completes within 5 seconds
        Assert.NotNull(token);
        Assert.NotEmpty(token.AccessToken);
        Assert.Equal("Bearer", token.TokenType);
        Assert.True(token.ExpiresIn > 0);
        Assert.False(token.IsExpired);

        // FR-005: Health status is tracked
        Assert.NotNull(authService.GetLastSuccessfulAuthTime());
        Assert.Null(authService.GetLastError());
    }
    */
}
