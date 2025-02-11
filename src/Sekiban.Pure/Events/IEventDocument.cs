namespace Sekiban.Pure.Events;

public interface IEventDocument
{
    Guid Id { get; }
    string SortableUniqueId { get; }
    int Version { get; }
    Guid AggregateId { get; }
    string AggregateGroup { get; }
    string RootPartitionKey { get; }
    string PayloadTypeName { get; }
    DateTime TimeStamp { get; }
    string PartitionKey { get; }
    EventMetadata Metadata { get; }
}