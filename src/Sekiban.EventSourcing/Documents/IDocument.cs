namespace Sekiban.EventSourcing.Documents;

public interface IDocument
{
    public Guid Id { get; init; }
    public string PartitionKey { get; }

    public DocumentType DocumentType { get; }
    public string DocumentTypeName { get; }
    public DateTime TimeStamp { get; }
    public string SortableUniqueId { get; }
}
