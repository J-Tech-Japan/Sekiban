using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public interface IReadModel
{
    string LastSortableUniqueId { get; }
}
public record ReadModelGroupState(
    Guid Id,
    string PartitionKey,
    DocumentType DocumentType,
    string DocumentTypeName,
    DateTime TimeStamp,
    string SortableUniqueId,
    string AggregateType,
    string RootPartitionKey) : IDocument
{
    public SortableUniqueIdValue GetSortableUniqueId() => new(SortableUniqueId);
}
