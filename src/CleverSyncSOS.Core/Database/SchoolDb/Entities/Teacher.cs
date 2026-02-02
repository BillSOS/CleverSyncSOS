using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace CleverSyncSOS.Core.Database.SchoolDb.Entities;

/// <summary>
/// Represents a teacher record synced from Clever API.
/// Maps to [dbo].[Teacher] table in SchoolDb.
/// </summary>
[Table("Teacher")]
public class Teacher
{
    /// <summary>
    /// Unique identifier (Primary Key).
    /// </summary>
    [Key]
    public int TeacherId { get; set; }

    /// <summary>
    /// Teacher's first name.
    /// </summary>
    [Required]
    [MaxLength(32)]
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Teacher's last name.
    /// </summary>
    [Required]
    [MaxLength(32)]
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Priority ID (default 2).
    /// </summary>
    public int PriorityId { get; set; } = 2;

    /// <summary>
    /// Full name (computed or stored).
    /// </summary>
    [MaxLength(255)]
    public string? FullName { get; set; }

    /// <summary>
    /// Username for login.
    /// </summary>
    [MaxLength(255)]
    public string? UserName { get; set; }

    /// <summary>
    /// Password (hashed).
    /// </summary>
    [MaxLength(255)]
    public string? Password { get; set; }

    /// <summary>
    /// Whether the teacher is an administrator.
    /// </summary>
    public bool? Administrator { get; set; } = false;

    /// <summary>
    /// Whether to ignore this teacher during import.
    /// </summary>
    public bool? IgnoreImport { get; set; } = false;

    /// <summary>
    /// Virtual meeting URL.
    /// </summary>
    [MaxLength(256)]
    public string? VirtualMeeting { get; set; }

    /// <summary>
    /// Staff number (unique identifier from SIS).
    /// Maps to Clever's SisId.
    /// </summary>
    [Required]
    [MaxLength(32)]
    public string StaffNumber { get; set; } = string.Empty;

    /// <summary>
    /// Default room ID (FK to Room table).
    /// </summary>
    public int? DefaultRoomId { get; set; }

    /// <summary>
    /// Whether this teacher can see all students in workshops.
    /// </summary>
    public bool AllStudentsWorkshops { get; set; } = false;

    /// <summary>
    /// Room ID (FK to Room table).
    /// </summary>
    public int? RoomId { get; set; }

    /// <summary>
    /// Whether this teacher should not see workshops.
    /// </summary>
    public bool NoWorkshops { get; set; } = false;

    /// <summary>
    /// Clever's teacher identifier.
    /// </summary>
    [MaxLength(50)]
    public string? CleverTeacherId { get; set; }

    /// <summary>
    /// Legacy ID from previous system.
    /// </summary>
    [MaxLength(50)]
    public string? LegacyId { get; set; }

    /// <summary>
    /// Teacher number from Clever API.
    /// </summary>
    [MaxLength(50)]
    public string? TeacherNumber { get; set; }

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
