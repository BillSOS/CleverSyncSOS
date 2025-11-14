namespace CleverSyncSOS.Core.Database.SchoolDb.Entities;

/// <summary>
/// Represents a teacher record synced from Clever API.
/// </summary>
public class Teacher
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public int TeacherId { get; set; }

    /// <summary>
    /// Clever's teacher identifier.
    /// </summary>
    public string CleverTeacherId { get; set; } = string.Empty;

    /// <summary>
    /// Teacher's first name.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Teacher's last name.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Teacher's email address.
    /// </summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>
    /// Teacher's job title.
    /// </summary>
    public string? Title { get; set; }

    /// <summary>
    /// Clever's last_modified timestamp.
    /// </summary>
    public DateTime? LastModifiedInClever { get; set; }

    /// <summary>
    /// Whether the teacher is currently active in the school.
    /// Set to false when teacher is not present in Clever during full sync (resigned, transferred, etc.).
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Timestamp when teacher was marked as inactive.
    /// Set during full sync when teacher is no longer in Clever.
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
