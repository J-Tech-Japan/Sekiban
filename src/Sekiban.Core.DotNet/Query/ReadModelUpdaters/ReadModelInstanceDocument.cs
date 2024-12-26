using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Query.ReadModelUpdaters;

public record ReadModelInstanceDocument(
    Guid Id,
    string PartitionKey,
    DocumentType DocumentType,
    string DocumentTypeName,
    DateTime TimeStamp,
    string SortableUniqueId,
    string AggregateType,
    string RootPartitionKey,
    string SafeSortableUniqueId,
    Guid ReadModelInstanceId,
    string TableName,
    bool IsActive) : IDocument
{
    public SortableUniqueIdValue GetSortableUniqueId() => new(SortableUniqueId);
}
