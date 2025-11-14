using System.Text.Json.Serialization;

namespace CleverSyncSOS.Core.CleverApi.Models;

/// <summary>
/// DTO for Clever API teacher response.
/// Clever v3.0 returns nested structure with roles.teacher containing teacher-specific data.
/// </summary>
public class CleverTeacher
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public CleverName Name { get; set; } = new();

    [JsonPropertyName("email")]
    public string Email { get; set; } = string.Empty;

    [JsonPropertyName("last_modified")]
    public DateTime? LastModified { get; set; }

    [JsonPropertyName("roles")]
    public CleverTeacherRoles? Roles { get; set; }

    // Convenience properties that extract from nested roles
    [JsonIgnore]
    public string? Title => Roles?.Teacher?.Title;

    [JsonIgnore]
    public string? School => Roles?.Teacher?.School;

    [JsonIgnore]
    public string? SisId => Roles?.Teacher?.SisId;

    [JsonIgnore]
    public string? TeacherNumber => Roles?.Teacher?.TeacherNumber;

    [JsonIgnore]
    public string? StateId => Roles?.Teacher?.StateId;
}

/// <summary>
/// Nested roles structure from Clever API.
/// </summary>
public class CleverTeacherRoles
{
    [JsonPropertyName("teacher")]
    public CleverTeacherRole? Teacher { get; set; }
}

/// <summary>
/// Teacher role data from Clever API.
/// </summary>
public class CleverTeacherRole
{
    [JsonPropertyName("sis_id")]
    public string? SisId { get; set; }

    [JsonPropertyName("teacher_number")]
    public string? TeacherNumber { get; set; }

    [JsonPropertyName("state_id")]
    public string? StateId { get; set; }

    [JsonPropertyName("title")]
    public string? Title { get; set; }

    [JsonPropertyName("school")]
    public string? School { get; set; }

    [JsonPropertyName("schools")]
    public string[]? Schools { get; set; }

    [JsonPropertyName("credentials")]
    public CleverTeacherCredentials? Credentials { get; set; }
}

/// <summary>
/// Teacher credentials from Clever API.
/// </summary>
public class CleverTeacherCredentials
{
    [JsonPropertyName("district_username")]
    public string? DistrictUsername { get; set; }
}
