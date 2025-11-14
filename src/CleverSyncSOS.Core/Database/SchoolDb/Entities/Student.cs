namespace CleverSyncSOS.Core.Database.SchoolDb.Entities;

/// <summary>
/// Represents a student record synced from Clever API.
/// </summary>
public class Student
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public int StudentId { get; set; }

    /// <summary>
    /// Clever's student identifier.
    /// </summary>
    public string CleverStudentId { get; set; } = string.Empty;

    /// <summary>
    /// Student's first name.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Student's last name.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Student's email address.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Student's grade level.
    /// </summary>
    public string? Grade { get; set; }

    /// <summary>
    /// School's local student number.
    /// </summary>
    public string? StudentNumber { get; set; }

    /// <summary>
    /// Clever's last_modified timestamp.
    /// </summary>
    public DateTime? LastModifiedInClever { get; set; }

    /// <summary>
    /// Whether the student is currently active in the school.
    /// Set to false when student is not present in Clever during full sync (graduated, transferred, etc.).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Timestamp when student was marked as inactive.
    /// Set during full sync when student is no longer in Clever.
    /// </summary>
    public DateTime? DeactivatedAt { get; set; }

    /// <summary>
    /// Timestamp of record creation.
    /// </summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>
    /// Timestamp of last update.
    /// </summary>
    public DateTime UpdatedAt { get; set; }
}
