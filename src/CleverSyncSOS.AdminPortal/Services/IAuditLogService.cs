namespace CleverSyncSOS.AdminPortal.Services;

/// <summary>
/// Service for logging audit events
/// </summary>
public interface IAuditLogService
{
    /// <summary>
    /// Logs an authentication event
    /// </summary>
    Task LogAuthenticationEventAsync(
        string action,
        bool success,
        string? userIdentifier = null,
        int? userId = null,
        string? details = null,
        string? ipAddress = null,
        string? userAgent = null);

    /// <summary>
    /// Logs a general audit event
    /// </summary>
    Task LogEventAsync(
        string action,
        bool success,
        int? userId = null,
        string? userIdentifier = null,
        string? resource = null,
        string? details = null,
        string? ipAddress = null,
        string? userAgent = null);
}
