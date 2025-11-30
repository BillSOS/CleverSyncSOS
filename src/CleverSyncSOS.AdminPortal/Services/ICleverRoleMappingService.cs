using System.Security.Claims;

namespace CleverSyncSOS.AdminPortal.Services;

/// <summary>
/// Service for mapping Clever user data to admin portal roles and claims
/// </summary>
public interface ICleverRoleMappingService
{
    /// <summary>
    /// Maps Clever user information to role-based claims
    /// </summary>
    /// <param name="cleverUserId">The Clever user ID</param>
    /// <param name="cleverUserType">The Clever user type (e.g., "district_admin", "school_admin")</param>
    /// <param name="schoolIds">List of school IDs the user has access to</param>
    /// <param name="districtId">The district ID the user belongs to</param>
    /// <returns>List of claims to add to the user principal</returns>
    Task<List<Claim>> MapCleverUserToClaimsAsync(
        string cleverUserId,
        string cleverUserType,
        List<string>? schoolIds,
        string? districtId);
}
