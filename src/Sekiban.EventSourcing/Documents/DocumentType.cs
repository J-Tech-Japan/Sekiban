namespace Sekiban.EventSourcing.Documents;

public enum DocumentType
{
    AggregateCommand = 1,
    AggregateEvent = 2,
    AggregateSnapshot = 3,
    IntegratedCommand = 11,
    IntegratedEvent = 12,
    SnapshotList = 21,
    SnapshotListChunk = 22
}
