using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Event;

public interface ICreatedEvent<TAggregatePayload> : ICreatedAggregateEventPayload<TAggregatePayload>, IApplicableEvent<TAggregatePayload>
    where TAggregatePayload : IAggregatePayload
{
}
