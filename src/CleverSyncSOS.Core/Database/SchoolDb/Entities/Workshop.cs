namespace CleverSyncSOS.Core.Database.SchoolDb.Entities;

/// <summary>
/// Represents a workshop that can be linked to sections.
/// Workshops are used to organize and track professional development or instructional activities.
/// </summary>
public class Workshop
{
    /// <summary>
    /// Primary key, auto-increment
    /// </summary>
    public int WorkshopId { get; set; }

    /// <summary>
    /// Name of the workshop
    /// </summary>
    public string WorkshopName { get; set; } = string.Empty;

    /// <summary>
    /// Navigation property: Sections linked to this workshop
    /// </summary>
    public ICollection<WorkshopSection> WorkshopSections { get; set; } = new List<WorkshopSection>();
}
