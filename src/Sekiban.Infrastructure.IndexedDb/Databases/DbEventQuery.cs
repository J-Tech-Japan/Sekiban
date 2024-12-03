using Sekiban.Core.Documents.Pools;

namespace Sekiban.Infrastructure.IndexedDb.Databases;

public record DbEventQuery
{
    public string? RootPartitionKey { get; init; }
    public string? PartitionKey { get; init; }
    public string[] AggregateType { get; init; } = [];
    public string? SortableId { get; init; }

    // TODO
    public static DbEventQuery FromEventRetrievalInfo(EventRetrievalInfo eventRetrievalInfo) => new();
}
