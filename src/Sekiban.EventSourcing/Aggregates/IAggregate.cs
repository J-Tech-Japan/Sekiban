using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Aggregates
{
    public interface IAggregate : ISingleAggregate, ISingleAggregateProjection
    {
        ReadOnlyCollection<IAggregateEvent> Events { get; }
    }
}
