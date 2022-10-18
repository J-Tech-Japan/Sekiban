using MediatR;
using Sekiban.Core.Event;
using Sekiban.Core.Query.UpdateNotice;
namespace Sekiban.Core.PubSub;

public class UpdateNoticeEventSubscriber<TEvent> : INotificationHandler<TEvent> where TEvent : IAggregateEvent
{
    private readonly IUpdateNotice _updateNotice;

    public UpdateNoticeEventSubscriber(IUpdateNotice updateNotice)
    {
        _updateNotice = updateNotice;
    }
    public async Task Handle(TEvent notification, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        _updateNotice.SendUpdate(notification.AggregateType, notification.AggregateId, notification.SortableUniqueId, UpdatedLocationType.Local);
    }
}
