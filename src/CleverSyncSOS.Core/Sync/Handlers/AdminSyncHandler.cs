using CleverSyncSOS.Core.CleverApi;
using CleverSyncSOS.Core.CleverApi.Models;
using CleverSyncSOS.Core.Database.SessionDb.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Core.Sync.Handlers;

/// <summary>
/// Handles synchronization of Administrator users from Clever API to the User table in SessionDb.
/// </summary>
/// <remarks>
/// <para><b>Purpose:</b> Syncs school administrators from Clever to enable portal authentication.</para>
/// <para><b>Scope:</b> Only syncs users with role=school_admin (per-school). District admins require
/// different API patterns and are not included in this handler.</para>
/// <para><b>Safety:</b> Users with AuthenticationSource="Bypass" are never touched by this sync.</para>
/// </remarks>
public class AdminSyncHandler : IEntitySyncHandler<CleverAdministrator>, IOrphanDetectingSyncHandler
{
    private readonly ICleverApiClient _cleverClient;
    private readonly ISyncValidationService _validationService;
    private readonly ILogger<AdminSyncHandler> _logger;

    public string EntityType => "Admin";

    public AdminSyncHandler(
        ICleverApiClient cleverClient,
        ISyncValidationService validationService,
        ILogger<AdminSyncHandler> logger)
    {
        _cleverClient = cleverClient;
        _validationService = validationService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> SyncAllAsync(SyncContext context, int startPercent, int endPercent)
    {
        // Create SyncHistory record for admin sync
        var syncHistory = new SyncHistory
        {
            SchoolId = context.School.SchoolId,
            EntityType = EntityType,
            SyncType = context.Result.SyncType,
            SyncStartTime = DateTime.UtcNow,
            Status = "InProgress",
            LastSyncTimestamp = context.LastModified
        };

        context.SessionDb.SyncHistory.Add(syncHistory);
        await context.SessionDb.SaveChangesAsync(context.CancellationToken);

        var changeTracker = new ChangeTracker(context.SessionDb, _logger);

        try
        {
            _logger.LogInformation("Fetching school administrators from Clever API for school {SchoolId} (CleverSchoolId: {CleverSchoolId})",
                context.School.SchoolId, context.School.CleverSchoolId);

            var cleverAdmins = await _cleverClient.GetSchoolAdminsAsync(
                context.School.CleverSchoolId,
                context.CancellationToken);

            _logger.LogInformation("Fetched {Count} school administrators from Clever API for school {SchoolId}",
                cleverAdmins.Length, context.School.SchoolId);

            int totalAdmins = cleverAdmins.Length;
            int percentRange = endPercent - startPercent;

            for (int i = 0; i < cleverAdmins.Length; i++)
            {
                var cleverAdmin = cleverAdmins[i];
                try
                {
                    context.Result.AdminsProcessed++;
                    bool hasChanges = await UpsertAsync(context, cleverAdmin, syncHistory.SyncId, changeTracker);
                    if (hasChanges)
                    {
                        context.Result.AdminsUpdated++;
                    }

                    if ((i + 1) % 10 == 0 || i == totalAdmins - 1)
                    {
                        int currentPercent = startPercent + (percentRange * (i + 1) / Math.Max(totalAdmins, 1));
                        context.Progress?.Report(new SyncProgress
                        {
                            PercentComplete = currentPercent,
                            CurrentOperation = $"Processing {context.Result.AdminsProcessed}/{totalAdmins} administrators, {context.Result.AdminsUpdated} updated",
                            AdminsUpdated = context.Result.AdminsUpdated,
                            AdminsProcessed = context.Result.AdminsProcessed,
                            AdminsFailed = context.Result.AdminsFailed
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upsert administrator {CleverAdminId} for school {SchoolId}",
                        cleverAdmin.Id, context.School.SchoolId);
                    context.Result.AdminsFailed++;
                }
            }

            await changeTracker.SaveChangesAsync(context.CancellationToken);

            // Update sync history with success
            syncHistory.Status = "Success";
            syncHistory.RecordsProcessed = context.Result.AdminsProcessed;
            syncHistory.RecordsUpdated = context.Result.AdminsUpdated;
            syncHistory.RecordsFailed = context.Result.AdminsFailed;
            syncHistory.SyncEndTime = DateTime.UtcNow;

            _logger.LogInformation("Admin sync complete for school {SchoolId}: {Processed} processed, {Updated} updated, {Failed} failed",
                context.School.SchoolId, context.Result.AdminsProcessed, context.Result.AdminsUpdated, context.Result.AdminsFailed);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Admin sync failed for school {SchoolId}", context.School.SchoolId);
            syncHistory.Status = "Failed";
            syncHistory.ErrorMessage = ex.Message;
            syncHistory.SyncEndTime = DateTime.UtcNow;
            throw; // Re-throw so SyncService can handle it as non-fatal
        }
        finally
        {
            await context.SessionDb.SaveChangesAsync(context.CancellationToken);
        }

        return syncHistory.SyncId;
    }

    /// <inheritdoc />
    public async Task<bool> UpsertAsync(
        SyncContext context,
        CleverAdministrator cleverAdmin,
        int syncId,
        ChangeTracker changeTracker)
    {
        // Only process Clever-sourced users; never touch Bypass users
        var user = await context.SessionDb.Users
            .FirstOrDefaultAsync(u => u.CleverUserId == cleverAdmin.Id, context.CancellationToken);

        var now = DateTime.UtcNow;
        bool hasChanges = false;

        var displayName = $"{cleverAdmin.Name.First} {cleverAdmin.Name.Last}".Trim();
        var email = cleverAdmin.Email ?? string.Empty;
        var role = cleverAdmin.IsSchoolAdmin ? "SchoolAdmin" : "DistrictAdmin";

        if (user == null)
        {
            // Check if there's an existing user with this email that uses Bypass auth - don't overwrite
            var existingBypassUser = await context.SessionDb.Users
                .FirstOrDefaultAsync(u => u.Email == email && u.AuthenticationSource == "Bypass", context.CancellationToken);

            if (existingBypassUser != null)
            {
                _logger.LogWarning(
                    "Skipping Clever admin {CleverAdminId} ({Email}) - existing Bypass user with same email",
                    cleverAdmin.Id, email);
                return false;
            }

            // Create new Clever-sourced user
            user = new User
            {
                CleverUserId = cleverAdmin.Id,
                Email = email,
                DisplayName = displayName,
                Role = role,
                AuthenticationSource = "Clever",
                SchoolId = context.School.SchoolId,
                DistrictId = context.School.DistrictId, // Clever district ID (string)
                IsActive = true,
                CreatedAt = now,
                UpdatedAt = now,
                CreatedBy = "CleverSync"
            };
            context.SessionDb.Users.Add(user);
            hasChanges = true;

            changeTracker.TrackUserChange(syncId, null, user, "Created");
            _logger.LogInformation("Created new admin user: {Email} ({Role}) for school {SchoolId}",
                email, role, context.School.SchoolId);
        }
        else
        {
            // Only update if this is a Clever-sourced user
            if (user.AuthenticationSource != "Clever")
            {
                _logger.LogWarning(
                    "Skipping update for user {UserId} ({Email}) - AuthenticationSource is {Source}, not Clever",
                    user.UserId, user.Email, user.AuthenticationSource);
                return false;
            }

            var displayNameChanged = !_validationService.StringsEqual(user.DisplayName, displayName);
            var emailChanged = !_validationService.StringsEqual(user.Email, email);
            var roleChanged = !_validationService.StringsEqual(user.Role, role);
            var wasInactive = !user.IsActive;

            if (displayNameChanged || emailChanged || roleChanged || wasInactive)
            {
                var oldUser = new User
                {
                    CleverUserId = user.CleverUserId,
                    DisplayName = user.DisplayName,
                    Email = user.Email,
                    Role = user.Role,
                    IsActive = user.IsActive
                };

                user.DisplayName = displayName;
                user.Email = email;
                user.Role = role;
                user.IsActive = true;
                user.UpdatedAt = now;
                // Preserve MaxConcurrentSessions - don't overwrite user overrides
                hasChanges = true;

                changeTracker.TrackUserChange(syncId, oldUser, user, "Updated");
                _logger.LogInformation("Updated admin user: {Email} ({Role}) for school {SchoolId}",
                    email, role, context.School.SchoolId);
            }
        }

        if (hasChanges)
        {
            await context.SessionDb.SaveChangesAsync(context.CancellationToken);
        }

        return hasChanges;
    }

    /// <inheritdoc />
    public async Task<bool> HandleDeleteAsync(
        SyncContext context,
        string cleverId,
        int syncId,
        ChangeTracker changeTracker)
    {
        var user = await context.SessionDb.Users
            .FirstOrDefaultAsync(u => u.CleverUserId == cleverId && u.AuthenticationSource == "Clever", context.CancellationToken);

        if (user == null || !user.IsActive)
        {
            return false;
        }

        user.IsActive = false;
        user.UpdatedAt = DateTime.UtcNow;

        changeTracker.TrackUserChange(syncId, user, user, "Deleted");
        await context.SessionDb.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation("Deactivated admin user {UserId} ({CleverId}) via delete event",
            user.UserId, cleverId);

        return true;
    }

    /// <inheritdoc />
    public async Task DetectOrphansAsync(SyncContext context, int syncId, ChangeTracker changeTracker)
    {
        // Find Clever-sourced admin users for this school that weren't updated during sync
        // These are admins that were removed from Clever
        var orphanedAdmins = await context.SessionDb.Users
            .Where(u => u.SchoolId == context.School.SchoolId
                && u.AuthenticationSource == "Clever"
                && u.IsActive
                && (u.UpdatedAt == null || u.UpdatedAt < context.SyncStartTime))
            .ToListAsync(context.CancellationToken);

        if (orphanedAdmins.Count > 0)
        {
            _logger.LogInformation("Found {Count} orphaned admin users to deactivate for school {SchoolId}",
                orphanedAdmins.Count, context.School.SchoolId);

            var now = DateTime.UtcNow;
            foreach (var user in orphanedAdmins)
            {
                user.IsActive = false;
                user.UpdatedAt = now;
                changeTracker.TrackUserChange(syncId, user, user, "Orphaned");
            }

            await context.SessionDb.SaveChangesAsync(context.CancellationToken);
        }
    }
}
