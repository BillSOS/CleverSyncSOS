using CleverSyncSOS.AdminPortal.Models.ViewModels;
using CleverSyncSOS.Core.Database.SessionDb;
using Microsoft.EntityFrameworkCore;

namespace CleverSyncSOS.AdminPortal.Services;

/// <summary>
/// Implementation of ISchoolScopeService for role-based scope filtering.
/// Based on manual-sync-feature-plan.md
/// </summary>
public class SchoolScopeService : ISchoolScopeService
{
    private readonly SessionDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public SchoolScopeService(SessionDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<List<SyncScopeOption>> GetAvailableScopesAsync(int userId)
    {
        var user = await _dbContext.Users
            .Include(u => u.School)
            .Include(u => u.District)
            .FirstOrDefaultAsync(u => u.UserId == userId);

        // If user is not found in database (e.g., Super Admin bypass login),
        // use the current user's role from claims
        if (user == null)
        {
            var role = _currentUserService.Role;
            if (role == "SuperAdmin")
            {
                // Handle Super Admin bypass login (no database user)
                return await GetSuperAdminScopesAsync();
            }
            return new();
        }

        var options = new List<SyncScopeOption>();

        switch (user.Role)
        {
            case "SchoolAdmin":
                // Only their assigned school (disabled dropdown)
                if (user.SchoolId.HasValue && user.School != null)
                {
                    options.Add(new SyncScopeOption
                    {
                        Value = $"school:{user.SchoolId}",
                        DisplayText = user.School.Name,
                        Icon = "bi-building",
                        IsDisabled = true, // Pre-selected and locked
                        EntityId = user.SchoolId,
                        ScopeType = SyncScopeType.School
                    });
                }
                break;

            case "DistrictAdmin":
                // "All Schools in District" option
                if (!string.IsNullOrEmpty(user.DistrictId) && user.District != null)
                {
                    options.Add(new SyncScopeOption
                    {
                        Value = $"district:{user.DistrictId}",
                        DisplayText = $"All Schools in {user.District.Name}",
                        Icon = "bi-buildings",
                        EntityId = null, // District ID is Clever ID (string), not database ID
                        ScopeType = SyncScopeType.District
                    });

                    // Individual schools in their district
                    var schools = await _dbContext.Schools
                        .Where(s => s.DistrictId == user.DistrictId && s.IsActive)
                        .OrderBy(s => s.Name)
                        .ToListAsync();

                    foreach (var school in schools)
                    {
                        options.Add(new SyncScopeOption
                        {
                            Value = $"school:{school.SchoolId}",
                            DisplayText = school.Name,
                            Icon = "bi-building",
                            EntityId = school.SchoolId,
                            ScopeType = SyncScopeType.School
                        });
                    }
                }
                break;

            case "SuperAdmin":
                return await GetSuperAdminScopesAsync();
        }

        return options;
    }

    /// <summary>
    /// Gets all available scopes for Super Admin (bypass login or database user).
    /// </summary>
    private async Task<List<SyncScopeOption>> GetSuperAdminScopesAsync()
    {
        var options = new List<SyncScopeOption>();

        // All districts option
        options.Add(new SyncScopeOption
        {
            Value = "all",
            DisplayText = "All Districts and Schools",
            Icon = "bi-globe",
            ScopeType = SyncScopeType.AllDistricts
        });

        // Per-district "all schools" and individual schools
        var districts = await _dbContext.Districts
            .Include(d => d.Schools.Where(s => s.IsActive))
            .OrderBy(d => d.Name)
            .ToListAsync();

        foreach (var district in districts)
        {
            options.Add(new SyncScopeOption
            {
                Value = $"district:{district.CleverDistrictId}",
                DisplayText = $"All Schools in {district.Name}",
                Icon = "bi-buildings",
                EntityId = null, // District ID is Clever ID (string), not database ID
                ScopeType = SyncScopeType.District
            });

            foreach (var school in district.Schools.OrderBy(s => s.Name))
            {
                options.Add(new SyncScopeOption
                {
                    Value = $"school:{school.SchoolId}",
                    DisplayText = $"  {school.Name}", // Indent for hierarchy
                    Icon = "bi-building",
                    EntityId = school.SchoolId,
                    ScopeType = SyncScopeType.School
                });
            }
        }

        return options;
    }

    public async Task<bool> ValidateScopeAccessAsync(int userId, string scope)
    {
        var user = await _dbContext.Users.FindAsync(userId);

        // Handle bypass login (no database user)
        if (user == null)
        {
            var role = _currentUserService.Role;
            // Super Admin bypass login can access all scopes
            return role == "SuperAdmin";
        }

        if (!user.IsActive) return false;

        // SuperAdmin can access all scopes
        if (user.Role == "SuperAdmin") return true;

        // Parse scope
        var parts = scope.Split(':');
        var scopeType = parts[0]; // "school", "district", or "all"
        var entityId = parts.Length > 1 ? int.Parse(parts[1]) : 0;

        switch (user.Role)
        {
            case "SchoolAdmin":
                // Can only sync their assigned school
                return scopeType == "school" && entityId == user.SchoolId;

            case "DistrictAdmin":
                if (scopeType == "all") return false; // Cannot sync all districts

                if (scopeType == "district")
                {
                    // Can sync their assigned district (DistrictId is Clever ID string, scope format is district:{cleverId})
                    var districtCleverIdFromScope = parts.Length > 1 ? parts[1] : string.Empty;
                    return districtCleverIdFromScope == user.DistrictId;
                }

                if (scopeType == "school")
                {
                    // Can sync schools in their district
                    var school = await _dbContext.Schools.FindAsync(entityId);
                    return school?.DistrictId == user.DistrictId;
                }
                break;
        }

        return false;
    }
}
