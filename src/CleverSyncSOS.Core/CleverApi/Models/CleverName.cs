using System.Text.Json.Serialization;

namespace CleverSyncSOS.Core.CleverApi.Models;

/// <summary>
/// Represents a name object from Clever API.
/// </summary>
public class CleverName
{
    [JsonPropertyName("first")]
    public string First { get; set; } = string.Empty;

    [JsonPropertyName("last")]
    public string Last { get; set; } = string.Empty;

    [JsonPropertyName("middle")]
    public string? Middle { get; set; }
}
