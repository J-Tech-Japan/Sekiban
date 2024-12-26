using Sekiban.Core.Documents;
using Sekiban.Core.History;
namespace Sekiban.Core.Command;

public record CommandDocumentForJsonExport
{
    [JsonPropertyName("id")]
    public Guid Id { get; init; }

    public Guid AggregateId { get; init; }

    public string PartitionKey { get; init; } = default!;

    public DocumentType DocumentType { get; init; }

    public string DocumentTypeName { get; init; } = default!;

    public DateTime TimeStamp { get; init; }

    public string SortableUniqueId { get; init; } = default!;
    public string AggregateType { get; init; } = string.Empty;
    public string RootPartitionKey { get; init; } = string.Empty;

    public dynamic Payload { get; init; } = default!;
    public string? ExecutedUser { get; init; } = string.Empty;
    public string? Exception { get; init; }
    public List<CallHistory> CallHistories { get; init; } = [];
}
