// ---
// speckit:
//   type: test
//   source: SpecKit/Plans/003-key-vault-naming-standardization/plan.md
//   section: Phase 2 - Service Layer Updates
//   version: 2.0.0
// ---

using Azure;
using Azure.Security.KeyVault.Secrets;
using CleverSyncSOS.Core.Authentication;
using CleverSyncSOS.Core.Configuration;
using Moq;
using Xunit;

namespace CleverSyncSOS.Core.Tests.Authentication;

/// <summary>
/// Unit tests for KeyVaultCredentialStore.
/// Tests the v2.0 naming convention implementation.
/// </summary>
public class KeyVaultCredentialStoreTests
{
    private readonly Mock<SecretClient> _mockSecretClient;

    public KeyVaultCredentialStoreTests()
    {
        _mockSecretClient = new Mock<SecretClient>();
    }

    #region GetGlobalSecretAsync Tests

    [Fact]
    public async Task GetGlobalSecretAsync_WithValidFunctionalName_RetrievesSecret()
    {
        // Arrange
        var functionalName = "ClientId";
        var expectedValue = "test-client-id";

        var mockSecret = CreateMockSecret(functionalName, expectedValue);
        var mockResponse = Response.FromValue(mockSecret, Mock.Of<Response>());

        _mockSecretClient
            .Setup(x => x.GetSecretAsync(functionalName, null, default))
            .ReturnsAsync(mockResponse);

        var store = new KeyVaultCredentialStore(_mockSecretClient.Object);

        // Act
        var result = await store.GetGlobalSecretAsync(functionalName);

        // Assert
        Assert.Equal(expectedValue, result);
        _mockSecretClient.Verify(x => x.GetSecretAsync(functionalName, null, default), Times.Once);
    }

