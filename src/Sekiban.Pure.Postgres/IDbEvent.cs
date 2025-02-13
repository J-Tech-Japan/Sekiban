namespace Sekiban.Pure.Postgres;

public interface IDbEvent
{
    public Guid Id { get; init; }
    public string Payload { get; init; }
    public int Version { get; init; }
    public Guid AggregateId { get; init; }
    public string PartitionKey { get; init; }
    public DateTime TimeStamp { get; init; }
    public string SortableUniqueId { get; init; }
    public string AggregateGroup { get; init; }
    public string RootPartitionKey { get; init; }
    public string PayloadTypeName { get; init; }
    public string CausationId { get; init; }
    public string CorrelationId { get; init; }
    public string ExecutedUser { get; init; }
}