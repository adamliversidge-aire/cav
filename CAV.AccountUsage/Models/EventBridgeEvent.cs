using System.Text.Json.Serialization;

namespace CAV.AccountUsage.Models;

public class EventBridgeEvent<T> where T : class
{
    public required string Id { get; set; }
    [JsonPropertyName("detail-type")]
    public required string DetailType { get; set; }
    public required string Source { get; set; }
    public required string Account { get; set; }
    public DateTime Time { get; set; }
    public required string Region { get; set; }
    public IReadOnlyList<string> Resources = [];
    public T? Detail { get; set; }
}