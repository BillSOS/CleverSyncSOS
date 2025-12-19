namespace CleverSyncSOS.Core.Database.SchoolDb.Entities;

/// <summary>
/// Junction table linking Workshops to Sections.
/// Used to track which sections are associated with each workshop.
/// IMPORTANT: The sync process should alert if a linked section is modified or deleted.
/// </summary>
public class WorkshopSection
{
    /// <summary>
    /// Primary key, auto-increment
    /// </summary>
    public int WorkshopXSectionId { get; set; }

    /// <summary>
    /// Foreign key to Workshop table
    /// </summary>
    public int WorkshopId { get; set; }

    /// <summary>
    /// Priority ordering for the section within the workshop
    /// </summary>
    public int PriorityId { get; set; }

    /// <summary>
    /// Foreign key to Section table
    /// </summary>
    public int SectionId { get; set; }

    /// <summary>
    /// Whether to preserve existing assignments when syncing
    /// </summary>
    public bool KeepAssignments { get; set; }

    /// <summary>
    /// Navigation property: Related section
    /// </summary>
    public Section? Section { get; set; }

    /// <summary>
    /// Navigation property: Related workshop
    /// </summary>
    public Workshop? Workshop { get; set; }
}
