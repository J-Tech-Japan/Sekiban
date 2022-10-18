using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Event;

public interface IChangedAggregateEventPayload<TAggregate> : IChangedEventPayload, IAggregatePointerEvent<TAggregate> where TAggregate : IAggregate
{
}
