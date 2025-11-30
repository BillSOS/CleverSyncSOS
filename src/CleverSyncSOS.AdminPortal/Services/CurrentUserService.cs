using System.Security.Claims;

namespace CleverSyncSOS.AdminPortal.Services;

/// <summary>
/// Implementation of current user service using HttpContext claims
/// </summary>
public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

    public int? UserId
    {
        get
        {
            var userIdClaim = User?.FindFirst("user_id")?.Value;
            return int.TryParse(userIdClaim, out var id) ? id : null;
        }
    }

    public string? UserName => User?.Identity?.Name;

    public string? Role => User?.FindFirst(ClaimTypes.Role)?.Value;

    public int? SchoolId
    {
        get
        {
            var schoolIdClaim = User?.FindFirst("school_id")?.Value;
            return int.TryParse(schoolIdClaim, out var id) ? id : null;
        }
    }

    public string? DistrictId => User?.FindFirst("district_id")?.Value;

    public bool IsAuthenticated => User?.Identity?.IsAuthenticated ?? false;

    public bool IsSuperAdmin => User?.IsInRole("SuperAdmin") ?? false;

    public bool IsDistrictAdmin =>
        (User?.IsInRole("DistrictAdmin") ?? false) ||
        (User?.IsInRole("SuperAdmin") ?? false);

    public bool IsSchoolAdmin =>
        (User?.IsInRole("SchoolAdmin") ?? false) ||
        (User?.IsInRole("DistrictAdmin") ?? false) ||
        (User?.IsInRole("SuperAdmin") ?? false);
}
