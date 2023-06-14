using MediatR;
using Sekiban.Core.Events;
using Sekiban.Core.Query.UpdateNotice;
namespace Sekiban.Core.PubSub;

public class UpdateNoticeEventSubscriber<TEvent> : INotificationHandler<TEvent> where TEvent : IEvent
{
    private readonly IUpdateNotice _updateNotice;

    public UpdateNoticeEventSubscriber(IUpdateNotice updateNotice) => _updateNotice = updateNotice;

    public async Task Handle(TEvent notification, CancellationToken cancellationToken)
    {
        await Task.CompletedTask;
        _updateNotice.SendUpdate(
            notification.RootPartitionKey,
            notification.AggregateType,
            notification.AggregateId,
            notification.SortableUniqueId,
            UpdatedLocationType.Local);
    }
}
