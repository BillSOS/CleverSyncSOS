using CleverSyncSOS.Core.CleverApi;
using CleverSyncSOS.Core.CleverApi.Models;
using CleverSyncSOS.Core.Database.SchoolDb.Entities;
using CleverSyncSOS.Core.Database.SessionDb.Entities;
using CleverSyncSOS.Core.Sync.Workshop;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Core.Sync.Handlers;

/// <summary>
/// Handles synchronization of Section entities from Clever API,
/// including teacher and student enrollment associations.
/// </summary>
public class SectionSyncHandler : IEntitySyncHandler<CleverSection>, IOrphanDetectingSyncHandler
{
    private readonly ICleverApiClient _cleverClient;
    private readonly ISyncValidationService _validationService;
    private readonly IWorkshopSyncService _workshopSyncService;
    private readonly ILogger<SectionSyncHandler> _logger;

    public string EntityType => "Section";

    public SectionSyncHandler(
        ICleverApiClient cleverClient,
        ISyncValidationService validationService,
        IWorkshopSyncService workshopSyncService,
        ILogger<SectionSyncHandler> logger)
    {
        _cleverClient = cleverClient;
        _validationService = validationService;
        _workshopSyncService = workshopSyncService;
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
            RecordsProcessed = 0
        };

        context.SessionDb.SyncHistory.Add(syncHistory);
        await context.SessionDb.SaveChangesAsync(context.CancellationToken);

        var changeTracker = new ChangeTracker(context.SessionDb, _logger);

