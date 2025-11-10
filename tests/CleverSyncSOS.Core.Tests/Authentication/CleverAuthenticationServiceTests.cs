// ---
// speckit:
//   type: test
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-001, FR-003, FR-004
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Core Implementation
//   version: 1.0.0
// ---

using CleverSyncSOS.Core.Authentication;
using CleverSyncSOS.Core.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Moq.Protected;
using System.Net;
using Xunit;

namespace CleverSyncSOS.Core.Tests.Authentication;

/// <summary>
/// Unit tests for CleverAuthenticationService.
/// Source: SpecKit/Specs/001-clever-api-auth/spec-1.md (FR-001, FR-003, FR-004)
/// Tests: OAuth authentication, token caching, retry logic
/// </summary>
public class CleverAuthenticationServiceTests
{
    private readonly Mock<ICredentialStore> _mockCredentialStore;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly Mock<ILogger<CleverAuthenticationService>> _mockLogger;
    private readonly CleverAuthConfiguration _configuration;

    public CleverAuthenticationServiceTests()
    {
        _mockCredentialStore = new Mock<ICredentialStore>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockLogger = new Mock<ILogger<CleverAuthenticationService>>();

        _configuration = new CleverAuthConfiguration
        {
            KeyVaultUri = "https://test-vault.vault.azure.net/",
            TokenEndpoint = "https://clever.com/oauth/tokens",
            MaxRetryAttempts = 3,
            InitialRetryDelaySeconds = 1,
            TokenRefreshThresholdPercent = 75.0
        };
    }

    /// <summary>
    /// Test: Successful authentication returns valid token.
    /// Source: FR-001 - OAuth Authentication
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_WithValidCredentials_ReturnsToken()
    {
        // Arrange
        _mockCredentialStore.Setup(x => x.GetClientIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-client-id");
        _mockCredentialStore.Setup(x => x.GetClientSecretAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-client-secret");

        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"access_token\":\"test-token\",\"token_type\":\"Bearer\",\"expires_in\":3600}")
            });

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        _mockHttpClientFactory.Setup(x => x.CreateClient("CleverAuth")).Returns(httpClient);

        var service = new CleverAuthenticationService(
            _mockCredentialStore.Object,
            _mockHttpClientFactory.Object,
            Options.Create(_configuration),
            _mockLogger.Object);

        // Act
        var token = await service.AuthenticateAsync();

        // Assert
        Assert.NotNull(token);
        Assert.Equal("test-token", token.AccessToken);
        Assert.Equal("Bearer", token.TokenType);
        Assert.Equal(3600, token.ExpiresIn);

        // FR-005: Verify last successful auth time is tracked
        Assert.NotNull(service.GetLastSuccessfulAuthTime());
        Assert.Null(service.GetLastError());
    }

    /// <summary>
    /// Test: Authentication failure sets error state.
    /// Source: FR-005 - Health check requires error status
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_OnFailure_TracksError()
    {
        // Arrange
        _mockCredentialStore.Setup(x => x.GetClientIdAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Key Vault unavailable"));

        var service = new CleverAuthenticationService(
            _mockCredentialStore.Object,
            _mockHttpClientFactory.Object,
            Options.Create(_configuration),
            _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<Exception>(async () => await service.AuthenticateAsync());

        // FR-005: Verify error is tracked
        Assert.NotNull(service.GetLastError());
        Assert.Contains("Key Vault unavailable", service.GetLastError());
    }

    /// <summary>
    /// Test: GetTokenAsync returns cached token when valid.
    /// Source: FR-003 - Cache tokens in memory
    /// </summary>
    [Fact]
    public async Task GetTokenAsync_WithValidCachedToken_ReturnsCachedToken()
    {
        // Arrange
        _mockCredentialStore.Setup(x => x.GetClientIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-client-id");
        _mockCredentialStore.Setup(x => x.GetClientSecretAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-client-secret");

        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("{\"access_token\":\"test-token\",\"token_type\":\"Bearer\",\"expires_in\":3600}")
            });

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        _mockHttpClientFactory.Setup(x => x.CreateClient("CleverAuth")).Returns(httpClient);

        var service = new CleverAuthenticationService(
            _mockCredentialStore.Object,
            _mockHttpClientFactory.Object,
            Options.Create(_configuration),
            _mockLogger.Object);

        // Act - First call authenticates
        var token1 = await service.GetTokenAsync();

        // Act - Second call should use cache
        var token2 = await service.GetTokenAsync();

        // Assert
        Assert.Equal(token1.AccessToken, token2.AccessToken);

        // Verify HTTP client was only called once (token was cached)
        mockHttpMessageHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    /// <summary>
    /// Test: GetCurrentToken returns cached token without refresh.
    /// Source: FR-003 - Token Management
    /// </summary>
    [Fact]
    public void GetCurrentToken_WithNoAuthentication_ReturnsNull()
    {
        // Arrange
        var service = new CleverAuthenticationService(
            _mockCredentialStore.Object,
            _mockHttpClientFactory.Object,
            Options.Create(_configuration),
            _mockLogger.Object);

        // Act
        var token = service.GetCurrentToken();

        // Assert
        Assert.Null(token);
    }

    /// <summary>
    /// Test: Rate limiting is properly detected.
    /// Source: FR-008 - Detect and handle HTTP 429 responses
    /// </summary>
    [Fact]
    public async Task AuthenticateAsync_OnRateLimit_RetriesWithBackoff()
    {
        // Arrange
        _mockCredentialStore.Setup(x => x.GetClientIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-client-id");
        _mockCredentialStore.Setup(x => x.GetClientSecretAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-client-secret");

        var callCount = 0;
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                // First two calls return 429, third succeeds
                if (callCount <= 2)
                {
                    return new HttpResponseMessage
                    {
                        StatusCode = HttpStatusCode.TooManyRequests
                    };
                }
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("{\"access_token\":\"test-token\",\"token_type\":\"Bearer\",\"expires_in\":3600}")
                };
            });

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);
        _mockHttpClientFactory.Setup(x => x.CreateClient("CleverAuth")).Returns(httpClient);

        var service = new CleverAuthenticationService(
            _mockCredentialStore.Object,
            _mockHttpClientFactory.Object,
            Options.Create(_configuration),
            _mockLogger.Object);

        // Act
        var token = await service.AuthenticateAsync();

        // Assert - FR-004: Retry logic succeeded after rate limiting
        Assert.NotNull(token);
        Assert.Equal(3, callCount); // Should have retried twice then succeeded
    }
}
