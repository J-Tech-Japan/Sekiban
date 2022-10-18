using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Event;

public interface ICreatedAggregateEventPayload<TAggregate> : ICreatedEventPayload, IAggregatePointerEvent<TAggregate> where TAggregate : IAggregate
{
}
