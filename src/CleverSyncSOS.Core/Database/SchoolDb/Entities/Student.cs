using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CleverSyncSOS.Core.Database.SchoolDb.Entities;

/// <summary>
/// Represents a student record synced from Clever API.
/// Maps to [dbo].[Student] table in SchoolDb.
/// </summary>
[Table("Student")]
public class Student
{
    [Key]
    public int StudentId { get; set; }

    [Required]
    [MaxLength(32)]
    public string FirstName { get; set; } = string.Empty;

    [MaxLength(32)]
    public string? MiddleName { get; set; }

    [Required]
    [MaxLength(32)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(255)]
    public string? BlendedLearningAssignment { get; set; }

    public int? Grade { get; set; }

    [Required]
    [MaxLength(32)]
    public string StateStudentId { get; set; } = string.Empty;

    public bool KeepWithoutSchedule { get; set; }

    public bool VirtualStudent { get; set; }

    public bool NoOffCampus { get; set; }

    public bool? TeacherCloses { get; set; }

    public int? DailyHallPasses { get; set; }

    public int? WeeklyHallPasses { get; set; }

    [Required]
    [MaxLength(50)]
    public string CleverStudentId { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string StudentNumber { get; set; } = string.Empty;

    [MaxLength(20)]
    public string GradeLevel { get; set; } = "0";

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
}
