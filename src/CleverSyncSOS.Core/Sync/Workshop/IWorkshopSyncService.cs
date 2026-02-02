namespace CleverSyncSOS.Core.Sync.Workshop;

using CleverSyncSOS.Core.Database.SchoolDb;

/// <summary>
/// Tracks workshop-relevant changes during a sync operation.
/// Used to determine if the workshop sync stored procedure should be called.
/// </summary>
public class WorkshopSyncTracker
{
    /// <summary>
    /// Whether any students were added to or removed from workshop-linked sections.
    /// </summary>
    public bool HasWorkshopEnrollmentChanges { get; set; }

    /// <summary>
    /// Whether any student grades were changed.
    /// </summary>
    public bool HasGradeChanges { get; set; }

    /// <summary>
    /// Count of students added to workshop-linked sections.
    /// </summary>
    public int StudentsAddedToWorkshopSections { get; set; }

    /// <summary>
    /// Count of students removed from workshop-linked sections.
    /// </summary>
    public int StudentsRemovedFromWorkshopSections { get; set; }

    /// <summary>
    /// Count of students whose grades changed.
    /// </summary>
    public int StudentGradesChanged { get; set; }

    /// <summary>
    /// Whether any workshop-relevant changes occurred that require running the stored procedure.
    /// </summary>
    public bool RequiresWorkshopSync => HasWorkshopEnrollmentChanges || HasGradeChanges;

    /// <summary>
    /// Gets a summary of the changes for logging/reporting.
    /// </summary>
    public string GetSummary()
    {
        var parts = new List<string>();
        if (StudentsAddedToWorkshopSections > 0)
            parts.Add($"{StudentsAddedToWorkshopSections} students added to workshop sections");
        if (StudentsRemovedFromWorkshopSections > 0)
            parts.Add($"{StudentsRemovedFromWorkshopSections} students removed from workshop sections");
        if (StudentGradesChanged > 0)
            parts.Add($"{StudentGradesChanged} student grades changed");
        return parts.Count > 0 ? string.Join(", ", parts) : "No workshop-relevant changes";
    }

    /// <summary>
    /// Records a student being added to a workshop-linked section.
    /// </summary>
    public void RecordStudentAddedToWorkshopSection()
    {
        HasWorkshopEnrollmentChanges = true;
        StudentsAddedToWorkshopSections++;
    }

    /// <summary>
    /// Records a student being removed from a workshop-linked section.
    /// </summary>
    public void RecordStudentRemovedFromWorkshopSection()
    {
        HasWorkshopEnrollmentChanges = true;
        StudentsRemovedFromWorkshopSections++;
    }

    /// <summary>
    /// Records a student grade change.
    /// </summary>
    public void RecordGradeChange()
    {
        HasGradeChanges = true;
        StudentGradesChanged++;
    }
}

/// <summary>
/// Result of workshop sync execution.
/// </summary>
public class WorkshopSyncResult
{
    /// <summary>
    /// Whether the workshop sync was successful.
    /// </summary>
    public bool Success { get; set; }

    /// <summary>
    /// Error message if the sync failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether workshop sync was skipped because no changes were detected.
    /// </summary>
    public bool Skipped { get; set; }

    /// <summary>
    /// Summary of changes that triggered the sync.
    /// </summary>
    public string? ChangesSummary { get; set; }
}

/// <summary>
/// Service for executing workshop synchronization after sync operations.
/// Calls the stored procedure spSyncWorkshops_FromSectionsAndGrades_WithAudit
/// when workshop-relevant changes are detected.
/// </summary>
public interface IWorkshopSyncService
{
    /// <summary>
    /// Executes the workshop sync stored procedure if workshop-relevant changes were detected.
    /// </summary>
    /// <param name="schoolDb">School database context</param>
    /// <param name="syncId">The SyncId to pass to the stored procedure</param>
    /// <param name="workshopTracker">Tracker containing workshop-relevant change information</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Result of the workshop sync operation</returns>
    Task<WorkshopSyncResult> ExecuteWorkshopSyncAsync(
        SchoolDbContext schoolDb,
        int syncId,
        WorkshopSyncTracker workshopTracker,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a list of section IDs that are linked to workshops.
    /// Used to determine which sections should trigger workshop sync when modified.
    /// </summary>
    /// <param name="schoolDb">School database context</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Set of section IDs linked to workshops</returns>
    Task<HashSet<int>> GetWorkshopLinkedSectionIdsAsync(
        SchoolDbContext schoolDb,
        CancellationToken cancellationToken = default);
}
