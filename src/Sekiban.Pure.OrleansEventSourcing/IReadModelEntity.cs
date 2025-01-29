namespace Sekiban.Pure.OrleansEventSourcing
{
    public interface IReadModelEntity
    {
        Guid Id { get; }
        Guid TargetId { get; }
        string RootPartitionKey { get; }
        string AggregateGroup { get; }
        string LastSortableUniqueId { get; }
        DateTime TimeStamp { get; }
    }
}
