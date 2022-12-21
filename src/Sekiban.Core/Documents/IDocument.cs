using Sekiban.Core.Documents.ValueObjects;
namespace Sekiban.Core.Documents;

public interface IDocument
{
    public Guid Id { get; init; }
    public Guid AggregateId { get; init; }
    public string PartitionKey { get; init; }
    public DocumentType DocumentType { get; init; }
    public string DocumentTypeName { get; init; }
    public DateTime TimeStamp { get; init; }
    public string SortableUniqueId { get; init; }
    public SortableUniqueIdValue GetSortableUniqueId();
}
