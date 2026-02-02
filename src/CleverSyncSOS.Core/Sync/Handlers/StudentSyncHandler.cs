using CleverSyncSOS.Core.CleverApi;
using CleverSyncSOS.Core.CleverApi.Models;
using CleverSyncSOS.Core.Database.SchoolDb.Entities;
using CleverSyncSOS.Core.Database.SessionDb.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Core.Sync.Handlers;

/// <summary>
/// Handles synchronization of Student entities from Clever API.
/// </summary>
public class StudentSyncHandler : IEntitySyncHandler<CleverStudent>, IOrphanDetectingSyncHandler
{
    private readonly ICleverApiClient _cleverClient;
    private readonly ISyncValidationService _validationService;
    private readonly ILogger<StudentSyncHandler> _logger;

    public string EntityType => "Student";

    public StudentSyncHandler(
        ICleverApiClient cleverClient,
        ISyncValidationService validationService,
        ILogger<StudentSyncHandler> logger)
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
            var cleverStudents = await _cleverClient.GetStudentsAsync(
                context.School.CleverSchoolId,
                context.LastModified,
                context.CancellationToken);

            _logger.LogDebug("Fetched {Count} students from Clever API for school {SchoolId}",
                cleverStudents.Length, context.School.SchoolId);

            int totalStudents = cleverStudents.Length;
            int percentRange = endPercent - startPercent;