    [Fact]
    public async Task GetGlobalSecretAsync_WithKeyVaultSecretNamingConstant_RetrievesSecret()
    {
        // Arrange
        var expectedValue = "test-client-secret";

        var mockSecret = CreateMockSecret(KeyVaultSecretNaming.Global.ClientSecret, expectedValue);
        var mockResponse = Response.FromValue(mockSecret, Mock.Of<Response>());

        _mockSecretClient
            .Setup(x => x.GetSecretAsync(KeyVaultSecretNaming.Global.ClientSecret, null, default))
            .ReturnsAsync(mockResponse);

        var store = new KeyVaultCredentialStore(_mockSecretClient.Object);

        // Act
        var result = await store.GetGlobalSecretAsync(KeyVaultSecretNaming.Global.ClientSecret);

        // Assert
        Assert.Equal(expectedValue, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetGlobalSecretAsync_WithInvalidFunctionalName_ThrowsArgumentException(string invalidName)
    {
        // Arrange
        var store = new KeyVaultCredentialStore(_mockSecretClient.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.GetGlobalSecretAsync(invalidName));
    }

    [Fact]
    public async Task GetGlobalSecretAsync_WhenSecretValueIsNull_ReturnsEmptyString()
    {
        // Arrange
        var functionalName = "ClientId";
        var mockSecret = CreateMockSecret(functionalName, null);
        var mockResponse = Response.FromValue(mockSecret, Mock.Of<Response>());

        _mockSecretClient
            .Setup(x => x.GetSecretAsync(functionalName, null, default))
            .ReturnsAsync(mockResponse);

        var store = new KeyVaultCredentialStore(_mockSecretClient.Object);

        // Act
        var result = await store.GetGlobalSecretAsync(functionalName);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    #endregion

    #region GetDistrictSecretAsync Tests

    [Fact]
    public async Task GetDistrictSecretAsync_WithValidInputs_BuildsCorrectSecretName()
    {
        // Arrange
        var districtPrefix = "NorthCentral";
        var functionalName = "ApiToken";
        var expectedSecretName = "NorthCentral--ApiToken";
        var expectedValue = "test-api-token";

        var mockSecret = CreateMockSecret(expectedSecretName, expectedValue);
        var mockResponse = Response.FromValue(mockSecret, Mock.Of<Response>());

        _mockSecretClient
            .Setup(x => x.GetSecretAsync(expectedSecretName, null, default))
            .ReturnsAsync(mockResponse);

        var store = new KeyVaultCredentialStore(_mockSecretClient.Object);

        // Act
        var result = await store.GetDistrictSecretAsync(districtPrefix, functionalName);

        // Assert
        Assert.Equal(expectedValue, result);
        _mockSecretClient.Verify(x => x.GetSecretAsync(expectedSecretName, null, default), Times.Once);
    }

    [Fact]
    public async Task GetDistrictSecretAsync_WithKeyVaultSecretNamingConstant_RetrievesSecret()
    {
        // Arrange
        var districtPrefix = "SouthDistrict";
        var expectedSecretName = "SouthDistrict--ContactEmail";
        var expectedValue = "admin@south.edu";

        var mockSecret = CreateMockSecret(expectedSecretName, expectedValue);
        var mockResponse = Response.FromValue(mockSecret, Mock.Of<Response>());

        _mockSecretClient
            .Setup(x => x.GetSecretAsync(expectedSecretName, null, default))
            .ReturnsAsync(mockResponse);

        var store = new KeyVaultCredentialStore(_mockSecretClient.Object);

        // Act
        var result = await store.GetDistrictSecretAsync(districtPrefix, KeyVaultSecretNaming.District.ContactEmail);

        // Assert
        Assert.Equal(expectedValue, result);
    }

    [Theory]
    [InlineData(null, "ApiToken")]
    [InlineData("", "ApiToken")]
    [InlineData("   ", "ApiToken")]
    public async Task GetDistrictSecretAsync_WithInvalidPrefix_ThrowsArgumentException(
        string invalidPrefix, string functionalName)
    {
        // Arrange
        var store = new KeyVaultCredentialStore(_mockSecretClient.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.GetDistrictSecretAsync(invalidPrefix, functionalName));
    }

    [Theory]
    [InlineData("NorthCentral", null)]
    [InlineData("NorthCentral", "")]
    [InlineData("NorthCentral", "   ")]
    public async Task GetDistrictSecretAsync_WithInvalidFunctionalName_ThrowsArgumentException(
        string prefix, string invalidFunctionalName)
    {
        // Arrange
        var store = new KeyVaultCredentialStore(_mockSecretClient.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.GetDistrictSecretAsync(prefix, invalidFunctionalName));
    }

    #endregion

    #region GetSchoolSecretAsync Tests

    [Fact]
    public async Task GetSchoolSecretAsync_WithValidInputs_BuildsCorrectSecretName()
    {
        // Arrange
        var schoolPrefix = "CityHighSchool";
        var functionalName = "ConnectionString";
        var expectedSecretName = "CityHighSchool--ConnectionString";
        var expectedValue = "Server=test.database.windows.net;Database=School_CityHigh;";

        var mockSecret = CreateMockSecret(expectedSecretName, expectedValue);
        var mockResponse = Response.FromValue(mockSecret, Mock.Of<Response>());

        _mockSecretClient
            .Setup(x => x.GetSecretAsync(expectedSecretName, null, default))
            .ReturnsAsync(mockResponse);

        var store = new KeyVaultCredentialStore(_mockSecretClient.Object);

        // Act
        var result = await store.GetSchoolSecretAsync(schoolPrefix, functionalName);

        // Assert
        Assert.Equal(expectedValue, result);
        _mockSecretClient.Verify(x => x.GetSecretAsync(expectedSecretName, null, default), Times.Once);
    }

    [Fact]
    public async Task GetSchoolSecretAsync_WithKeyVaultSecretNamingConstant_RetrievesSecret()
    {
        // Arrange
        var schoolPrefix = "LincolnElementary";
        var expectedSecretName = "LincolnElementary--ApiKey";
        var expectedValue = "test-api-key-123";

        var mockSecret = CreateMockSecret(expectedSecretName, expectedValue);
        var mockResponse = Response.FromValue(mockSecret, Mock.Of<Response>());

        _mockSecretClient
            .Setup(x => x.GetSecretAsync(expectedSecretName, null, default))
            .ReturnsAsync(mockResponse);

        var store = new KeyVaultCredentialStore(_mockSecretClient.Object);

        // Act
        var result = await store.GetSchoolSecretAsync(schoolPrefix, KeyVaultSecretNaming.School.ApiKey);

        // Assert
        Assert.Equal(expectedValue, result);
    }

    [Theory]
    [InlineData(null, "ConnectionString")]
    [InlineData("", "ConnectionString")]
    [InlineData("   ", "ConnectionString")]
    public async Task GetSchoolSecretAsync_WithInvalidPrefix_ThrowsArgumentException(
        string invalidPrefix, string functionalName)
    {
        // Arrange
        var store = new KeyVaultCredentialStore(_mockSecretClient.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.GetSchoolSecretAsync(invalidPrefix, functionalName));
    }

    [Theory]
    [InlineData("CityHighSchool", null)]
    [InlineData("CityHighSchool", "")]
    [InlineData("CityHighSchool", "   ")]
    public async Task GetSchoolSecretAsync_WithInvalidFunctionalName_ThrowsArgumentException(
        string prefix, string invalidFunctionalName)
    {
        // Arrange
        var store = new KeyVaultCredentialStore(_mockSecretClient.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.GetSchoolSecretAsync(prefix, invalidFunctionalName));
    }

    #endregion

    #region GetSecretAsync Tests

    [Fact]
    public async Task GetSecretAsync_WithValidSecretName_RetrievesSecret()
    {
        // Arrange
        var secretName = "CustomSecret";
        var expectedValue = "custom-value";

        var mockSecret = CreateMockSecret(secretName, expectedValue);
        var mockResponse = Response.FromValue(mockSecret, Mock.Of<Response>());

        _mockSecretClient
            .Setup(x => x.GetSecretAsync(secretName, null, default))
            .ReturnsAsync(mockResponse);

        var store = new KeyVaultCredentialStore(_mockSecretClient.Object);

        // Act
        var result = await store.GetSecretAsync(secretName);

        // Assert
        Assert.Equal(expectedValue, result);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetSecretAsync_WithInvalidSecretName_ThrowsArgumentException(string invalidName)
    {
        // Arrange
        var store = new KeyVaultCredentialStore(_mockSecretClient.Object);

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentException>(() =>
            store.GetSecretAsync(invalidName));
    }

    #endregion

    #region Deprecated Methods Tests

    [Fact]
    public async Task GetClientIdAsync_ObsoleteMethod_CallsGetGlobalSecretAsync()
    {
        // Test backward compatibility
        // Arrange
        var expectedValue = "test-client-id";

        var mockSecret = CreateMockSecret(KeyVaultSecretNaming.Global.ClientId, expectedValue);
        var mockResponse = Response.FromValue(mockSecret, Mock.Of<Response>());

        _mockSecretClient
            .Setup(x => x.GetSecretAsync(KeyVaultSecretNaming.Global.ClientId, null, default))
            .ReturnsAsync(mockResponse);

        var store = new KeyVaultCredentialStore(_mockSecretClient.Object);

        // Act
#pragma warning disable CS0618 // Type or member is obsolete
        var result = await store.GetClientIdAsync();
#pragma warning restore CS0618

        // Assert
        Assert.Equal(expectedValue, result);
        _mockSecretClient.Verify(
            x => x.GetSecretAsync(KeyVaultSecretNaming.Global.ClientId, null, default),
            Times.Once);
    }

    [Fact]
    public async Task GetClientSecretAsync_ObsoleteMethod_CallsGetGlobalSecretAsync()
    {
        // Test backward compatibility
        // Arrange
        var expectedValue = "test-client-secret";

        var mockSecret = CreateMockSecret(KeyVaultSecretNaming.Global.ClientSecret, expectedValue);
        var mockResponse = Response.FromValue(mockSecret, Mock.Of<Response>());

        _mockSecretClient
            .Setup(x => x.GetSecretAsync(KeyVaultSecretNaming.Global.ClientSecret, null, default))
            .ReturnsAsync(mockResponse);

        var store = new KeyVaultCredentialStore(_mockSecretClient.Object);

        // Act
#pragma warning disable CS0618 // Type or member is obsolete
        var result = await store.GetClientSecretAsync();
#pragma warning restore CS0618

        // Assert
        Assert.Equal(expectedValue, result);
        _mockSecretClient.Verify(
            x => x.GetSecretAsync(KeyVaultSecretNaming.Global.ClientSecret, null, default),
            Times.Once);
    }

    #endregion

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullSecretClient_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new KeyVaultCredentialStore(null!));
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a mock KeyVaultSecret for testing.
    /// </summary>
    private static KeyVaultSecret CreateMockSecret(string name, string? value)
    {
        var secretProperties = SecretModelFactory.SecretProperties(
            name: name,
            vaultUri: new Uri($"https://test-vault.vault.azure.net/secrets/{name}"));

        return SecretModelFactory.KeyVaultSecret(secretProperties, value);
    }

    #endregion
}
