using MediatR;
using Sekiban.Core.Events;
namespace Sekiban.Core.PubSub;

/// <summary>
///     General Event Subscriber interface.
/// </summary>
/// <typeparam name="TEventPayload"></typeparam>
/// <typeparam name="TEventSubscriber">Use own Event subscriber class to make interface original</typeparam>
// ReSharper disable once UnusedTypeParameter
public class EventSubscriber<TEventPayload, TEventSubscriber> : INotificationHandler<Event<TEventPayload>> where TEventPayload : IEventPayloadCommon
    where TEventSubscriber : IEventSubscriber<TEventPayload, TEventSubscriber>
{
    private readonly IEventSubscriber<TEventPayload, TEventSubscriber> _eventSubscriber;
    public EventSubscriber(IEventSubscriber<TEventPayload, TEventSubscriber> eventSubscriber) => _eventSubscriber = eventSubscriber;

    public async Task Handle(Event<TEventPayload> ev, CancellationToken cancellationToken)
    {
        await _eventSubscriber.HandleEventAsync(ev);
    }
}
