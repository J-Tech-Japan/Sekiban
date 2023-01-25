using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Events;

public interface IEventPayloadApplicableTo<TAggregatePayload> : IEventPayloadCommon where TAggregatePayload : IAggregatePayload
{
}
