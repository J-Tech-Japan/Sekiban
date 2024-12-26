using Sekiban.Core.Events;
namespace Sekiban.Core.PubSub;

/// <summary>
///     Event Subscriber interface
/// </summary>
/// <typeparam name="TEventPayload"></typeparam>
/// <typeparam name="TEventSubscriber">Use own Event subscriber class to make interface original</typeparam>
public interface IEventSubscriber<TEventPayload, TEventSubscriber> where TEventPayload : IEventPayloadCommon
    where TEventSubscriber : IEventSubscriber<TEventPayload, TEventSubscriber>
{
    public Task HandleEventAsync(Event<TEventPayload> ev);
}
