using Sekiban.Pure.Aggregates;

namespace Sekiban.Pure.OrleansEventSourcing;

public static class OrleansAggregateExtensions
{
    public static OrleansAggregate ToOrleansAggregate(this IAggregate aggregate)
    {
        return new OrleansAggregate(OrleansAggregate.ConvertPayload(aggregate.GetPayload()), aggregate.PartitionKeys.ToOrleansPartitionKeys(), aggregate.Version,
            aggregate.LastSortableUniqueId, aggregate.ProjectorVersion, aggregate.ProjectorTypeName, aggregate.PayloadTypeName);
    }
    public static Aggregate ToAggregate(this OrleansAggregate oAggregate)
        => new Aggregate(OrleansAggregate.ConvertPayload(oAggregate.Payload), oAggregate.PartitionKeys.ToPartitionKeys(), oAggregate.Version, oAggregate.LastSortableUniqueId, oAggregate.ProjectorVersion, oAggregate.ProjectorTypeName, oAggregate.PayloadTypeName);
}