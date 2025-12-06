namespace CleverSyncSOS.Core.Database.SchoolDb.Entities;

/// <summary>
/// Represents an individual class section.
/// Each section is an instance of a course taught during a specific period/term.
/// </summary>
public class Section
{
    /// <summary>
    /// Primary key, auto-increment
    /// </summary>
    public int SectionId { get; set; }

    /// <summary>
    /// Unique identifier from Clever API
    /// </summary>
    public string CleverSectionId { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key to Courses (required - all sections must have a course)
    /// </summary>
    public int CourseId { get; set; }

    /// <summary>
    /// Foreign key to Schools table
    /// </summary>
    public int SchoolId { get; set; }

    /// <summary>
    /// Section name (e.g., "Algebra I - Period 1")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Class period designation (e.g., "1st Period")
    /// </summary>
    public string? Period { get; set; }

    /// <summary>
    /// Subject from Clever
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// Clever's normalized subject taxonomy
    /// </summary>
    public string? SubjectNormalized { get; set; }

    /// <summary>
    /// Clever term identifier
    /// </summary>
    public string? TermId { get; set; }

    /// <summary>
    /// Term name (e.g., "Fall 2024")
    /// </summary>
    public string? TermName { get; set; }

    /// <summary>
    /// Term start date
    /// </summary>
    public DateTime? TermStartDate { get; set; }

    /// <summary>
    /// Term end date
    /// </summary>
    public DateTime? TermEndDate { get; set; }

    /// <summary>
    /// Grade level for section (e.g., "9", "K")
    /// </summary>
    public string? Grade { get; set; }

    /// <summary>
    /// Soft delete flag
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Last modified timestamp from Clever API
    /// </summary>
    public DateTime? LastModifiedInClever { get; set; }

    /// <summary>
    /// Record creation timestamp
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When section was deactivated
    /// </summary>
    public DateTime? DeactivatedAt { get; set; }

    /// <summary>
    /// Navigation property: Parent course (required)
    /// </summary>
    public Course Course { get; set; } = null!;

    /// <summary>
    /// Navigation property: Teacher-section associations
    /// </summary>
    public ICollection<TeacherSection> TeacherSections { get; set; } = new List<TeacherSection>();

    /// <summary>
    /// Navigation property: Student-section enrollments
    /// </summary>
    public ICollection<StudentSection> StudentSections { get; set; } = new List<StudentSection>();
}
