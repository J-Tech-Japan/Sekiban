using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Event;

public interface ICreatedEvent<TAggregatePayload> : ICreatedEventPayload, IApplicableEvent<TAggregatePayload>
    where TAggregatePayload : IAggregatePayload
{
}
