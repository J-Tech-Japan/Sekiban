using ResultBoxes;
using Sekiban.Pure.Command.Handlers;
using Sekiban.Pure.Events;
namespace Sekiban.Pure;

public interface ICommandContext<TAggregatePayload> : ICommandContextWithoutState
    where TAggregatePayload : IAggregatePayload
{
    CommandExecuted ICommandContextWithoutState.GetCommandExecuted(List<IEvent> producedEvents) => new(
        GetAggregateCommon().PartitionKeys,
        GetAggregateCommon().LastSortableUniqueId,
        producedEvents);
    internal IAggregate GetAggregateCommon();
    public ResultBox<Aggregate<TAggregatePayload>> GetAggregate();
}
