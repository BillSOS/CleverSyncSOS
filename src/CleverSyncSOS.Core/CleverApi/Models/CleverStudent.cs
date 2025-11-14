using System.Text.Json.Serialization;

namespace CleverSyncSOS.Core.CleverApi.Models;

/// <summary>
/// DTO for Clever API student response.
/// Clever v3.0 returns nested structure with roles.student containing student-specific data.
/// </summary>
public class CleverStudent
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public CleverName Name { get; set; } = new();

    [JsonPropertyName("email")]
    public string? Email { get; set; }

    [JsonPropertyName("last_modified")]
    public DateTime? LastModified { get; set; }

    [JsonPropertyName("roles")]
    public CleverStudentRoles? Roles { get; set; }

    // Convenience properties that extract from nested roles
    [JsonIgnore]
    public string? Grade => Roles?.Student?.GraduationYear;

    [JsonIgnore]
    public string? StudentNumber => Roles?.Student?.StudentNumber;

    [JsonIgnore]
    public string? School => Roles?.Student?.School;

    [JsonIgnore]
    public string? SisId => Roles?.Student?.SisId;
}

/// <summary>
/// Nested roles structure from Clever API.
/// </summary>
public class CleverStudentRoles
{
    [JsonPropertyName("student")]
    public CleverStudentRole? Student { get; set; }
}

/// <summary>
/// Student role data from Clever API.
/// </summary>
public class CleverStudentRole
{
    [JsonPropertyName("sis_id")]
    public string? SisId { get; set; }

    [JsonPropertyName("student_number")]
    public string? StudentNumber { get; set; }

    [JsonPropertyName("graduation_year")]
    public string? GraduationYear { get; set; }

    [JsonPropertyName("school")]
    public string? School { get; set; }

    [JsonPropertyName("schools")]
    public string[]? Schools { get; set; }

    [JsonPropertyName("email")]
    public string? Email { get; set; }
}
