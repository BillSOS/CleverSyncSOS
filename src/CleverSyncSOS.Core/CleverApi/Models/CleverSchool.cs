using System.Text.Json.Serialization;

namespace CleverSyncSOS.Core.CleverApi.Models;

/// <summary>
/// DTO for Clever API school response.
/// </summary>
public class CleverSchool
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("district")]
    public string? District { get; set; }

    [JsonPropertyName("last_modified")]
    public DateTime? LastModified { get; set; }
}
