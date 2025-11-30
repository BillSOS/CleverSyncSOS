namespace CleverSyncSOS.AdminPortal.Services;

/// <summary>
/// Service for accessing current user information from claims
/// </summary>
public interface ICurrentUserService
{
    /// <summary>
    /// Gets the current user's ID from the database
    /// </summary>
    int? UserId { get; }

    /// <summary>
    /// Gets the current user's email/username
    /// </summary>
    string? UserName { get; }

    /// <summary>
    /// Gets the current user's role
    /// </summary>
    string? Role { get; }

    /// <summary>
    /// Gets the current user's school ID (if SchoolAdmin)
    /// </summary>
    int? SchoolId { get; }

    /// <summary>
    /// Gets the current user's district ID (if SchoolAdmin or DistrictAdmin)
    /// </summary>
    string? DistrictId { get; }

    /// <summary>
    /// Gets whether the user is authenticated
    /// </summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets whether the user is a Super Admin
    /// </summary>
    bool IsSuperAdmin { get; }

    /// <summary>
    /// Gets whether the user is a District Admin or higher
    /// </summary>
    bool IsDistrictAdmin { get; }

    /// <summary>
    /// Gets whether the user is a School Admin or higher
    /// </summary>
    bool IsSchoolAdmin { get; }
}
