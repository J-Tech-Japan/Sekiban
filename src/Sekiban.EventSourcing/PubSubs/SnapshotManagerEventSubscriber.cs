using MediatR;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers.Commands;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers.Events;
namespace Sekiban.EventSourcing.PubSubs;

public class SnapshotManagerEventSubscriber<TEvent> : INotificationHandler<TEvent> where TEvent : AggregateEvent
{
    private readonly IAggregateCommandExecutor _aggregateCommandExecutor;
    private readonly IDocumentPersistentRepository _documentPersistentRepository;
    private readonly IDocumentWriter _documentWriter;
    private readonly RegisteredEventTypes _registeredEventTypes;
    private readonly SekibanAggregateTypes _sekibanAggregateTypes;
    private readonly SingleAggregateService _singleAggregateService;
    public SnapshotManagerEventSubscriber(
        RegisteredEventTypes registeredEventTypes,
        SekibanAggregateTypes sekibanAggregateTypes,
        IAggregateCommandExecutor aggregateCommandExecutor,
        IDocumentPersistentRepository documentPersistentRepository,
        SingleAggregateService singleAggregateService,
        IDocumentWriter documentWriter)
    {
        _registeredEventTypes = registeredEventTypes;
        _sekibanAggregateTypes = sekibanAggregateTypes;
        _aggregateCommandExecutor = aggregateCommandExecutor;
        _documentPersistentRepository = documentPersistentRepository;
        _singleAggregateService = singleAggregateService;
        _documentWriter = documentWriter;
    }

    public async Task Handle(TEvent notification, CancellationToken cancellationToken)
    {
        var aggregateType = _sekibanAggregateTypes.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == notification.AggregateType);
        if (aggregateType == null) { return; }

        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType.Aggregate);

        if (aggregateContainerGroup != AggregateContainerGroup.InMemoryContainer)
        {
            var snapshotManagerResponse
                = await _aggregateCommandExecutor.ExecChangeCommandAsync<SnapshotManager, SnapshotManagerDto, ReportAggregateVersionToSnapshotManger>(
                    new ReportAggregateVersionToSnapshotManger(
                        SnapshotManager.SharedId,
                        aggregateType.Aggregate,
                        notification.AggregateId,
                        notification.Version,
                        null));
            if (snapshotManagerResponse.Events.Any(m => m.DocumentTypeName == nameof(SnapshotManagerSnapshotTaken)))
            {
                foreach (var taken in snapshotManagerResponse.Events.Where(m => m.DocumentTypeName == nameof(SnapshotManagerSnapshotTaken))
                    .Select(m => (SnapshotManagerSnapshotTaken)m))
                {
                    if (!await _documentPersistentRepository.ExistsSnapshotForAggregateAsync(
                        notification.AggregateId,
                        aggregateType.Aggregate,
                        taken.NextSnapshotVersion))
                    {
                        Console.WriteLine($"creating snapshot - {taken.NextSnapshotVersion}");

                        dynamic? awaitable = _singleAggregateService.GetType()
                            ?.GetMethod(nameof(_singleAggregateService.GetAggregateDtoAsync))
                            ?.MakeGenericMethod(aggregateType.Aggregate, aggregateType.Dto)
                            .Invoke(_singleAggregateService, new object[] { notification.AggregateId, taken.NextSnapshotVersion });
                        if (awaitable == null) { continue; }
                        var aggregateToSnapshot = await awaitable;
                        // var aggregateToSnapshot = await _singleAggregateService.GetAggregateDtoAsync<T, Q>(
                        // command.AggregateId,
                        // taken.NextSnapshotVersion);
                        if (aggregateToSnapshot == null)
                        {
                            continue;
                        }
                        if (taken.NextSnapshotVersion != aggregateToSnapshot.Version)
                        {
                            continue;
                        }
                        var snapshotDocument = new SnapshotDocument(
                            new AggregateIdPartitionKeyFactory(notification.AggregateId, aggregateType.Aggregate),
                            aggregateType.Aggregate.Name,
                            aggregateToSnapshot,
                            notification.AggregateId,
                            aggregateToSnapshot.LastEventId,
                            aggregateToSnapshot.LastSortableUniqueId,
                            aggregateToSnapshot.Version);
                        await _documentWriter.SaveAsync(snapshotDocument, aggregateType.Aggregate);
                    }
                }
            }
        }
    }
}
