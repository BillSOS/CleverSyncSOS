using CleverSyncSOS.Core.Database.SessionDb;
using CleverSyncSOS.Core.Database.SessionDb.Entities;

namespace CleverSyncSOS.AdminPortal.Services;

/// <summary>
/// Implementation of audit logging service
/// </summary>
public class AuditLogService : IAuditLogService
{
    private readonly SessionDbContext _dbContext;
    private readonly ILogger<AuditLogService> _logger;

    public AuditLogService(
        SessionDbContext dbContext,
        ILogger<AuditLogService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task LogAuthenticationEventAsync(
        string action,
        bool success,
        string? userIdentifier = null,
        int? userId = null,
        string? details = null,
        string? ipAddress = null,
        string? userAgent = null)
    {
        await LogEventAsync(
            action,
            success,
            userId,
            userIdentifier,
            resource: null,
            details,
            ipAddress,
            userAgent);
    }

    public async Task LogEventAsync(
        string action,
        bool success,
        int? userId = null,
        string? userIdentifier = null,
        string? resource = null,
        string? details = null,
        string? ipAddress = null,
        string? userAgent = null)
    {
        try
        {
            var auditLog = new AuditLog
            {
                Timestamp = DateTime.UtcNow,
                UserId = userId > 0 ? userId : null, // Treat 0 as null to avoid FK constraint violation
                UserIdentifier = userIdentifier,
                Action = action,
                IpAddress = ipAddress,
                UserAgent = userAgent,
                Success = success,
                Details = details,
                Resource = resource
            };

            _dbContext.AuditLogs.Add(auditLog);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Audit log created: Action={Action}, Success={Success}, User={UserIdentifier}, IP={IpAddress}",
                action, success, userIdentifier ?? userId?.ToString() ?? "Anonymous", ipAddress);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create audit log entry for action {Action}", action);
            // Don't throw - audit logging should not break the application flow
        }
    }
}
