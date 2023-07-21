using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Events;
namespace Sekiban.Core.PubSub;

public class NonBlockingEventSubscriber<TEventPayload, TEventSubscriber> : INotificationHandler<Event<TEventPayload>>
    where TEventPayload : IEventPayloadCommon where TEventSubscriber : IEventSubscriber<TEventPayload, TEventSubscriber>
{
    private readonly EventNonBlockingStatus _eventNonBlockingStatus;
    // private readonly IEventSubscriber<TEventPayload, TEventSubscriber> _eventSubscriber;
    private readonly IServiceProvider _serviceProvider;
    public NonBlockingEventSubscriber(IServiceProvider serviceProvider, EventNonBlockingStatus eventNonBlockingStatus)
    {
        _serviceProvider = serviceProvider;
        _eventNonBlockingStatus = eventNonBlockingStatus;
    }

    public async Task Handle(Event<TEventPayload> ev, CancellationToken cancellationToken)
    {
        if (_eventNonBlockingStatus.IsBlocking)
        {
            using var scope = _serviceProvider.CreateScope();
            var eventSubscriber = scope.ServiceProvider.GetService<IEventSubscriber<TEventPayload, TEventSubscriber>>();
            if (eventSubscriber is null) { return; }
            await eventSubscriber.HandleEventAsync(ev);
        } else
        {
            await Task.CompletedTask;
            var thread = new Thread(
                () =>
                {
                    using var scope = _serviceProvider.CreateScope();
                    var eventSubscriber = scope.ServiceProvider.GetService<IEventSubscriber<TEventPayload, TEventSubscriber>>();
                    if (eventSubscriber is null) { return; }
                    eventSubscriber.HandleEventAsync(ev).Wait(cancellationToken);
                }) { IsBackground = true };
            thread.Start();
        }
    }
}
