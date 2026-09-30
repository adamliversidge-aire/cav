using System.Text.Json.Serialization;

namespace CAV.EntraLeavers.Models;

/// <summary>
/// Scheduled EventBridge event. Nothing in it is used; the properties are optional so the
/// function can also be invoked manually with an empty payload ({}).
/// </summary>
public class EventBridgeEvent<T> where T : class
{
    public string? Id { get; set; }
    [JsonPropertyName("detail-type")]
    public string? DetailType { get; set; }
    public string? Source { get; set; }
    public string? Account { get; set; }
    public DateTime Time { get; set; }
    public string? Region { get; set; }
    public IReadOnlyList<string> Resources { get; set; } = [];
    public T? Detail { get; set; }
}
