using System.Security.Claims;
using CleverSyncSOS.Core.Database.SessionDb;
using Microsoft.EntityFrameworkCore;

namespace CleverSyncSOS.AdminPortal.Services;

/// <summary>
/// Implementation of role mapping from Clever user data
/// </summary>
public class CleverRoleMappingService : ICleverRoleMappingService
{
    private readonly SessionDbContext _dbContext;
    private readonly ILogger<CleverRoleMappingService> _logger;

    public CleverRoleMappingService(
        SessionDbContext dbContext,
        ILogger<CleverRoleMappingService> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task<List<Claim>> MapCleverUserToClaimsAsync(
        string cleverUserId,
        string cleverUserType,
        List<string>? schoolIds,
        string? districtId)
    {
        var claims = new List<Claim>
        {
            new Claim("clever_user_id", cleverUserId),
            new Claim("authentication_source", "Clever")
        };

        // Determine role based on Clever user type and scope
        string role;
        string? assignedSchoolId = null;
        string? assignedDistrictId = null;

        if (cleverUserType.Equals("district_admin", StringComparison.OrdinalIgnoreCase))
        {
            role = "DistrictAdmin";
            assignedDistrictId = districtId;

            _logger.LogInformation(
                "Mapped Clever user {CleverUserId} to DistrictAdmin for district {DistrictId}",
                cleverUserId, districtId);
        }
        else if (schoolIds?.Count == 1)
        {
            role = "SchoolAdmin";
            assignedSchoolId = schoolIds[0];
            assignedDistrictId = districtId;

            // Look up school in database to get internal ID
            var school = await _dbContext.Schools
                .FirstOrDefaultAsync(s => s.CleverSchoolId == assignedSchoolId);

            if (school != null)
            {
                claims.Add(new Claim("school_id", school.SchoolId.ToString()));
            }

            _logger.LogInformation(
                "Mapped Clever user {CleverUserId} to SchoolAdmin for school {SchoolId}",
                cleverUserId, assignedSchoolId);
        }
        else
        {
            // Default to SchoolAdmin with no specific school (will need assignment)
            role = "SchoolAdmin";
            _logger.LogWarning(
                "Clever user {CleverUserId} has ambiguous scope (multiple schools or no schools). Defaulting to SchoolAdmin without assignment.",
                cleverUserId);
        }

        claims.Add(new Claim(ClaimTypes.Role, role));

        if (assignedDistrictId != null)
        {
            claims.Add(new Claim("district_id", assignedDistrictId));
        }

        return claims;
    }
}
