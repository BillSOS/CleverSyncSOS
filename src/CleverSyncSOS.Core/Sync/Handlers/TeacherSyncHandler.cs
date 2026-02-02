using CleverSyncSOS.Core.CleverApi;
using CleverSyncSOS.Core.CleverApi.Models;
using CleverSyncSOS.Core.Database.SchoolDb.Entities;
using CleverSyncSOS.Core.Database.SessionDb.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Core.Sync.Handlers;

/// <summary>
/// Handles synchronization of Teacher entities from Clever API.
/// </summary>
public class TeacherSyncHandler : IEntitySyncHandler<CleverTeacher>, IOrphanDetectingSyncHandler
{
    private readonly ICleverApiClient _cleverClient;
    private readonly ISyncValidationService _validationService;
    private readonly ILogger<TeacherSyncHandler> _logger;

    public string EntityType => "Teacher";

    public TeacherSyncHandler(
        ICleverApiClient cleverClient,
        ISyncValidationService validationService,
        ILogger<TeacherSyncHandler> logger)
    {
        _cleverClient = cleverClient;
        _validationService = validationService;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> SyncAllAsync(SyncContext context, int startPercent, int endPercent)
    {
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
            var cleverTeachers = await _cleverClient.GetTeachersAsync(
                context.School.CleverSchoolId,
                context.LastModified,
                context.CancellationToken);

            _logger.LogDebug("Fetched {Count} teachers from Clever API for school {SchoolId}",
                cleverTeachers.Length, context.School.SchoolId);

            int totalTeachers = cleverTeachers.Length;
            int percentRange = endPercent - startPercent;

            for (int i = 0; i < cleverTeachers.Length; i++)
            {
                var cleverTeacher = cleverTeachers[i];
                try
                {
                    context.Result.TeachersProcessed++;
                    bool hasChanges = await UpsertAsync(context, cleverTeacher, syncHistory.SyncId, changeTracker);
                    if (hasChanges)
                    {
                        context.Result.TeachersUpdated++;
                    }

                    if ((i + 1) % 10 == 0 || i == totalTeachers - 1)
                    {
                        int currentPercent = startPercent + (percentRange * (i + 1) / totalTeachers);
                        context.Progress?.Report(new SyncProgress
                        {
                            PercentComplete = currentPercent,
                            CurrentOperation = $"Processing {context.Result.TeachersProcessed}/{totalTeachers} teachers, {context.Result.TeachersUpdated} updated",
                            TeachersUpdated = context.Result.TeachersUpdated,
                            TeachersProcessed = context.Result.TeachersProcessed,
                            TeachersFailed = context.Result.TeachersFailed
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upsert teacher {CleverTeacherId} for school {SchoolId}",
                        cleverTeacher.Id, context.School.SchoolId);
                    context.Result.TeachersFailed++;
                }
            }

            await changeTracker.SaveChangesAsync(context.CancellationToken);

            syncHistory.Status = "Success";
            syncHistory.RecordsProcessed = context.Result.TeachersProcessed;
            syncHistory.RecordsUpdated = context.Result.TeachersUpdated;
            syncHistory.RecordsFailed = context.Result.TeachersFailed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync teachers for school {SchoolId}", context.School.SchoolId);
            syncHistory.Status = "Failed";
            syncHistory.ErrorMessage = ex.Message;
            throw;
        }
        finally
        {
            syncHistory.SyncEndTime = DateTime.UtcNow;
            await context.SessionDb.SaveChangesAsync(context.CancellationToken);
        }

        return syncHistory.SyncId;
    }

    /// <inheritdoc />
    public async Task<bool> UpsertAsync(
        SyncContext context,
        CleverTeacher cleverTeacher,
        int syncId,
        ChangeTracker changeTracker)
    {
        var teacher = await context.SchoolDb.Teachers
            .FirstOrDefaultAsync(t => t.CleverTeacherId == cleverTeacher.Id, context.CancellationToken);

        var now = context.TimeContext.Now;
        bool hasChanges = false;

        var staffNumber = cleverTeacher.SisId ?? string.Empty;
        var teacherNumber = cleverTeacher.TeacherNumber;
        var userName = cleverTeacher.Roles?.Teacher?.Credentials?.DistrictUsername;
        var fullName = $"{cleverTeacher.Name.First} {cleverTeacher.Name.Last}".Trim();

        if (teacher == null)
        {
            teacher = new Teacher
            {
                CleverTeacherId = cleverTeacher.Id,
                FirstName = cleverTeacher.Name.First,
                LastName = cleverTeacher.Name.Last,
                FullName = fullName,
                StaffNumber = staffNumber,
                TeacherNumber = teacherNumber,
                UserName = userName,
                CreatedAt = now,
                UpdatedAt = now,
                LastSyncedAt = now
            };
            context.SchoolDb.Teachers.Add(teacher);
            hasChanges = true;

            changeTracker.TrackTeacherChange(syncId, null, teacher, "Created");
        }
        else
        {
            teacher.LastSyncedAt = now;

            var firstNameChanged = !_validationService.StringsEqual(teacher.FirstName, cleverTeacher.Name.First);
            var lastNameChanged = !_validationService.StringsEqual(teacher.LastName, cleverTeacher.Name.Last);
            var fullNameChanged = !_validationService.StringsEqual(teacher.FullName, fullName);
            var staffNumberChanged = !_validationService.StringsEqual(teacher.StaffNumber, staffNumber);
            var teacherNumberChanged = !_validationService.StringsEqual(teacher.TeacherNumber, teacherNumber);
            var userNameChanged = !_validationService.StringsEqual(teacher.UserName, userName);
            var wasDeleted = teacher.DeletedAt != null;

            if (firstNameChanged || lastNameChanged || fullNameChanged ||
                staffNumberChanged || teacherNumberChanged || userNameChanged || wasDeleted)
            {
                var oldTeacher = new Teacher
                {
                    CleverTeacherId = teacher.CleverTeacherId,
                    FirstName = teacher.FirstName,
                    LastName = teacher.LastName,
                    FullName = teacher.FullName,
                    StaffNumber = teacher.StaffNumber,
                    TeacherNumber = teacher.TeacherNumber,
                    UserName = teacher.UserName
                };

                teacher.FirstName = cleverTeacher.Name.First;
                teacher.LastName = cleverTeacher.Name.Last;
                teacher.FullName = fullName;
                teacher.StaffNumber = staffNumber;
                teacher.TeacherNumber = teacherNumber;
                teacher.UserName = userName;
                teacher.UpdatedAt = now;
                teacher.DeletedAt = null;
                hasChanges = true;

                changeTracker.TrackTeacherChange(syncId, oldTeacher, teacher, "Updated");
            }
        }

        if (hasChanges)
        {
            await context.SchoolDb.SaveChangesAsync(context.CancellationToken);
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
        var teacher = await context.SchoolDb.Teachers
            .FirstOrDefaultAsync(t => t.CleverTeacherId == cleverId, context.CancellationToken);

        if (teacher == null || teacher.DeletedAt != null)
        {
            return false;
        }

        var now = context.TimeContext.Now;
        teacher.DeletedAt = now;
        teacher.UpdatedAt = now;

        changeTracker.TrackTeacherChange(syncId, teacher, null, "Deleted");
        await context.SchoolDb.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation("Soft-deleted teacher {TeacherId} ({CleverId}) via event",
            teacher.TeacherId, cleverId);

        return true;
    }

    /// <inheritdoc />
    public async Task DetectOrphansAsync(SyncContext context, int syncId, ChangeTracker changeTracker)
    {
        var orphanedTeachers = await context.SchoolDb.Teachers
            .Where(t => t.LastSyncedAt < context.SyncStartTime && t.DeletedAt == null)
            .ToListAsync(context.CancellationToken);

        if (orphanedTeachers.Count > 0)
        {
            _logger.LogInformation("Found {Count} orphaned teachers to soft-delete for school {SchoolId}",
                orphanedTeachers.Count, context.School.SchoolId);

            var now = context.TimeContext.Now;
            foreach (var teacher in orphanedTeachers)
            {
                teacher.DeletedAt = now;
                teacher.UpdatedAt = now;
                changeTracker.TrackTeacherChange(syncId, teacher, null, "Orphaned");
            }

            await context.SchoolDb.SaveChangesAsync(context.CancellationToken);
        }
    }
}
