using Sekiban.Core.Events;
namespace Sekiban.Core.Query.SingleProjections;

public interface ISingleProjectionEventApplicable<TProjectionPayload> : ISingleProjectionPayloadCommon
    where TProjectionPayload : ISingleProjectionPayloadCommon
{
    static abstract TProjectionPayload? ApplyEvent<TEventPayload>(TProjectionPayload projectionPayload, Event<TEventPayload> ev)
        where TEventPayload : IEventPayloadCommon;
}
