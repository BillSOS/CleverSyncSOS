using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using CleverSyncSOS.Core.CleverApi.Models;

namespace CleverSyncSOS.Core.Database.SchoolDb.Entities;

/// <summary>
/// Many-to-many relationship between Students and Sections.
/// Tracks student enrollments in sections.
/// Maps to [Student_X_Section] table.
/// </summary>
[Table("Student_X_Section")]
public class StudentSection
{
    /// <summary>
    /// Primary key, auto-increment
    /// </summary>
    [Key]
    [Column("StudentXSectionId")]
    public int StudentSectionId { get; set; }

    /// <summary>
    /// Foreign key to Students table
    /// </summary>
    public int StudentId { get; set; }

    /// <summary>
    /// Foreign key to Sections table
    /// </summary>
    public int SectionId { get; set; }

    /// <summary>
    /// Whether the student is off-campus for this section
    /// </summary>
    public bool OffCampus { get; set; }

    /// <summary>
    /// When enrollment was created
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Navigation property: Student
    /// </summary>
    public Student Student { get; set; } = null!;

    /// <summary>
    /// Navigation property: Section
    /// </summary>
    public Section Section { get; set; } = null!;
}
