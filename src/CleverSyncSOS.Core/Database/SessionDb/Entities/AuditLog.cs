namespace CleverSyncSOS.Core.Database.SessionDb.Entities;

/// <summary>
/// Represents an audit log entry for security and compliance tracking
/// </summary>
public class AuditLog
{
    public int AuditLogId { get; set; }

    /// <summary>
    /// Timestamp of the event
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// User ID if authenticated, null for failed login attempts
    /// </summary>
    public int? UserId { get; set; }

    /// <summary>
    /// Email or username attempting authentication
    /// </summary>
    public string? UserIdentifier { get; set; }

    /// <summary>
    /// Action performed (e.g., Login, Logout, CleverLogin, BypassLogin, BypassLoginFailed, etc.)
    /// </summary>
    public string Action { get; set; } = string.Empty;

    /// <summary>
    /// IP address of the client
    /// </summary>
    public string? IpAddress { get; set; }

    /// <summary>
    /// User agent string from the browser
    /// </summary>
    public string? UserAgent { get; set; }

    /// <summary>
    /// Success or failure
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Additional details or error message
    /// </summary>
    public string? Details { get; set; }

    /// <summary>
    /// Resource or entity affected (e.g., "School:123", "District:abc")
    /// </summary>
    public string? Resource { get; set; }

    // Navigation property
    public User? User { get; set; }
}
