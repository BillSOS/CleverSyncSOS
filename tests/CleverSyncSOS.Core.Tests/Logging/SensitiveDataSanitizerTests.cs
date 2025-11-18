// ---
// speckit:
//   type: test
//   source: SpecKit/Specs/001-clever-api-auth/spec-1.md
//   section: FR-010
//   plan: SpecKit/Plans/001-clever-api-auth/plan.md
//   phase: Testing
//   version: 1.0.0
// ---

using CleverSyncSOS.Core.Logging;
using Xunit;

namespace CleverSyncSOS.Core.Tests.Logging;

/// <summary>
/// Unit tests for SensitiveDataSanitizer to verify credential and PII sanitization.
/// FR-010: Structured logging with sanitization to prevent credential leakage.
/// </summary>
public class SensitiveDataSanitizerTests
{
    private const string RedactedPlaceholder = "***REDACTED***";

    #region SanitizeToken Tests

    [Fact]
    public void SanitizeToken_WithNullInput_ReturnsRedacted()
    {
        // Arrange
        string? token = null;

        // Act
        var result = SensitiveDataSanitizer.SanitizeToken(token);

        // Assert
        Assert.Equal(RedactedPlaceholder, result);
    }

    [Fact]
    public void SanitizeToken_WithEmptyString_ReturnsRedacted()
    {
        // Arrange
        var token = string.Empty;

        // Act
        var result = SensitiveDataSanitizer.SanitizeToken(token);

        // Assert
        Assert.Equal(RedactedPlaceholder, result);
    }

    [Fact]
    public void SanitizeToken_WithWhitespace_ReturnsRedacted()
    {
        // Arrange
        var token = "   ";

        // Act
        var result = SensitiveDataSanitizer.SanitizeToken(token);

        // Assert
        Assert.Equal(RedactedPlaceholder, result);
    }

    [Fact]
    public void SanitizeToken_WithShortToken_ReturnsRedacted()
    {
        // Arrange
        var token = "abc"; // Less than 4 characters

        // Act
        var result = SensitiveDataSanitizer.SanitizeToken(token);

        // Assert
        Assert.Equal(RedactedPlaceholder, result);
    }

    [Fact]
    public void SanitizeToken_WithNormalToken_ShowsLastFourChars()
    {
        // Arrange
        var token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.dozjgNryP4J3jVmNHl0w5N_XgL0n3I9PlFUP0THsR8U";

        // Act
        var result = SensitiveDataSanitizer.SanitizeToken(token);

        // Assert
        Assert.StartsWith(RedactedPlaceholder, result);
        Assert.EndsWith("sR8U", result); // Last 4 chars
        Assert.Contains("...", result);
    }

    #endregion

    #region SanitizeConnectionString Tests

    [Fact]
    public void SanitizeConnectionString_WithNullInput_ReturnsRedacted()
    {
        // Arrange
        string? connectionString = null;

        // Act
        var result = SensitiveDataSanitizer.SanitizeConnectionString(connectionString);

        // Assert
        Assert.Equal(RedactedPlaceholder, result);
    }

    [Fact]
    public void SanitizeConnectionString_WithPasswordField_MasksPassword()
    {
        // Arrange
        var connectionString = "Server=myserver;Database=mydb;Password=SuperSecret123;";

        // Act
        var result = SensitiveDataSanitizer.SanitizeConnectionString(connectionString);

        // Assert
        Assert.Contains($"Password={RedactedPlaceholder}", result);
        Assert.DoesNotContain("SuperSecret123", result);
        Assert.Contains("Server=myserver", result); // Non-sensitive parts preserved
    }

    [Fact]
    public void SanitizeConnectionString_WithPwdField_MasksPassword()
    {
        // Arrange
        var connectionString = "Server=myserver;Database=mydb;Pwd=MyPassword;";

        // Act
        var result = SensitiveDataSanitizer.SanitizeConnectionString(connectionString);

        // Assert
        Assert.Contains($"Password={RedactedPlaceholder}", result);
        Assert.DoesNotContain("MyPassword", result);
    }

    [Fact]
    public void SanitizeConnectionString_WithAccountKey_MasksKey()
    {
        // Arrange
        var connectionString = "DefaultEndpointsProtocol=https;AccountName=myaccount;AccountKey=mykey123==;EndpointSuffix=core.windows.net";

        // Act
        var result = SensitiveDataSanitizer.SanitizeConnectionString(connectionString);

        // Assert
        Assert.Contains($"AccountKey={RedactedPlaceholder}", result);
        Assert.DoesNotContain("mykey123==", result);
        Assert.Contains("AccountName=myaccount", result);
    }

    [Fact]
    public void SanitizeConnectionString_WithUserId_MasksUserId()
    {
        // Arrange
        var connectionString = "Server=myserver;Database=mydb;User Id=admin;Password=secret;";

        // Act
        var result = SensitiveDataSanitizer.SanitizeConnectionString(connectionString);

        // Assert
        Assert.Contains("User Id=a***n", result); // First and last char preserved
        Assert.DoesNotContain("admin", result);
    }

