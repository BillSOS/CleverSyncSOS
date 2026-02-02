using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CleverSyncSOS.Core.Database.SchoolDb.Entities;

/// <summary>
/// Represents an individual class section.
/// Maps to [dbo].[Section] table in SchoolDb.
/// </summary>
[Table("Section")]
public class Section
{
    /// <summary>
    /// Primary key, auto-increment
    /// </summary>
    [Key]
    public int SectionId { get; set; }

    /// <summary>
    /// Section number (from Clever API SectionNumber field)
    /// </summary>
    [Required]
    [MaxLength(32)]
    public string SectionNumber { get; set; } = string.Empty;

    /// <summary>
    /// Subject/Department from Clever
    /// </summary>
    [MaxLength(64)]
    public string? Subject { get; set; }

    /// <summary>
    /// Class period designation (e.g., "1st Period")
    /// </summary>
    [MaxLength(64)]
    public string? Period { get; set; }

    /// <summary>
    /// Section name (e.g., "Algebra I - Period 1")
    /// </summary>
    [MaxLength(64)]
    public string? SectionName { get; set; }

    /// <summary>
    /// Unique identifier from Clever API
    /// </summary>
    [Required]
    [MaxLength(50)]
    public string CleverSectionId { get; set; } = string.Empty;

    /// <summary>
    /// Clever Course ID associated with this section (optional - sections can exist without courses)
    /// Stored for reference/grouping purposes but not synced to a local Course table
    /// </summary>
    [MaxLength(50)]
    public string? CleverCourseId { get; set; }

    /// <summary>
    /// Term start date
    /// </summary>
    public DateTime TermStartDate { get; set; }

    /// <summary>
    /// Term end date
    /// </summary>
    public DateTime TermEndDate { get; set; }

    /// <summary>
    /// When the record was first created in our database
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the record's data last changed
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When soft-deleted (null = active)
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// When we last saw this record in Clever (for orphan detection).
    /// Uses local time based on the district's timezone setting.
    /// </summary>
    public DateTime LastSyncedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation property: Teacher-section associations
    /// </summary>
    public ICollection<TeacherSection> TeacherSections { get; set; } = new List<TeacherSection>();

    /// <summary>
    /// Navigation property: Student-section enrollments
    /// </summary>
    public ICollection<StudentSection> StudentSections { get; set; } = new List<StudentSection>();
}