            for (int i = 0; i < cleverStudents.Length; i++)
            {
                var cleverStudent = cleverStudents[i];
                try
                {
                    context.Result.StudentsProcessed++;
                    bool hasChanges = await UpsertAsync(context, cleverStudent, syncHistory.SyncId, changeTracker);
                    if (hasChanges)
                    {
                        context.Result.StudentsUpdated++;
                    }

                    if ((i + 1) % 10 == 0 || i == totalStudents - 1)
                    {
                        int currentPercent = startPercent + (percentRange * (i + 1) / totalStudents);
                        context.Progress?.Report(new SyncProgress
                        {
                            PercentComplete = currentPercent,
                            CurrentOperation = $"Processing {context.Result.StudentsProcessed}/{totalStudents} students, {context.Result.StudentsUpdated} updated",
                            StudentsUpdated = context.Result.StudentsUpdated,
                            StudentsProcessed = context.Result.StudentsProcessed,
                            StudentsFailed = context.Result.StudentsFailed
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upsert student {CleverStudentId} for school {SchoolId}",
                        cleverStudent.Id, context.School.SchoolId);
                    context.Result.StudentsFailed++;
                }
            }

            await changeTracker.SaveChangesAsync(context.CancellationToken);

            syncHistory.Status = "Success";
            syncHistory.RecordsProcessed = context.Result.StudentsProcessed;
            syncHistory.RecordsUpdated = context.Result.StudentsUpdated;
            syncHistory.RecordsFailed = context.Result.StudentsFailed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync students for school {SchoolId}", context.School.SchoolId);
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
        CleverStudent cleverStudent,
        int syncId,
        ChangeTracker changeTracker)
    {
        var student = await context.SchoolDb.Students
            .FirstOrDefaultAsync(s => s.CleverStudentId == cleverStudent.Id, context.CancellationToken);

        var now = context.TimeContext.Now;
        bool hasChanges = false;

        int? parsedGrade = _validationService.ParseGrade(cleverStudent.Grade);
        var middleName = cleverStudent.Name.Middle;
        var stateStudentId = cleverStudent.SisId ?? string.Empty;
        var gradeLevel = cleverStudent.Grade ?? "0";

        if (student == null)
        {
            student = new Student
            {
                CleverStudentId = cleverStudent.Id,
                FirstName = cleverStudent.Name.First,
                MiddleName = middleName,
                LastName = cleverStudent.Name.Last,
                Grade = parsedGrade,
                GradeLevel = gradeLevel,
                StudentNumber = cleverStudent.StudentNumber ?? string.Empty,
                StateStudentId = stateStudentId,
                CreatedAt = now,
                UpdatedAt = now,
                LastSyncedAt = now
            };
            context.SchoolDb.Students.Add(student);
            hasChanges = true;

            changeTracker.TrackStudentChange(syncId, null, student, "Created");
        }
        else
        {
            student.LastSyncedAt = now;

            var firstNameChanged = !_validationService.StringsEqual(student.FirstName, cleverStudent.Name.First);
            var middleNameChanged = !_validationService.StringsEqual(student.MiddleName, middleName);
            var lastNameChanged = !_validationService.StringsEqual(student.LastName, cleverStudent.Name.Last);
            var gradeChanged = student.Grade != parsedGrade;
            var gradeLevelChanged = !_validationService.StringsEqual(student.GradeLevel, gradeLevel);
            var studentNumberChanged = !_validationService.StringsEqual(student.StudentNumber, cleverStudent.StudentNumber);
            var stateStudentIdChanged = !_validationService.StringsEqual(student.StateStudentId, stateStudentId);
            var wasDeleted = student.DeletedAt != null;

            if (firstNameChanged || middleNameChanged || lastNameChanged || gradeChanged ||
                gradeLevelChanged || studentNumberChanged || stateStudentIdChanged || wasDeleted)
            {
                // Track grade changes for workshop sync
                if (gradeChanged && context.WorkshopTracker != null)
                {
                    context.WorkshopTracker.HasGradeChanges = true;
                    context.WorkshopTracker.StudentGradesChanged++;
                    _logger.LogDebug(
                        "Student {StudentId} ({Name}) grade changed from {OldGrade} to {NewGrade}",
                        student.StudentId, $"{student.FirstName} {student.LastName}",
                        student.Grade, parsedGrade);
                }

                var oldStudent = new Student
                {
                    CleverStudentId = student.CleverStudentId,
                    FirstName = student.FirstName,
                    MiddleName = student.MiddleName,
                    LastName = student.LastName,
                    Grade = student.Grade,
                    GradeLevel = student.GradeLevel,
                    StudentNumber = student.StudentNumber,
                    StateStudentId = student.StateStudentId
                };

                student.FirstName = cleverStudent.Name.First;
                student.MiddleName = middleName;
                student.LastName = cleverStudent.Name.Last;
                student.Grade = parsedGrade;
                student.GradeLevel = gradeLevel;
                student.StudentNumber = cleverStudent.StudentNumber ?? string.Empty;
                student.StateStudentId = stateStudentId;
                student.UpdatedAt = now;
                student.DeletedAt = null;
                hasChanges = true;

                changeTracker.TrackStudentChange(syncId, oldStudent, student, "Updated");
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
        var student = await context.SchoolDb.Students
            .FirstOrDefaultAsync(s => s.CleverStudentId == cleverId, context.CancellationToken);

        if (student == null || student.DeletedAt != null)
        {
            return false;
        }

        var now = context.TimeContext.Now;
        student.DeletedAt = now;
        student.UpdatedAt = now;

        changeTracker.TrackStudentChange(syncId, student, null, "Deleted");
        await context.SchoolDb.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation("Soft-deleted student {StudentId} ({CleverId}) via event",
            student.StudentId, cleverId);

        return true;
    }

    /// <inheritdoc />
    public async Task DetectOrphansAsync(SyncContext context, int syncId, ChangeTracker changeTracker)
    {
        var orphanedStudents = await context.SchoolDb.Students
            .Where(s => s.LastSyncedAt < context.SyncStartTime && s.DeletedAt == null)
            .ToListAsync(context.CancellationToken);

        if (orphanedStudents.Count > 0)
        {
            _logger.LogInformation("Found {Count} orphaned students to soft-delete for school {SchoolId}",
                orphanedStudents.Count, context.School.SchoolId);

            var now = context.TimeContext.Now;
            foreach (var student in orphanedStudents)
            {
                student.DeletedAt = now;
                student.UpdatedAt = now;
                changeTracker.TrackStudentChange(syncId, student, null, "Orphaned");
            }

            await context.SchoolDb.SaveChangesAsync(context.CancellationToken);
        }
    }
}