    [Fact]
    public void SanitizeConnectionString_WithMultipleSecrets_MasksAll()
    {
        // Arrange
        var connectionString = "Server=myserver;User Id=testuser;Password=testpass;AccountKey=testkey;";

        // Act
        var result = SensitiveDataSanitizer.SanitizeConnectionString(connectionString);

        // Assert
        Assert.Contains($"Password={RedactedPlaceholder}", result);
        Assert.Contains($"AccountKey={RedactedPlaceholder}", result);
        Assert.DoesNotContain("testpass", result);
        Assert.DoesNotContain("testkey", result);
    }

    #endregion

    #region SanitizeException Tests

    [Fact]
    public void SanitizeException_WithNullException_ReturnsEmptyString()
    {
        // Arrange
        Exception? exception = null;

        // Act
        var result = SensitiveDataSanitizer.SanitizeException(exception);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void SanitizeException_WithBearerToken_RemovesToken()
    {
        // Arrange
        var exception = new Exception("Failed to authenticate. Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9");

        // Act
        var result = SensitiveDataSanitizer.SanitizeException(exception);

        // Assert
        Assert.Contains($"Bearer {RedactedPlaceholder}", result);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", result);
    }

    [Fact]
    public void SanitizeException_WithConnectionString_RemovesPassword()
    {
        // Arrange
        var exception = new Exception("Connection failed: Server=myserver;Password=secret123;");

        // Act
        var result = SensitiveDataSanitizer.SanitizeException(exception);

        // Assert
        Assert.Contains($"Password={RedactedPlaceholder}", result);
        Assert.DoesNotContain("secret123", result);
    }

    [Fact]
    public void SanitizeException_WithInnerException_SanitizesBoth()
    {
        // Arrange
        var innerException = new Exception("Inner error: token=secret456");
        var outerException = new Exception("Outer error: password=secret789", innerException);

        // Act
        var result = SensitiveDataSanitizer.SanitizeException(outerException);

        // Assert
        Assert.Contains("Exception", result);
        Assert.Contains("-->", result); // Inner exception marker
        Assert.Contains($"token={RedactedPlaceholder}", result);
        Assert.Contains($"Password={RedactedPlaceholder}", result); // Connection string sanitizer capitalizes Password
        Assert.DoesNotContain("secret456", result);
        Assert.DoesNotContain("secret789", result);
    }

    [Fact]
    public void SanitizeException_WithBasicAuth_RemovesCredentials()
    {
        // Arrange
        var exception = new Exception("Auth failed: Basic dXNlcm5hbWU6cGFzc3dvcmQ=");

        // Act
        var result = SensitiveDataSanitizer.SanitizeException(exception);

        // Assert
        Assert.Contains($"Basic {RedactedPlaceholder}", result);
        Assert.DoesNotContain("dXNlcm5hbWU6cGFzc3dvcmQ=", result);
    }

    #endregion

    #region SanitizeUrl Tests

    [Fact]
    public void SanitizeUrl_WithNullInput_ReturnsEmptyString()
    {
        // Arrange
        string? url = null;

        // Act
        var result = SensitiveDataSanitizer.SanitizeUrl(url);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void SanitizeUrl_WithoutQueryParams_ReturnsOriginal()
    {
        // Arrange
        var url = "https://api.example.com/v1/users";

        // Act
        var result = SensitiveDataSanitizer.SanitizeUrl(url);

        // Assert
        Assert.Equal(url, result);
    }

    [Fact]
    public void SanitizeUrl_WithQueryParams_RemovesParams()
    {
        // Arrange
        var url = "https://api.example.com/v1/users?token=secret123&id=456";

        // Act
        var result = SensitiveDataSanitizer.SanitizeUrl(url);

        // Assert
        Assert.Equal("https://api.example.com/v1/users?***REDACTED***", result);
        Assert.DoesNotContain("secret123", result);
        Assert.DoesNotContain("id=456", result);
    }

    [Fact]
    public void SanitizeUrl_WithSingleQueryParam_RemovesParam()
    {
        // Arrange
        var url = "https://api.example.com/oauth/token?client_secret=mysecret";

        // Act
        var result = SensitiveDataSanitizer.SanitizeUrl(url);

        // Assert
        Assert.StartsWith("https://api.example.com/oauth/token?", result);
        Assert.DoesNotContain("mysecret", result);
    }

    #endregion

    #region SanitizeEmail Tests

    [Fact]
    public void SanitizeEmail_WithNullInput_ReturnsRedacted()
    {
        // Arrange
        string? email = null;

        // Act
        var result = SensitiveDataSanitizer.SanitizeEmail(email);

        // Assert
        Assert.Equal(RedactedPlaceholder, result);
    }

    [Fact]
    public void SanitizeEmail_WithValidEmail_MasksLocalPart()
    {
        // Arrange
        var email = "john.doe@example.com";

        // Act
        var result = SensitiveDataSanitizer.SanitizeEmail(email);

        // Assert
        Assert.StartsWith("j***", result);
        Assert.EndsWith("@example.com", result);
        Assert.DoesNotContain("john.doe", result);
    }

    [Fact]
    public void SanitizeEmail_WithSingleCharLocalPart_MasksProperly()
    {
        // Arrange
        var email = "a@example.com";

        // Act
        var result = SensitiveDataSanitizer.SanitizeEmail(email);

        // Assert
        Assert.Equal("***@example.com", result);
    }

    [Fact]
    public void SanitizeEmail_WithInvalidFormat_ReturnsRedacted()
    {
        // Arrange
        var email = "notanemail";

        // Act
        var result = SensitiveDataSanitizer.SanitizeEmail(email);

        // Assert
        Assert.Equal(RedactedPlaceholder, result);
    }

    #endregion

    #region SanitizeName Tests

    [Fact]
    public void SanitizeName_WithNullInput_ReturnsRedacted()
    {
        // Arrange
        string? name = null;

        // Act
        var result = SensitiveDataSanitizer.SanitizeName(name);

        // Assert
        Assert.Equal(RedactedPlaceholder, result);
    }

    [Fact]
    public void SanitizeName_WithFullName_ShowsInitials()
    {
        // Arrange
        var name = "John Doe";

        // Act
        var result = SensitiveDataSanitizer.SanitizeName(name);

        // Assert
        Assert.Equal("J*** D***", result);
        Assert.DoesNotContain("John", result);
        Assert.DoesNotContain("Doe", result);
    }

    [Fact]
    public void SanitizeName_WithSingleName_ShowsInitial()
    {
        // Arrange
        var name = "Madonna";

        // Act
        var result = SensitiveDataSanitizer.SanitizeName(name);

        // Assert
        Assert.Equal("M***", result);
    }

    [Fact]
    public void SanitizeName_WithThreePartName_SanitizesAll()
    {
        // Arrange
        var name = "John Michael Doe";

        // Act
        var result = SensitiveDataSanitizer.SanitizeName(name);

        // Assert
        Assert.Equal("J*** M*** D***", result);
    }

    #endregion

    #region SanitizeHttpResponse Tests

    [Fact]
    public void SanitizeHttpResponse_WithNullInput_ReturnsEmptyString()
    {
        // Arrange
        string? response = null;

        // Act
        var result = SensitiveDataSanitizer.SanitizeHttpResponse(response);

        // Assert
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void SanitizeHttpResponse_WithAccessToken_RedactsToken()
    {
        // Arrange
        var response = "{\"access_token\":\"eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9\",\"expires_in\":3600}";

        // Act
        var result = SensitiveDataSanitizer.SanitizeHttpResponse(response);

        // Assert
        Assert.Contains($"\"access_token\":\"{RedactedPlaceholder}\"", result);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", result);
        Assert.Contains("\"expires_in\":3600", result); // Non-sensitive preserved
    }

    [Fact]
    public void SanitizeHttpResponse_WithRefreshToken_RedactsToken()
    {
        // Arrange
        var response = "{\"refresh_token\":\"mysecrettoken123\"}";

        // Act
        var result = SensitiveDataSanitizer.SanitizeHttpResponse(response);

        // Assert
        Assert.Contains($"\"refresh_token\":\"{RedactedPlaceholder}\"", result);
        Assert.DoesNotContain("mysecrettoken123", result);
    }

    [Fact]
    public void SanitizeHttpResponse_WithBearerToken_RedactsToken()
    {
        // Arrange
        var response = "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";

        // Act
        var result = SensitiveDataSanitizer.SanitizeHttpResponse(response);

        // Assert
        Assert.Contains($"Bearer {RedactedPlaceholder}", result);
        Assert.DoesNotContain("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9", result);
    }

    #endregion

    #region CreateSafeErrorSummary Tests

    [Fact]
    public void CreateSafeErrorSummary_WithException_IncludesExceptionType()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error");

        // Act
        var result = SensitiveDataSanitizer.CreateSafeErrorSummary(exception);

        // Assert
        Assert.Contains("InvalidOperationException", result);
        Assert.Contains("Error Type:", result);
    }

    [Fact]
    public void CreateSafeErrorSummary_WithSensitiveMessage_SanitizesMessage()
    {
        // Arrange
        var exception = new Exception("Failed to connect: Password=secret123");

        // Act
        var result = SensitiveDataSanitizer.CreateSafeErrorSummary(exception);

        // Assert
        Assert.Contains($"Password={RedactedPlaceholder}", result);
        Assert.DoesNotContain("secret123", result);
    }

    [Fact]
    public void CreateSafeErrorSummary_WithContext_IncludesContext()
    {
        // Arrange
        var exception = new Exception("Test error");
        var context = "School: City High";

        // Act
        var result = SensitiveDataSanitizer.CreateSafeErrorSummary(exception, context);

        // Assert
        Assert.Contains("Context: School: City High", result);
    }

    [Fact]
    public void CreateSafeErrorSummary_WithNullContext_OmitsContext()
    {
        // Arrange
        var exception = new Exception("Test error");

        // Act
        var result = SensitiveDataSanitizer.CreateSafeErrorSummary(exception, null);

        // Assert
        Assert.DoesNotContain("Context:", result);
    }

    #endregion
}
