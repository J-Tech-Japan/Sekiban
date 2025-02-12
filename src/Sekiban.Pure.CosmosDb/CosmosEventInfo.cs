using System.Text.Json.Serialization;

namespace Sekiban.Pure.CosmosDb;

public record CosmosEventInfo
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }
    public string PartitionKey { get; init; } = string.Empty;
    public string RootPartitionKey { get; init; } = string.Empty;
    public string AggregateType { get; init; } = string.Empty;
}