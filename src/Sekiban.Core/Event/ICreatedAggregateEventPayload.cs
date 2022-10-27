using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Event;

public interface ICreatedAggregateEventPayload<TAggregatePayload> : ICreatedEventPayload, IApplicableEvent<TAggregatePayload>
    where TAggregatePayload : IAggregatePayload
{
}
