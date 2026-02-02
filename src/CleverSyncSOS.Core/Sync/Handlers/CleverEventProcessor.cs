using System.Text.Json;
using CleverSyncSOS.Core.CleverApi.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CleverSyncSOS.Core.Sync.Handlers;

/// <summary>
/// Processes Clever events from the Events API and routes them to the appropriate entity handlers.
/// </summary>
public class CleverEventProcessor
{
    private readonly StudentSyncHandler _studentHandler;
    private readonly TeacherSyncHandler _teacherHandler;
    private readonly SectionSyncHandler _sectionHandler;
    private readonly TermSyncHandler _termHandler;
    private readonly ILogger<CleverEventProcessor> _logger;

    public CleverEventProcessor(
        StudentSyncHandler studentHandler,
        TeacherSyncHandler teacherHandler,
        SectionSyncHandler sectionHandler,
        TermSyncHandler termHandler,
        ILogger<CleverEventProcessor> logger)
    {
        _studentHandler = studentHandler;
        _teacherHandler = teacherHandler;
        _sectionHandler = sectionHandler;
        _termHandler = termHandler;
        _logger = logger;
    }

    /// <summary>
    /// Processes a batch of Clever events.
    /// </summary>
    public async Task ProcessEventsAsync(
        SyncContext context,
        IReadOnlyList<CleverEvent> events,
        int syncId,
        ChangeTracker changeTracker)
    {
        foreach (var evt in events)
        {
            try
            {
                await ProcessEventAsync(context, evt, syncId, changeTracker);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing event {EventId}", evt.Id);
            }
        }
    }

    /// <summary>
    /// Processes a single event from Clever's Events API.
    /// </summary>
    public async Task ProcessEventAsync(
        SyncContext context,
        CleverEvent evt,
        int syncId,
        ChangeTracker changeTracker)
    {
        var objectType = evt.Data.Object;
        if (string.IsNullOrEmpty(objectType))
        {
            objectType = evt.ObjectType?.TrimEnd('s') ?? string.Empty;
        }

        var eventType = evt.ActionType;
        var eventsSummary = context.Result.EventsSummary;

        if (eventsSummary != null)
        {
            eventsSummary.TotalEventsProcessed++;
        }

        _logger.LogInformation("Processing event {EventId}: Type={EventType}, ObjectType={ObjectType}, ObjectId={ObjectId}",
            evt.Id, evt.Type, objectType, evt.Data.Id);

        var hasRawData = evt.Data.RawData != null && evt.Data.RawData.Value.ValueKind != JsonValueKind.Undefined;

        if (!hasRawData)
        {
            _logger.LogWarning("Event {EventId} has no data. Skipping.", evt.Id);
            return;
        }

        var dataElement = evt.Data.RawData.Value;
        var rawDataJson = dataElement.GetRawText();

        if (dataElement.ValueKind != JsonValueKind.Object)
        {
            _logger.LogWarning("Event {EventId} has invalid data structure. Skipping.", evt.Id);
            return;
        }

        // Skip district and course events
        if (objectType == "course" || objectType == "district")
        {
            _logger.LogDebug("Event {EventId} is a {ObjectType} event - skipping", evt.Id, objectType);
            if (eventsSummary != null) eventsSummary.EventsSkipped++;
            return;
        }

        // Handle user events (students/teachers)
        if (objectType == "user")
        {
            await ProcessUserEventAsync(context, evt, dataElement, rawDataJson, eventType, syncId, changeTracker);
            return;
        }

        // Handle section events
        if (objectType == "section")
        {
            await ProcessSectionEventAsync(context, evt, rawDataJson, eventType, syncId, changeTracker);
            return;
        }

        // Handle term events
        if (objectType == "term")
        {
            await ProcessTermEventAsync(context, evt, rawDataJson, eventType, syncId, changeTracker);
            return;
        }

        _logger.LogWarning("Unknown object type: {ObjectType} for event {EventId}", objectType, evt.Id);
        if (eventsSummary != null) eventsSummary.EventsSkipped++;
    }

