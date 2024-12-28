using Sekiban.Core.Events;
namespace Sekiban.Core.Query.SingleProjections;

/// <summary>
///     Single projection applicable to events.
///     Aggregate Developers does not need to implement this interface directly.
/// </summary>
/// <typeparam name="TProjectionPayload"></typeparam>
public interface ISingleProjectionEventApplicable<TProjectionPayload> : ISingleProjectionPayloadCommon
    where TProjectionPayload : ISingleProjectionPayloadCommon
{
    static abstract TProjectionPayload? ApplyEvent<TEventPayload>(
        TProjectionPayload projectionPayload,
        Event<TEventPayload> ev) where TEventPayload : IEventPayloadCommon;
}