        try
        {
            var cleverSections = await _cleverClient.GetSectionsAsync(
                context.School.CleverSchoolId,
                context.CancellationToken);

            _logger.LogDebug("Fetched {Count} sections from Clever API for school {SchoolId}",
                cleverSections.Length, context.School.SchoolId);

            // Pre-load workshop-linked sections
            var workshopLinkedSectionIds = await _workshopSyncService.GetWorkshopLinkedSectionIdsAsync(
                context.SchoolDb, context.CancellationToken);
            _logger.LogDebug("Found {Count} sections linked to workshops", workshopLinkedSectionIds.Count);

            int totalSections = cleverSections.Length;
            int percentRange = endPercent - startPercent;

            var cleverSectionIds = new HashSet<string>(cleverSections.Select(s => s.Id));

            for (int i = 0; i < cleverSections.Length; i++)
            {
                var cleverSection = cleverSections[i];
                try
                {
                    context.Result.SectionsProcessed++;

                    var existingSection = await context.SchoolDb.Sections
                        .FirstOrDefaultAsync(s => s.CleverSectionId == cleverSection.Id, context.CancellationToken);

                    if (existingSection == null)
                    {
                        // New section
                        var newSection = new Section
                        {
                            CleverSectionId = cleverSection.Id,
                            SectionName = cleverSection.Name,
                            Period = cleverSection.Period,
                            Subject = cleverSection.Subject,
                            TermId = cleverSection.TermId,
                            CreatedAt = context.TimeContext.Now,
                            UpdatedAt = context.TimeContext.Now,
                            LastEventReceivedAt = context.TimeContext.Now
                        };
                        context.SchoolDb.Sections.Add(newSection);
                        await context.SchoolDb.SaveChangesAsync(context.CancellationToken);
                        changeTracker.TrackSectionChange(syncHistory.SyncId, null, newSection, "Created");
                        context.Result.SectionsUpdated++;

                        await SyncSectionTeachersAsync(context, newSection, cleverSection.Teachers, cleverSection.Teacher);
                        await SyncSectionStudentsAsync(context, newSection, cleverSection.Students, workshopLinkedSectionIds);
                    }
                    else
                    {
                        bool hasChanges = await UpsertAsync(context, cleverSection, syncHistory.SyncId, changeTracker);
                        if (hasChanges)
                        {
                            context.Result.SectionsUpdated++;
                        }

                        await SyncSectionTeachersAsync(context, existingSection, cleverSection.Teachers, cleverSection.Teacher);
                        await SyncSectionStudentsAsync(context, existingSection, cleverSection.Students, workshopLinkedSectionIds);
                    }

                    if ((i + 1) % 50 == 0 || i == totalSections - 1)
                    {
                        int currentPercent = startPercent + (percentRange * (i + 1) / totalSections);
                        context.Progress?.Report(new SyncProgress
                        {
                            PercentComplete = currentPercent,
                            CurrentOperation = $"Processing {context.Result.SectionsProcessed}/{totalSections} sections, {context.Result.SectionsUpdated} updated",
                            WarningsGenerated = context.Result.WarningsGenerated
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upsert section {CleverSectionId} for school {SchoolId}",
                        cleverSection.Id, context.School.SchoolId);
                    context.Result.SectionsFailed++;
                }
            }

            // Check for deleted sections
            await CheckForDeletedSectionsAsync(context, cleverSectionIds, workshopLinkedSectionIds, syncHistory.SyncId);

            await changeTracker.SaveChangesAsync(context.CancellationToken);
            await context.SchoolDb.SaveChangesAsync(context.CancellationToken);

            syncHistory.Status = "Success";
            syncHistory.RecordsProcessed = context.Result.SectionsProcessed;
            syncHistory.RecordsUpdated = context.Result.SectionsUpdated;
            syncHistory.RecordsFailed = context.Result.SectionsFailed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to sync sections for school {SchoolId}", context.School.SchoolId);
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
        CleverSection cleverSection,
        int syncId,
        ChangeTracker changeTracker)
    {
        var existingSection = await context.SchoolDb.Sections
            .FirstOrDefaultAsync(s => s.CleverSectionId == cleverSection.Id, context.CancellationToken);

        if (existingSection == null)
        {
            return false;
        }

        var now = context.TimeContext.Now;
        bool hasChanges = false;

        var sectionNameChanged = !_validationService.StringsEqual(existingSection.SectionName, cleverSection.Name);
        var periodChanged = !_validationService.StringsEqual(existingSection.Period, cleverSection.Period);
        var subjectChanged = !_validationService.StringsEqual(existingSection.Subject, cleverSection.Subject);
        var termIdChanged = !_validationService.StringsEqual(existingSection.TermId, cleverSection.TermId);
        var wasDeleted = existingSection.DeletedAt != null;

        bool isWorkshopLinked = context.WorkshopLinkedSectionIds.Contains(existingSection.SectionId);

        if (sectionNameChanged || periodChanged || subjectChanged || termIdChanged || wasDeleted)
        {
            hasChanges = true;

            if (isWorkshopLinked && sectionNameChanged)
            {
                await GenerateWorkshopWarningAsync(
                    context, existingSection, syncId, "SectionModified",
                    $"Section '{existingSection.SectionName}' (ID: {existingSection.SectionId}) linked to workshops has been modified. " +
                    $"Name changed to: '{cleverSection.Name}'");
            }

            var oldSection = new Section
            {
                SectionId = existingSection.SectionId,
                CleverSectionId = existingSection.CleverSectionId,
                SectionName = existingSection.SectionName,
                Period = existingSection.Period,
                Subject = existingSection.Subject,
                TermId = existingSection.TermId
            };

            existingSection.SectionName = cleverSection.Name;
            existingSection.Period = cleverSection.Period;
            existingSection.Subject = cleverSection.Subject;
            existingSection.TermId = cleverSection.TermId;
            existingSection.DeletedAt = null;
            existingSection.UpdatedAt = now;
            existingSection.LastEventReceivedAt = now;

            changeTracker.TrackSectionChange(syncId, oldSection, existingSection, "Updated");
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
        var section = await context.SchoolDb.Sections
            .FirstOrDefaultAsync(s => s.CleverSectionId == cleverId, context.CancellationToken);

        if (section == null || section.DeletedAt != null)
        {
            return false;
        }

        // Check if workshop-linked
        if (context.WorkshopLinkedSectionIds.Contains(section.SectionId))
        {
            await GenerateWorkshopWarningAsync(
                context, section, syncId, "SectionDeleted",
                $"Section '{section.SectionName}' (ID: {section.SectionId}) is linked to workshops but received delete event. " +
                "The section was NOT deactivated. Manual review required.");
            return false;
        }

        var now = context.TimeContext.Now;
        section.DeletedAt = now;
        section.UpdatedAt = now;

        changeTracker.TrackSectionChange(syncId, section, null, "Deleted");
        await context.SchoolDb.SaveChangesAsync(context.CancellationToken);

        _logger.LogInformation("Soft-deleted section {SectionId} ({CleverId}) via event",
            section.SectionId, cleverId);

        return true;
    }

    /// <inheritdoc />
    public async Task DetectOrphansAsync(SyncContext context, int syncId, ChangeTracker changeTracker)
    {
        // Handled in CheckForDeletedSectionsAsync during SyncAllAsync
    }

    /// <summary>
    /// Checks for sections that exist in the database but are no longer in Clever.
    /// </summary>
    private async Task CheckForDeletedSectionsAsync(
        SyncContext context,
        HashSet<string> cleverSectionIds,
        HashSet<int> workshopLinkedSectionIds,
        int syncId)
    {
        var sectionsInDb = await context.SchoolDb.Sections
            .Where(s => s.DeletedAt == null)
            .Select(s => new { s.SectionId, s.CleverSectionId, s.SectionName })
            .ToListAsync(context.CancellationToken);

        var missingSections = sectionsInDb
            .Where(s => !cleverSectionIds.Contains(s.CleverSectionId))
            .ToList();

        var now = context.TimeContext.Now;
        foreach (var missingSection in missingSections)
        {
            if (workshopLinkedSectionIds.Contains(missingSection.SectionId))
            {
                var section = await context.SchoolDb.Sections.FindAsync(
                    new object[] { missingSection.SectionId }, context.CancellationToken);
                if (section != null)
                {
                    await GenerateWorkshopWarningAsync(
                        context, section, syncId, "SectionDeleted",
                        $"Section '{missingSection.SectionName}' (ID: {missingSection.SectionId}) is linked to workshops but no longer exists in Clever. " +
                        "The section was NOT deactivated. Manual review required.");

                    context.Result.SectionsSkippedWorkshopLinked++;
                    _logger.LogWarning(
                        "Section {SectionId} ({SectionName}) is linked to workshops but missing from Clever. Skipping deactivation.",
                        missingSection.SectionId, missingSection.SectionName);
                }
            }
            else
            {
                var section = await context.SchoolDb.Sections.FindAsync(
                    new object[] { missingSection.SectionId }, context.CancellationToken);
                if (section != null)
                {
                    section.DeletedAt = now;
                    section.UpdatedAt = now;
                    _logger.LogInformation(
                        "Soft-deleting section {SectionId} ({SectionName}) - no longer in Clever",
                        missingSection.SectionId, missingSection.SectionName);
                }
            }
        }

        await context.SchoolDb.SaveChangesAsync(context.CancellationToken);
    }

    /// <summary>
    /// Syncs teacher-section associations for a given section.
    /// </summary>
    public async Task SyncSectionTeachersAsync(
        SyncContext context,
        Section section,
        string[] cleverTeacherIds,
        string? primaryTeacherId)
    {
        var existingAssociations = await context.SchoolDb.TeacherSections
            .Where(ts => ts.SectionId == section.SectionId)
            .ToListAsync(context.CancellationToken);
        context.SchoolDb.TeacherSections.RemoveRange(existingAssociations);

        var now = context.TimeContext.Now;
        foreach (var cleverTeacherId in cleverTeacherIds ?? Array.Empty<string>())
        {
            var teacher = await context.SchoolDb.Teachers
                .FirstOrDefaultAsync(t => t.CleverTeacherId == cleverTeacherId, context.CancellationToken);

            if (teacher != null)
            {
                var isPrimary = cleverTeacherId == primaryTeacherId;
                var association = new TeacherSection
                {
                    TeacherId = teacher.TeacherId,
                    SectionId = section.SectionId,
                    IsPrimary = isPrimary,
                    CreatedAt = now
                };
                context.SchoolDb.TeacherSections.Add(association);
            }
            else
            {
                _logger.LogWarning("Teacher {CleverTeacherId} not found for section {SectionId}",
                    cleverTeacherId, section.CleverSectionId);
            }
        }

        await context.SchoolDb.SaveChangesAsync(context.CancellationToken);
    }

    /// <summary>
    /// Syncs student-section enrollments for a given section.
    /// </summary>
    public async Task SyncSectionStudentsAsync(
        SyncContext context,
        Section section,
        string[] cleverStudentIds,
        HashSet<int> workshopLinkedSectionIds)
    {
        var now = context.TimeContext.Now;
        var incomingStudentIds = cleverStudentIds ?? Array.Empty<string>();
        var isWorkshopLinkedSection = workshopLinkedSectionIds.Contains(section.SectionId);

        var existingEnrollments = await context.SchoolDb.StudentSections
            .Where(ss => ss.SectionId == section.SectionId)
            .Include(ss => ss.Student)
            .ToListAsync(context.CancellationToken);

        var existingByCleverStudentId = existingEnrollments
            .Where(e => e.Student != null)
            .ToDictionary(e => e.Student.CleverStudentId, e => e);

        var enrollmentsToKeep = new HashSet<int>();
        int studentsAdded = 0;

        foreach (var cleverStudentId in incomingStudentIds)
        {
            var student = await context.SchoolDb.Students
                .FirstOrDefaultAsync(s => s.CleverStudentId == cleverStudentId, context.CancellationToken);

            if (student != null)
            {
                if (existingByCleverStudentId.TryGetValue(cleverStudentId, out var existingEnrollment))
                {
                    enrollmentsToKeep.Add(existingEnrollment.StudentSectionId);
                }
                else
                {
                    var enrollment = new StudentSection
                    {
                        StudentId = student.StudentId,
                        SectionId = section.SectionId,
                        OffCampus = false,
                        CreatedAt = now
                    };
                    context.SchoolDb.StudentSections.Add(enrollment);
                    studentsAdded++;
                }
            }
            else
            {
                _logger.LogWarning("Student {CleverStudentId} not found for section {SectionId}",
                    cleverStudentId, section.CleverSectionId);
            }
        }

        var enrollmentsToRemove = existingEnrollments
            .Where(e => !enrollmentsToKeep.Contains(e.StudentSectionId))
            .ToList();
        int studentsRemoved = enrollmentsToRemove.Count;
        context.SchoolDb.StudentSections.RemoveRange(enrollmentsToRemove);

        await context.SchoolDb.SaveChangesAsync(context.CancellationToken);

        if (isWorkshopLinkedSection && context.WorkshopTracker != null && (studentsAdded > 0 || studentsRemoved > 0))
        {
            context.WorkshopTracker.HasWorkshopEnrollmentChanges = true;
            context.WorkshopTracker.StudentsAddedToWorkshopSections += studentsAdded;
            context.WorkshopTracker.StudentsRemovedFromWorkshopSections += studentsRemoved;

            _logger.LogDebug(
                "Workshop-linked section {SectionId} ({SectionName}): {Added} students added, {Removed} students removed",
                section.SectionId, section.SectionName, studentsAdded, studentsRemoved);
        }
    }

    /// <summary>
    /// Generates a warning for workshop-linked sections that are being modified or deleted.
    /// </summary>
    private async Task GenerateWorkshopWarningAsync(
        SyncContext context,
        Section section,
        int syncId,
        string warningType,
        string message)
    {
        var affectedWorkshops = await context.SchoolDb.WorkshopSections
            .Where(ws => ws.SectionId == section.SectionId)
            .Include(ws => ws.Workshop)
            .Select(ws => new { ws.Workshop!.WorkshopId, ws.Workshop.WorkshopName })
            .ToListAsync(context.CancellationToken);

        var workshopNames = affectedWorkshops.Select(w => w.WorkshopName).ToList();
        var workshopJson = System.Text.Json.JsonSerializer.Serialize(
            affectedWorkshops.Select(w => new { w.WorkshopId, w.WorkshopName }));

        var warning = new SyncWarning
        {
            SyncId = syncId,
            WarningType = warningType,
            EntityType = "Section",
            EntityId = section.SectionId,
            CleverEntityId = section.CleverSectionId,
            EntityName = section.SectionName ?? $"Section {section.CleverSectionId}",
            Message = message,
            AffectedWorkshops = workshopJson,
            AffectedWorkshopCount = affectedWorkshops.Count,
            IsAcknowledged = false,
            CreatedAt = DateTime.UtcNow
        };

        context.SessionDb.SyncWarnings.Add(warning);
        await context.SessionDb.SaveChangesAsync(context.CancellationToken);

        var sectionDisplayName = section.SectionName ?? $"Section {section.CleverSectionId}";
        context.Result.WarningsGenerated++;
        context.Result.Warnings.Add(new SyncWarningInfo
        {
            WarningType = warningType,
            EntityType = "Section",
            EntityId = section.SectionId,
            EntityName = sectionDisplayName,
            Message = message,
            AffectedWorkshopNames = workshopNames
        });

        _logger.LogWarning(
            "SYNC WARNING [{WarningType}]: Section {SectionId} ({SectionName}) - {Message}. Affects {Count} workshop(s): {Workshops}",
            warningType, section.SectionId, sectionDisplayName, message, affectedWorkshops.Count,
            string.Join(", ", workshopNames));
    }
}
