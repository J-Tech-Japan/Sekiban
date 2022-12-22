using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Events;

public interface IEventPayload<TAggregatePayload> : IEventPayloadCommon where TAggregatePayload : IAggregatePayload
{
    public TAggregatePayload OnEvent(TAggregatePayload payload, IEvent ev);
}
