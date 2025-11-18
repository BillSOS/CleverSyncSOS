using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Core.Logging;

/// <summary>
/// Extension methods for ILogger that implement structured logging events per SpecKit requirements.
/// Observability.md Lines 29-36: Required log events with correlation ID support.
/// </summary>
public static class LoggerExtensions
{
    // Event IDs for structured logging
    private const int CleverAuthTokenAcquiredEventId = 1001;
    private const int CleverAuthTokenRefreshedEventId = 1002;
    private const int CleverAuthFailureEventId = 1003;
    private const int KeyVaultAccessFailureEventId = 1004;
    private const int HealthCheckEvaluatedEventId = 1005;

    /// <summary>
    /// Logs when a Clever API authentication token is successfully acquired.
    /// </summary>
    public static void LogCleverAuthTokenAcquired(
        this ILogger logger,
        DateTimeOffset expiresAt,
        string? scope = null,
        string? correlationId = null)
    {
        logger.LogInformation(
            CleverAuthTokenAcquiredEventId,
            "Clever auth token acquired. ExpiresAt: {ExpiresAt}, Scope: {Scope}, CorrelationId: {CorrelationId}, RetrievedAt: {RetrievedAt}",
            expiresAt,
            scope ?? "default",
            correlationId ?? Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Logs when a Clever API authentication token is proactively refreshed.
    /// </summary>
    public static void LogCleverAuthTokenRefreshed(
        this ILogger logger,
        DateTimeOffset expiresAt,
        string refreshReason,
        string? correlationId = null)
    {
        logger.LogInformation(
            CleverAuthTokenRefreshedEventId,
            "Clever auth token refreshed. ExpiresAt: {ExpiresAt}, RefreshReason: {RefreshReason}, CorrelationId: {CorrelationId}",
            expiresAt,
            refreshReason,
            correlationId ?? Guid.NewGuid().ToString());
    }

    /// <summary>
    /// Logs when Clever API authentication fails.
    /// </summary>
    public static void LogCleverAuthFailure(
        this ILogger logger,
        Exception exception,
        int retryCount,
        string? correlationId = null)
    {
        var sanitizedError = SensitiveDataSanitizer.CreateSafeErrorSummary(exception);

        logger.LogError(
            CleverAuthFailureEventId,
            exception,
            "Clever authentication failed. Error: {SanitizedError}, RetryCount: {RetryCount}, CorrelationId: {CorrelationId}, Timestamp: {Timestamp}",
            sanitizedError,
            retryCount,
            correlationId ?? Guid.NewGuid().ToString(),
            DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Logs when Azure Key Vault access fails.
    /// </summary>
    public static void LogKeyVaultAccessFailure(
        this ILogger logger,
        string vaultUri,
        string secretName,
        Exception exception,
        string? correlationId = null)
    {
        var sanitizedError = SensitiveDataSanitizer.CreateSafeErrorSummary(exception);

        logger.LogError(
            KeyVaultAccessFailureEventId,
            exception,
            "Key Vault access failed. VaultUri: {VaultUri}, SecretName: {SecretName}, ExceptionType: {ExceptionType}, SanitizedError: {SanitizedError}, CorrelationId: {CorrelationId}",
            vaultUri,
            secretName,
            exception.GetType().Name,
            sanitizedError,
            correlationId ?? Guid.NewGuid().ToString());
    }

    /// <summary>
    /// Logs when a health check is evaluated.
    /// </summary>
    public static void LogHealthCheckEvaluated(
        this ILogger logger,
        bool isHealthy,
        DateTimeOffset? lastSuccessTimestamp = null,
        int errorCount = 0,
        string? correlationId = null)
    {
        var logLevel = isHealthy ? LogLevel.Information : LogLevel.Warning;

        logger.Log(
            logLevel,
            HealthCheckEvaluatedEventId,
            "Health check evaluated. IsHealthy: {IsHealthy}, LastSuccessTimestamp: {LastSuccessTimestamp}, ErrorCount: {ErrorCount}, CorrelationId: {CorrelationId}",
            isHealthy,
            lastSuccessTimestamp?.ToString() ?? "N/A",
            errorCount,
            correlationId ?? Guid.NewGuid().ToString());
    }

    /// <summary>
    /// Logs a sanitized error with full exception details removed.
    /// </summary>
    public static void LogSanitizedError(
        this ILogger logger,
        Exception exception,
        string message,
        string? correlationId = null,
        params object?[] args)
    {
        var sanitizedError = SensitiveDataSanitizer.CreateSafeErrorSummary(exception);

        var fullMessage = $"{message} | SanitizedError: {{SanitizedError}} | CorrelationId: {{CorrelationId}}";
        var fullArgs = args.Concat(new object?[] { sanitizedError, correlationId ?? Guid.NewGuid().ToString() }).ToArray();

        logger.LogError(exception, fullMessage, fullArgs);
    }

    /// <summary>
    /// Logs a warning with sanitization.
    /// </summary>
    public static void LogSanitizedWarning(
        this ILogger logger,
        string message,
        string? correlationId = null,
        params object?[] args)
    {
        var fullMessage = $"{message} | CorrelationId: {{CorrelationId}}";
        var fullArgs = args.Concat(new object?[] { correlationId ?? Guid.NewGuid().ToString() }).ToArray();

        logger.LogWarning(fullMessage, fullArgs);
    }

    /// <summary>
    /// Logs information with correlation ID.
    /// </summary>
    public static void LogWithCorrelation(
        this ILogger logger,
        string message,
        string? correlationId = null,
        params object?[] args)
    {
        var fullMessage = $"{message} | CorrelationId: {{CorrelationId}}";
        var fullArgs = args.Concat(new object?[] { correlationId ?? Guid.NewGuid().ToString() }).ToArray();

        logger.LogInformation(fullMessage, fullArgs);
    }
}
