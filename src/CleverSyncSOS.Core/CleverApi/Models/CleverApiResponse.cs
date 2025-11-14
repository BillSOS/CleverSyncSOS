using System.Text.Json.Serialization;

namespace CleverSyncSOS.Core.CleverApi.Models;

/// <summary>
/// Generic wrapper for Clever API paged responses.
/// Clever v3.0 uses cursor-based pagination with links.
/// </summary>
/// <typeparam name="T">The type of data in the response.</typeparam>
public class CleverApiResponse<T>
{
    [JsonPropertyName("data")]
    public T[] Data { get; set; } = Array.Empty<T>();

    [JsonPropertyName("links")]
    public CleverLink[]? Links { get; set; }
}

/// <summary>
/// Link object for pagination navigation.
/// </summary>
public class CleverLink
{
    [JsonPropertyName("rel")]
    public string Rel { get; set; } = string.Empty;

    [JsonPropertyName("uri")]
    public string Uri { get; set; } = string.Empty;
}

/// <summary>
/// Wrapper for single Clever API data item.
/// </summary>
/// <typeparam name="T">The type of data.</typeparam>
public class CleverDataWrapper<T>
{
    [JsonPropertyName("data")]
    public T Data { get; set; } = default!;
}
