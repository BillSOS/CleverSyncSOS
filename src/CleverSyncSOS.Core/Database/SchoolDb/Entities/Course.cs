namespace CleverSyncSOS.Core.Database.SchoolDb.Entities;

/// <summary>
/// Represents a course offering in the school catalog.
/// Courses are parent entities that have one or more sections.
/// </summary>
public class Course
{
    /// <summary>
    /// Primary key, auto-increment
    /// </summary>
    public int CourseId { get; set; }

    /// <summary>
    /// Unique identifier from Clever API
    /// </summary>
    public string CleverCourseId { get; set; } = string.Empty;

    /// <summary>
    /// Foreign key to Schools table (for multi-school support)
    /// </summary>
    public int SchoolId { get; set; }

    /// <summary>
    /// Course name (e.g., "Algebra I")
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Course code/number (e.g., "MATH-101")
    /// </summary>
    public string? Number { get; set; }

    /// <summary>
    /// Normalized subject from Clever (e.g., "english/language arts")
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// JSON array of applicable grades (e.g., ["9", "10", "11"])
    /// </summary>
    public string? GradeLevels { get; set; }

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
    /// When course was deactivated
    /// </summary>
    public DateTime? DeactivatedAt { get; set; }

    /// <summary>
    /// Navigation property: Sections for this course
    /// </summary>
    public ICollection<Section> Sections { get; set; } = new List<Section>();
}
