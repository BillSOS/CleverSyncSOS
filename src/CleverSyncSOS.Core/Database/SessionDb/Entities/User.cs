namespace CleverSyncSOS.Core.Database.SessionDb.Entities;

/// <summary>
/// Represents a user in the admin portal
/// </summary>
public class User
{
    public int UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// User role: SchoolAdmin, DistrictAdmin, or SuperAdmin
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Authentication source: Clever or Bypass
    /// </summary>
    public string AuthenticationSource { get; set; } = string.Empty;

    /// <summary>
    /// Internal school ID (for SchoolAdmin only)
    /// </summary>
    public int? SchoolId { get; set; }

    /// <summary>
    /// Clever district ID (for SchoolAdmin and DistrictAdmin)
    /// </summary>
    public string? DistrictId { get; set; }

    /// <summary>
    /// Clever user ID (null for SuperAdmin with Bypass auth)
    /// </summary>
    public string? CleverUserId { get; set; }

    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? CreatedBy { get; set; }
    public DateTime? LastLogin { get; set; }
    public DateTime? UpdatedAt { get; set; }

    // Navigation properties
    public School? School { get; set; }
    public District? District { get; set; }
}
