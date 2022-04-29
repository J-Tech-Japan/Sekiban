using MediatR;
using Sekiban.EventSourcing.AggregateEvents;

namespace Sekiban.EventSourcing.PubSubs;

public abstract class AggregateEventSubscriberBase<TEvent> : INotificationHandler<TEvent>
    where TEvent : AggregateEvent
{
    public async Task Handle(
        TEvent ev,
        CancellationToken cancellationToken)
    {
        // TODO: 下記行が必要かどうか確認する。
        //_aggregateQueryStore.ReceiveEvent(notification.AggregateEvent);

        await SubscribeAggregateEventAsync(ev);
    }

    public abstract Task SubscribeAggregateEventAsync(TEvent ev);
}
