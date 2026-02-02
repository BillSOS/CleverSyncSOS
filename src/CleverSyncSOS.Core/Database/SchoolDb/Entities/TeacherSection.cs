namespace CleverSyncSOS.Core.Database.SchoolDb.Entities;

/// <summary>
/// Many-to-many relationship between Teachers and Sections.
/// Tracks which teachers are assigned to which sections.
/// </summary>
public class TeacherSection
{
    /// <summary>
    /// Primary key, auto-increment
    /// </summary>
    public int TeacherSectionId { get; set; }

    /// <summary>
    /// Foreign key to Teachers table
    /// </summary>
    public int TeacherId { get; set; }

    /// <summary>
    /// Foreign key to Sections table
    /// </summary>
    public int SectionId { get; set; }

    /// <summary>
    /// True if this is the primary teacher for the section
    /// </summary>
    public bool IsPrimary { get; set; } = false;

    /// <summary>
    /// When association was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation property: Teacher
    /// </summary>
    public Teacher Teacher { get; set; } = null!;

    /// <summary>
    /// Navigation property: Section
    /// </summary>
    public Section Section { get; set; } = null!;
}
