using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Event;

public interface IApplicableEvent<TAggregatePayload> : IEventPayload where TAggregatePayload : IAggregatePayload
{
    public TAggregatePayload OnEvent(TAggregatePayload payload, IEvent ev);
}
