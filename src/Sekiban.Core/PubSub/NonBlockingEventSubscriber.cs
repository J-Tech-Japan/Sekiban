using MediatR;
using Sekiban.Core.Events;
namespace Sekiban.Core.PubSub;

public class NonBlockingEventSubscriber<TEventPayload, TEventSubscriber> : INotificationHandler<Event<TEventPayload>>
    where TEventPayload : IEventPayloadCommon where TEventSubscriber : IEventSubscriber<TEventPayload, TEventSubscriber>
{
    private readonly IEventSubscriber<TEventPayload, TEventSubscriber> _eventSubscriber;
    public NonBlockingEventSubscriber(IEventSubscriber<TEventPayload, TEventSubscriber> eventSubscriber) => _eventSubscriber = eventSubscriber;

    public async Task Handle(Event<TEventPayload> ev, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        var thread = new Thread(() => _eventSubscriber.HandleEventAsync(ev));
        thread.Start();
    }
}
