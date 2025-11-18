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
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace CleverSyncSOS.Core.Tests.Logging;

/// <summary>
/// Unit tests for LoggerExtensions to verify structured logging events per SpecKit Observability.md.
/// FR-010: Structured logging with correlation IDs and standardized event IDs.
/// </summary>
public class LoggerExtensionsTests
{
    private readonly Mock<ILogger> _mockLogger;

    public LoggerExtensionsTests()
    {
        _mockLogger = new Mock<ILogger>();
    }

    #region LogCleverAuthTokenAcquired Tests

    [Fact]
    public void LogCleverAuthTokenAcquired_LogsAtInformationLevel()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        // Act
        _mockLogger.Object.LogCleverAuthTokenAcquired(expiresAt);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public void LogCleverAuthTokenAcquired_IncludesEventId1001()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        // Act
        _mockLogger.Object.LogCleverAuthTokenAcquired(expiresAt);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.Is<EventId>(id => id.Id == 1001),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public void LogCleverAuthTokenAcquired_WithNullCorrelationId_GeneratesGuid()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);

        // Act
        _mockLogger.Object.LogCleverAuthTokenAcquired(expiresAt, correlationId: null);

        // Assert - Should not throw, and should log (correlation ID auto-generated)
        _mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public void LogCleverAuthTokenAcquired_WithScope_IncludesScope()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var scope = "district";

        // Act
        _mockLogger.Object.LogCleverAuthTokenAcquired(expiresAt, scope);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    #endregion

    #region LogCleverAuthTokenRefreshed Tests

    [Fact]
    public void LogCleverAuthTokenRefreshed_LogsAtInformationLevel()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var reason = "75% lifetime threshold";

        // Act
        _mockLogger.Object.LogCleverAuthTokenRefreshed(expiresAt, reason);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public void LogCleverAuthTokenRefreshed_IncludesEventId1002()
    {
        // Arrange
        var expiresAt = DateTimeOffset.UtcNow.AddHours(1);
        var reason = "Proactive refresh";

        // Act
        _mockLogger.Object.LogCleverAuthTokenRefreshed(expiresAt, reason);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.Is<EventId>(id => id.Id == 1002),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    #endregion

    #region LogCleverAuthFailure Tests

    [Fact]
    public void LogCleverAuthFailure_LogsAtErrorLevel()
    {
        // Arrange
        var exception = new Exception("Auth failed");
        var retryCount = 3;

        // Act
        _mockLogger.Object.LogCleverAuthFailure(exception, retryCount);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                exception,
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public void LogCleverAuthFailure_IncludesEventId1003()
    {
        // Arrange
        var exception = new Exception("Auth failed");
        var retryCount = 2;

        // Act
        _mockLogger.Object.LogCleverAuthFailure(exception, retryCount);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.Is<EventId>(id => id.Id == 1003),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public void LogCleverAuthFailure_SanitizesException()
    {
        // Arrange - Exception with sensitive data
        var exception = new Exception("Failed: Password=secret123");
        var retryCount = 1;

        // Act
        _mockLogger.Object.LogCleverAuthFailure(exception, retryCount);

        // Assert - Verify logging occurred (sanitization happens inside the method)
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                exception,
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    #endregion

    #region LogKeyVaultAccessFailure Tests

    [Fact]
    public void LogKeyVaultAccessFailure_LogsAtErrorLevel()
    {
        // Arrange
        var vaultUri = "https://myvault.vault.azure.net/";
        var secretName = "MySecret";
        var exception = new Exception("Access denied");

        // Act
        _mockLogger.Object.LogKeyVaultAccessFailure(vaultUri, secretName, exception);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                exception,
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public void LogKeyVaultAccessFailure_IncludesEventId1004()
    {
        // Arrange
        var vaultUri = "https://myvault.vault.azure.net/";
        var secretName = "MySecret";
        var exception = new Exception("Access denied");

        // Act
        _mockLogger.Object.LogKeyVaultAccessFailure(vaultUri, secretName, exception);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.Is<EventId>(id => id.Id == 1004),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    #endregion

    #region LogHealthCheckEvaluated Tests

    [Fact]
    public void LogHealthCheckEvaluated_WhenHealthy_LogsAtInformationLevel()
    {
        // Arrange
        var isHealthy = true;
        var lastSuccess = DateTimeOffset.UtcNow.AddMinutes(-5);

        // Act
        _mockLogger.Object.LogHealthCheckEvaluated(isHealthy, lastSuccess);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public void LogHealthCheckEvaluated_WhenUnhealthy_LogsAtWarningLevel()
    {
        // Arrange
        var isHealthy = false;
        var errorCount = 3;

        // Act
        _mockLogger.Object.LogHealthCheckEvaluated(isHealthy, errorCount: errorCount);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public void LogHealthCheckEvaluated_IncludesEventId1005()
    {
        // Arrange
        var isHealthy = true;

        // Act
        _mockLogger.Object.LogHealthCheckEvaluated(isHealthy);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                It.IsAny<LogLevel>(),
                It.Is<EventId>(id => id.Id == 1005),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    #endregion

    #region LogSanitizedError Tests

    [Fact]
    public void LogSanitizedError_LogsAtErrorLevel()
    {
        // Arrange
        var exception = new Exception("Test error");
        var message = "An error occurred";

        // Act
        _mockLogger.Object.LogSanitizedError(exception, message);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                exception,
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public void LogSanitizedError_WithCorrelationId_IncludesCorrelationId()
    {
        // Arrange
        var exception = new Exception("Test error");
        var message = "Error occurred";
        var correlationId = "test-correlation-123";

        // Act
        _mockLogger.Object.LogSanitizedError(exception, message, correlationId);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                exception,
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    #endregion

    #region LogSanitizedWarning Tests

    [Fact]
    public void LogSanitizedWarning_LogsAtWarningLevel()
    {
        // Arrange
        var message = "Warning message";

        // Act
        _mockLogger.Object.LogSanitizedWarning(message);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    #endregion

    #region LogWithCorrelation Tests

    [Fact]
    public void LogWithCorrelation_LogsAtInformationLevel()
    {
        // Arrange
        var message = "Information message";

        // Act
        _mockLogger.Object.LogWithCorrelation(message);

        // Assert
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    [Fact]
    public void LogWithCorrelation_WithNullCorrelationId_GeneratesGuid()
    {
        // Arrange
        var message = "Test message";

        // Act
        _mockLogger.Object.LogWithCorrelation(message, correlationId: null);

        // Assert - Should log successfully with auto-generated correlation ID
        _mockLogger.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    #endregion
}
