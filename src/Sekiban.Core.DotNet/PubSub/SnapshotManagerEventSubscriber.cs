using MediatR;
using Sekiban.Core.Events;
using Sekiban.Core.Snapshot.BackgroundServices;
namespace Sekiban.Core.PubSub;

/// <summary>
///     This class is not registered but it will called from MediatR.
/// </summary>
/// <typeparam name="TEvent"></typeparam>
public class SnapshotManagerEventSubscriber<TEvent> : INotificationHandler<TEvent> where TEvent : IEvent
{
    private readonly SnapshotTakingBackgroundService _snapshotTakingBackgroundService;


    public SnapshotManagerEventSubscriber(SnapshotTakingBackgroundService snapshotTakingBackgroundService) =>
        _snapshotTakingBackgroundService = snapshotTakingBackgroundService;

    public async Task Handle(TEvent notification, CancellationToken cancellationToken)
    {
        _snapshotTakingBackgroundService.EnqueueTask(notification);
        await Task.CompletedTask;
    }
}