    private async Task ProcessUserEventAsync(
        SyncContext context,
        CleverEvent evt,
        JsonElement dataElement,
        string rawDataJson,
        string eventType,
        int syncId,
        ChangeTracker changeTracker)
    {
        var eventsSummary = context.Result.EventsSummary;
        string? role = null;

        // Determine role from roles object
        if (dataElement.TryGetProperty("roles", out var rolesElement))
        {
            if (rolesElement.TryGetProperty("student", out _))
            {
                role = "student";
            }
            else if (rolesElement.TryGetProperty("teacher", out _))
            {
                role = "teacher";
            }
            else if (rolesElement.ValueKind == JsonValueKind.Array && rolesElement.GetArrayLength() > 0)
            {
                role = rolesElement[0].TryGetProperty("role", out var roleElement) ? roleElement.GetString() : null;
            }
        }

        if (string.IsNullOrEmpty(role))
        {
            _logger.LogDebug("Event {EventId} has no role. Skipping.", evt.Id);
            if (eventsSummary != null) eventsSummary.EventsSkipped++;
            return;
        }

        switch (eventType.ToLower())
        {
            case "created":
            case "updated":
                if (role == "student")
                {
                    var student = JsonSerializer.Deserialize<CleverStudent>(rawDataJson);
                    if (student != null)
                    {
                        context.Result.StudentsProcessed++;
                        bool hasChanges = await _studentHandler.UpsertAsync(context, student, syncId, changeTracker);
                        if (hasChanges) context.Result.StudentsUpdated++;
                        if (eventsSummary != null)
                        {
                            if (eventType.ToLower() == "created") eventsSummary.StudentCreated++;
                            else eventsSummary.StudentUpdated++;
                        }
                    }
                }
                else if (role == "teacher")
                {
                    var teacher = JsonSerializer.Deserialize<CleverTeacher>(rawDataJson);
                    if (teacher != null)
                    {
                        context.Result.TeachersProcessed++;
                        bool hasChanges = await _teacherHandler.UpsertAsync(context, teacher, syncId, changeTracker);
                        if (hasChanges) context.Result.TeachersUpdated++;
                        if (eventsSummary != null)
                        {
                            if (eventType.ToLower() == "created") eventsSummary.TeacherCreated++;
                            else eventsSummary.TeacherUpdated++;
                        }
                    }
                }
                break;

            case "deleted":
                if (role == "student")
                {
                    var deleted = await _studentHandler.HandleDeleteAsync(context, evt.Data.Id, syncId, changeTracker);
                    if (deleted && eventsSummary != null) eventsSummary.StudentDeleted++;
                }
                else if (role == "teacher")
                {
                    var deleted = await _teacherHandler.HandleDeleteAsync(context, evt.Data.Id, syncId, changeTracker);
                    if (deleted && eventsSummary != null) eventsSummary.TeacherDeleted++;
                }
                break;
        }
    }

    private async Task ProcessSectionEventAsync(
        SyncContext context,
        CleverEvent evt,
        string rawDataJson,
        string eventType,
        int syncId,
        ChangeTracker changeTracker)
    {
        var eventsSummary = context.Result.EventsSummary;

        switch (eventType.ToLower())
        {
            case "created":
            case "updated":
                var section = JsonSerializer.Deserialize<CleverSection>(rawDataJson);
                if (section != null)
                {
                    context.Result.SectionsProcessed++;
                    bool hasChanges = await _sectionHandler.UpsertAsync(context, section, syncId, changeTracker);
                    if (hasChanges) context.Result.SectionsUpdated++;

                    // Also sync enrollments
                    var existingSection = await context.SchoolDb.Sections
                        .FirstOrDefaultAsync(s => s.CleverSectionId == section.Id, context.CancellationToken);
                    if (existingSection != null)
                    {
                        await _sectionHandler.SyncSectionTeachersAsync(context, existingSection, section.Teachers, section.Teacher);
                        await _sectionHandler.SyncSectionStudentsAsync(context, existingSection, section.Students, context.WorkshopLinkedSectionIds);
                    }

                    if (eventsSummary != null)
                    {
                        if (eventType.ToLower() == "created") eventsSummary.SectionCreated++;
                        else eventsSummary.SectionUpdated++;
                    }
                }
                break;

            case "deleted":
                var deleted = await _sectionHandler.HandleDeleteAsync(context, evt.Data.Id, syncId, changeTracker);
                if (deleted && eventsSummary != null) eventsSummary.SectionDeleted++;
                break;
        }
    }

    private async Task ProcessTermEventAsync(
        SyncContext context,
        CleverEvent evt,
        string rawDataJson,
        string eventType,
        int syncId,
        ChangeTracker changeTracker)
    {
        var eventsSummary = context.Result.EventsSummary;

        switch (eventType.ToLower())
        {
            case "created":
            case "updated":
                var term = JsonSerializer.Deserialize<CleverTerm>(rawDataJson);
                if (term != null)
                {
                    context.Result.TermsProcessed++;
                    bool hasChanges = await _termHandler.UpsertAsync(context, term, syncId, changeTracker);
                    if (hasChanges) context.Result.TermsUpdated++;
                    if (eventsSummary != null)
                    {
                        if (eventType.ToLower() == "created") eventsSummary.TermCreated++;
                        else eventsSummary.TermUpdated++;
                    }
                }
                break;

            case "deleted":
                var deleted = await _termHandler.HandleDeleteAsync(context, evt.Data.Id, syncId, changeTracker);
                if (deleted && eventsSummary != null) eventsSummary.TermDeleted++;
                break;
        }
    }
}
