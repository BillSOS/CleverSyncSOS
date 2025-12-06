// ---
// speckit:
//   type: test
//   source: SpecKit/Plans/003-key-vault-naming-standardization/plan.md
//   section: Phase 2 - Service Layer Updates
//   version: 2.0.0
// ---

using CleverSyncSOS.Core.Authentication;
using CleverSyncSOS.Core.Configuration;
using CleverSyncSOS.Core.Database.SchoolDb;
using CleverSyncSOS.Core.Database.SessionDb.Entities;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CleverSyncSOS.Core.Tests.Database;

/// <summary>
/// Unit tests for SchoolDatabaseConnectionFactory.
/// Tests the v2.0 naming convention integration with Key Vault.
/// </summary>
public class SchoolDatabaseConnectionFactoryTests
{
    private readonly Mock<ICredentialStore> _mockCredentialStore;
    private readonly Mock<ILogger<SchoolDatabaseConnectionFactory>> _mockLogger;

    public SchoolDatabaseConnectionFactoryTests()
    {
        _mockCredentialStore = new Mock<ICredentialStore>();
        _mockLogger = new Mock<ILogger<SchoolDatabaseConnectionFactory>>();
    }

    #region CreateSchoolContextAsync Tests

    [Fact]
    public async Task CreateSchoolContextAsync_WithValidSchool_RetrievesConnectionStringUsingKeyVaultSchoolPrefix()
    {
        // Arrange
        var school = new School
        {
            SchoolId = 1,
            Name = "City High School",
            KeyVaultSchoolPrefix = "CityHighSchool",
            DatabaseName = "School_CityHigh"
        };

        var expectedConnectionString = "Server=test.database.windows.net,1433;Initial Catalog=School_CityHigh;User ID=admin;Password=testpass;";

        _mockCredentialStore
            .Setup(x => x.GetSchoolSecretAsync(
                "CityHighSchool",
                KeyVaultSecretNaming.School.ConnectionString,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedConnectionString);

        var factory = new SchoolDatabaseConnectionFactory(_mockCredentialStore.Object, _mockLogger.Object);

        // Act
        var context = await factory.CreateSchoolContextAsync(school);

        // Assert
        Assert.NotNull(context);
        _mockCredentialStore.Verify(
            x => x.GetSchoolSecretAsync(
                "CityHighSchool",
                KeyVaultSecretNaming.School.ConnectionString,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateSchoolContextAsync_WithDifferentSchoolPrefix_UsesCorrectPrefix()
    {
        // Arrange
        var school = new School
        {
            SchoolId = 2,
            Name = "Lincoln Elementary",
            KeyVaultSchoolPrefix = "LincolnElementary",
            DatabaseName = "School_Lincoln"
        };

        var expectedConnectionString = "Server=test.database.windows.net,1433;Initial Catalog=School_Lincoln;User ID=admin;Password=testpass;";

        _mockCredentialStore
            .Setup(x => x.GetSchoolSecretAsync(
                "LincolnElementary",
                KeyVaultSecretNaming.School.ConnectionString,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedConnectionString);

        var factory = new SchoolDatabaseConnectionFactory(_mockCredentialStore.Object, _mockLogger.Object);

        // Act
        var context = await factory.CreateSchoolContextAsync(school);

        // Assert
        Assert.NotNull(context);
        _mockCredentialStore.Verify(
            x => x.GetSchoolSecretAsync(
                "LincolnElementary",
                KeyVaultSecretNaming.School.ConnectionString,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateSchoolContextAsync_WithNullKeyVaultSchoolPrefix_ThrowsInvalidOperationException()
    {
        // Arrange
        var school = new School
        {
            SchoolId = 1,
            Name = "City High School",
            KeyVaultSchoolPrefix = null!,
            DatabaseName = "School_CityHigh"
        };

        var factory = new SchoolDatabaseConnectionFactory(_mockCredentialStore.Object, _mockLogger.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await factory.CreateSchoolContextAsync(school));

        Assert.Contains("does not have a KeyVaultSchoolPrefix configured", exception.Message);
        Assert.Contains(school.Name, exception.Message);
    }

    [Fact]
    public async Task CreateSchoolContextAsync_WithEmptyKeyVaultSchoolPrefix_ThrowsInvalidOperationException()
    {
        // Arrange
        var school = new School
        {
            SchoolId = 1,
            Name = "City High School",
            KeyVaultSchoolPrefix = "",
            DatabaseName = "School_CityHigh"
        };

        var factory = new SchoolDatabaseConnectionFactory(_mockCredentialStore.Object, _mockLogger.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await factory.CreateSchoolContextAsync(school));

        Assert.Contains("does not have a KeyVaultSchoolPrefix configured", exception.Message);
    }

    [Fact]
    public async Task CreateSchoolContextAsync_WhenConnectionStringIsEmpty_ThrowsInvalidOperationException()
    {
        // Arrange
        var school = new School
        {
            SchoolId = 1,
            Name = "City High School",
            KeyVaultSchoolPrefix = "CityHighSchool",
            DatabaseName = "School_CityHigh"
        };

        _mockCredentialStore
            .Setup(x => x.GetSchoolSecretAsync(
                "CityHighSchool",
                KeyVaultSecretNaming.School.ConnectionString,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(string.Empty);

        var factory = new SchoolDatabaseConnectionFactory(_mockCredentialStore.Object, _mockLogger.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await factory.CreateSchoolContextAsync(school));

        Assert.Contains("Failed to retrieve connection string", exception.Message);
        Assert.Contains(school.Name, exception.Message);
    }

    [Fact]
    public async Task CreateSchoolContextAsync_WhenConnectionStringIsNull_ThrowsInvalidOperationException()
    {
        // Arrange
        var school = new School
        {
            SchoolId = 1,
            Name = "City High School",
            KeyVaultSchoolPrefix = "CityHighSchool",
            DatabaseName = "School_CityHigh"
        };

        _mockCredentialStore
            .Setup(x => x.GetSchoolSecretAsync(
                "CityHighSchool",
                KeyVaultSecretNaming.School.ConnectionString,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string)null!);

        var factory = new SchoolDatabaseConnectionFactory(_mockCredentialStore.Object, _mockLogger.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            async () => await factory.CreateSchoolContextAsync(school));

        Assert.Contains("Failed to retrieve connection string", exception.Message);
    }

    [Fact]
    public async Task CreateSchoolContextAsync_WhenKeyVaultThrowsException_PropagatesException()
    {
        // Arrange
        var school = new School
        {
            SchoolId = 1,
            Name = "City High School",
            KeyVaultSchoolPrefix = "CityHighSchool",
            DatabaseName = "School_CityHigh"
        };

        _mockCredentialStore
            .Setup(x => x.GetSchoolSecretAsync(
                "CityHighSchool",
                KeyVaultSecretNaming.School.ConnectionString,
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Key Vault unavailable"));

        var factory = new SchoolDatabaseConnectionFactory(_mockCredentialStore.Object, _mockLogger.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<Exception>(
            async () => await factory.CreateSchoolContextAsync(school));

        Assert.Contains("Key Vault unavailable", exception.Message);
    }

    [Fact]
    public async Task CreateSchoolContextAsync_LogsSchoolNameAndPrefix()
    {
        // Arrange
        var school = new School
        {
            SchoolId = 1,
            Name = "City High School",
            KeyVaultSchoolPrefix = "CityHighSchool",
            DatabaseName = "School_CityHigh"
        };

        var connectionString = "Server=test.database.windows.net;Database=School_CityHigh;";

        _mockCredentialStore
            .Setup(x => x.GetSchoolSecretAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(connectionString);

        var factory = new SchoolDatabaseConnectionFactory(_mockCredentialStore.Object, _mockLogger.Object);

        // Act
        await factory.CreateSchoolContextAsync(school);

        // Assert - Verify that logging occurred with school information
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains("City High School") && v.ToString()!.Contains("CityHighSchool")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateSchoolContextAsync_WithSpecialCharactersInPrefix_WorksCorrectly()
    {
        // Arrange - Test that prefixes with hyphens work correctly
        var school = new School
        {
            SchoolId = 3,
            Name = "Jefferson-Lincoln High",
            KeyVaultSchoolPrefix = "Jefferson-Lincoln-High",
            DatabaseName = "School_JeffersonLincoln"
        };

        var connectionString = "Server=test.database.windows.net;Database=School_JeffersonLincoln;";

        _mockCredentialStore
            .Setup(x => x.GetSchoolSecretAsync(
                "Jefferson-Lincoln-High",
                KeyVaultSecretNaming.School.ConnectionString,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(connectionString);

        var factory = new SchoolDatabaseConnectionFactory(_mockCredentialStore.Object, _mockLogger.Object);

        // Act
        var context = await factory.CreateSchoolContextAsync(school);

        // Assert
        Assert.NotNull(context);
        _mockCredentialStore.Verify(
            x => x.GetSchoolSecretAsync(
                "Jefferson-Lincoln-High",
                KeyVaultSecretNaming.School.ConnectionString,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateSchoolContextAsync_MultipleCallsForSameSchool_RetrievesSecretEachTime()
    {
        // Arrange - Verify that connection strings are retrieved fresh each time (no caching)
        var school = new School
        {
            SchoolId = 1,
            Name = "City High School",
            KeyVaultSchoolPrefix = "CityHighSchool",
            DatabaseName = "School_CityHigh"
        };

        var connectionString = "Server=test.database.windows.net;Database=School_CityHigh;";

        _mockCredentialStore
            .Setup(x => x.GetSchoolSecretAsync(
                "CityHighSchool",
                KeyVaultSecretNaming.School.ConnectionString,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(connectionString);

        var factory = new SchoolDatabaseConnectionFactory(_mockCredentialStore.Object, _mockLogger.Object);

        // Act - Call twice
        var context1 = await factory.CreateSchoolContextAsync(school);
        var context2 = await factory.CreateSchoolContextAsync(school);

        // Assert - Verify secret was retrieved twice (no caching)
        Assert.NotNull(context1);
        Assert.NotNull(context2);
        Assert.NotSame(context1, context2); // Different context instances

        _mockCredentialStore.Verify(
            x => x.GetSchoolSecretAsync(
                "CityHighSchool",
                KeyVaultSecretNaming.School.ConnectionString,
                It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    #endregion

    #region Integration with KeyVaultSecretNaming Tests

    [Fact]
    public async Task CreateSchoolContextAsync_UsesKeyVaultSecretNamingConstant()
    {
        // Arrange - Verify integration with KeyVaultSecretNaming.School.ConnectionString
        var school = new School
        {
            SchoolId = 1,
            Name = "City High School",
            KeyVaultSchoolPrefix = "CityHighSchool",
            DatabaseName = "School_CityHigh"
        };

        var connectionString = "Server=test.database.windows.net;Database=School_CityHigh;";

        // Capture the functional name parameter
        string? capturedFunctionalName = null;
        _mockCredentialStore
            .Setup(x => x.GetSchoolSecretAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, string, CancellationToken>((prefix, funcName, ct) => capturedFunctionalName = funcName)
            .ReturnsAsync(connectionString);

        var factory = new SchoolDatabaseConnectionFactory(_mockCredentialStore.Object, _mockLogger.Object);

        // Act
        await factory.CreateSchoolContextAsync(school);

        // Assert - Verify that the constant value is used
        Assert.Equal(KeyVaultSecretNaming.School.ConnectionString, capturedFunctionalName);
        Assert.Equal("ConnectionString", capturedFunctionalName);
    }

    #endregion
}
