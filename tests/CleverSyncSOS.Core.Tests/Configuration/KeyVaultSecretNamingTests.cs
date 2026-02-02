// ---
// speckit:
//   type: test
//   source: SpecKit/Plans/003-key-vault-naming-standardization/plan.md
//   section: Phase 1 - Core Infrastructure
//   version: 2.0.0
// ---

using CleverSyncSOS.Core.Configuration;
using Xunit;

namespace CleverSyncSOS.Core.Tests.Configuration;

/// <summary>
/// Unit tests for KeyVaultSecretNaming helper class.
/// Tests the naming convention builder methods and validation logic.
/// </summary>
public class KeyVaultSecretNamingTests
{
    #region BuildDistrictSecretName Tests

    [Fact]
    public void BuildDistrictSecretName_WithValidInputs_ReturnsCorrectFormat()
    {
        // Arrange
        var prefix = "NorthCentral";
        var functionalName = "ApiToken";

        // Act
        var result = KeyVaultSecretNaming.BuildDistrictSecretName(prefix, functionalName);

        // Assert
        Assert.Equal("NorthCentral--ApiToken", result);
    }

    [Theory]
    [InlineData(null, "ApiToken")]
    [InlineData("", "ApiToken")]
    [InlineData("   ", "ApiToken")]
    public void BuildDistrictSecretName_WithInvalidPrefix_ThrowsArgumentException(
        string invalidPrefix, string functionalName)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            KeyVaultSecretNaming.BuildDistrictSecretName(invalidPrefix, functionalName));

        Assert.Contains("KeyVaultDistrictPrefix", exception.Message);
    }

    [Theory]
    [InlineData("NorthCentral", null)]
    [InlineData("NorthCentral", "")]
    [InlineData("NorthCentral", "   ")]
    public void BuildDistrictSecretName_WithInvalidFunctionalName_ThrowsArgumentException(
        string prefix, string invalidFunctionalName)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            KeyVaultSecretNaming.BuildDistrictSecretName(prefix, invalidFunctionalName));

        Assert.Contains("FunctionalName", exception.Message);
    }

    [Theory]
    [InlineData("District1", "ContactEmail", "District1--ContactEmail")]
    [InlineData("SouthEast", "ConnectionString", "SouthEast--ConnectionString")]
    [InlineData("West-Region", "ApiToken", "West-Region--ApiToken")]
    public void BuildDistrictSecretName_WithVariousInputs_ReturnsCorrectFormat(
        string prefix, string functionalName, string expected)
    {
        // Act
        var result = KeyVaultSecretNaming.BuildDistrictSecretName(prefix, functionalName);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region BuildSchoolSecretName Tests

    [Fact]
    public void BuildSchoolSecretName_WithValidInputs_ReturnsCorrectFormat()
    {
        // Arrange
        var prefix = "CityHighSchool";
        var functionalName = "ConnectionString";

        // Act
        var result = KeyVaultSecretNaming.BuildSchoolSecretName(prefix, functionalName);

        // Assert
        Assert.Equal("CityHighSchool--ConnectionString", result);
    }

    [Theory]
    [InlineData(null, "ConnectionString")]
    [InlineData("", "ConnectionString")]
    [InlineData("   ", "ConnectionString")]
    public void BuildSchoolSecretName_WithInvalidPrefix_ThrowsArgumentException(
        string invalidPrefix, string functionalName)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            KeyVaultSecretNaming.BuildSchoolSecretName(invalidPrefix, functionalName));

        Assert.Contains("KeyVaultSchoolPrefix", exception.Message);
    }

    [Theory]
    [InlineData("CityHighSchool", null)]
    [InlineData("CityHighSchool", "")]
    [InlineData("CityHighSchool", "   ")]
    public void BuildSchoolSecretName_WithInvalidFunctionalName_ThrowsArgumentException(
        string prefix, string invalidFunctionalName)
    {
        // Act & Assert
        var exception = Assert.Throws<ArgumentException>(() =>
            KeyVaultSecretNaming.BuildSchoolSecretName(prefix, invalidFunctionalName));

        Assert.Contains("FunctionalName", exception.Message);
    }

    [Theory]
    [InlineData("LincolnElementary", "ApiKey", "LincolnElementary--ApiKey")]
    [InlineData("WashingtonMiddle", "DatabasePassword", "WashingtonMiddle--DatabasePassword")]
    [InlineData("Jefferson-High", "ConnectionString", "Jefferson-High--ConnectionString")]
    public void BuildSchoolSecretName_WithVariousInputs_ReturnsCorrectFormat(
        string prefix, string functionalName, string expected)
    {
        // Act
        var result = KeyVaultSecretNaming.BuildSchoolSecretName(prefix, functionalName);

        // Assert
        Assert.Equal(expected, result);
    }

    #endregion

    #region Global Secret Name Constants Tests

    [Fact]
    public void GlobalSecretNames_AreDefinedCorrectly()
    {
        // Assert - Verify constants exist and have expected values
        Assert.Equal("ClientId", KeyVaultSecretNaming.Global.ClientId);
        Assert.Equal("ClientSecret", KeyVaultSecretNaming.Global.ClientSecret);
        Assert.Equal("SuperAdminPassword", KeyVaultSecretNaming.Global.SuperAdminPassword);
        Assert.Equal("SessionDbPassword", KeyVaultSecretNaming.Global.SessionDbPassword);
        Assert.Equal("SessionDbConnectionString", KeyVaultSecretNaming.Global.SessionDbConnectionString);
        Assert.Equal("AccessToken", KeyVaultSecretNaming.Global.AccessToken);
    }

    [Fact]
    public void GlobalSecretNames_DoNotContainPrefixSeparator()
    {
        // Global secrets should not contain the "--" separator
        Assert.DoesNotContain("--", KeyVaultSecretNaming.Global.ClientId);
        Assert.DoesNotContain("--", KeyVaultSecretNaming.Global.ClientSecret);
        Assert.DoesNotContain("--", KeyVaultSecretNaming.Global.SuperAdminPassword);
        Assert.DoesNotContain("--", KeyVaultSecretNaming.Global.SessionDbPassword);
        Assert.DoesNotContain("--", KeyVaultSecretNaming.Global.SessionDbConnectionString);
        Assert.DoesNotContain("--", KeyVaultSecretNaming.Global.AccessToken);
    }

    #endregion

    #region District Secret Name Constants Tests

    [Fact]
    public void DistrictSecretNames_AreDefinedCorrectly()
    {
        // Assert
        Assert.Equal("ApiToken", KeyVaultSecretNaming.District.ApiToken);
        Assert.Equal("ContactEmail", KeyVaultSecretNaming.District.ContactEmail);
        Assert.Equal("ConnectionString", KeyVaultSecretNaming.District.ConnectionString);
    }

    [Fact]
    public void DistrictSecretNames_DoNotContainPrefixSeparator()
    {
        // Functional names should not contain the "--" separator (it's added by builder)
        Assert.DoesNotContain("--", KeyVaultSecretNaming.District.ApiToken);
        Assert.DoesNotContain("--", KeyVaultSecretNaming.District.ContactEmail);
        Assert.DoesNotContain("--", KeyVaultSecretNaming.District.ConnectionString);
    }

    #endregion

    #region School Secret Name Constants Tests

    [Fact]
    public void SchoolSecretNames_AreDefinedCorrectly()
    {
        // Assert
        Assert.Equal("ConnectionString", KeyVaultSecretNaming.School.ConnectionString);
        Assert.Equal("ApiKey", KeyVaultSecretNaming.School.ApiKey);
        Assert.Equal("DatabasePassword", KeyVaultSecretNaming.School.DatabasePassword);
    }

    [Fact]
    public void SchoolSecretNames_DoNotContainPrefixSeparator()
    {
        // Functional names should not contain the "--" separator (it's added by builder)
        Assert.DoesNotContain("--", KeyVaultSecretNaming.School.ConnectionString);
        Assert.DoesNotContain("--", KeyVaultSecretNaming.School.ApiKey);
        Assert.DoesNotContain("--", KeyVaultSecretNaming.School.DatabasePassword);
    }

    #endregion

    #region Edge Cases Tests

    [Fact]
    public void BuildDistrictSecretName_WithSpecialCharactersInPrefix_WorksCorrectly()
    {
        // Arrange - Key Vault secret names support hyphens and underscores
        var prefix = "North-Central_District";
        var functionalName = "ApiToken";

        // Act
        var result = KeyVaultSecretNaming.BuildDistrictSecretName(prefix, functionalName);

        // Assert
        Assert.Equal("North-Central_District--ApiToken", result);
    }

    [Fact]
    public void BuildSchoolSecretName_WithSpecialCharactersInPrefix_WorksCorrectly()
    {
        // Arrange
        var prefix = "Lincoln-Elementary_School";
        var functionalName = "ConnectionString";

        // Act
        var result = KeyVaultSecretNaming.BuildSchoolSecretName(prefix, functionalName);

        // Assert
        Assert.Equal("Lincoln-Elementary_School--ConnectionString", result);
    }

    #endregion
}
