using MediatR;
using Sekiban.EventSourcing.Queries.SingleAggregates;
using Sekiban.EventSourcing.Settings;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers.Commands;
using Sekiban.EventSourcing.Snapshots.SnapshotManagers.Events;
namespace Sekiban.EventSourcing.PubSubs;

public class SnapshotManagerEventSubscriber<TEvent> : INotificationHandler<TEvent> where TEvent : IAggregateEvent
{
    private readonly IAggregateCommandExecutor _aggregateCommandExecutor;
    private readonly IAggregateSettings _aggregateSettings;
    private readonly IDocumentPersistentRepository _documentPersistentRepository;
    private readonly IDocumentWriter _documentWriter;
    private readonly SekibanAggregateTypes _sekibanAggregateTypes;
    private readonly ISekibanContext _sekibanContext;
    private readonly ISingleAggregateService _singleAggregateService;
    public SnapshotManagerEventSubscriber(
        SekibanAggregateTypes sekibanAggregateTypes,
        IAggregateCommandExecutor aggregateCommandExecutor,
        IDocumentPersistentRepository documentPersistentRepository,
        ISingleAggregateService singleAggregateService,
        IDocumentWriter documentWriter,
        IAggregateSettings aggregateSettings,
        ISekibanContext sekibanContext)
    {
        _sekibanAggregateTypes = sekibanAggregateTypes;
        _aggregateCommandExecutor = aggregateCommandExecutor;
        _documentPersistentRepository = documentPersistentRepository;
        _singleAggregateService = singleAggregateService;
        _documentWriter = documentWriter;
        _aggregateSettings = aggregateSettings;
        _sekibanContext = sekibanContext;
    }

    public async Task Handle(TEvent notification, CancellationToken cancellationToken)
    {
        var aggregateType = _sekibanAggregateTypes.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == notification.AggregateType);
        if (aggregateType == null) { return; }

        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType.Aggregate);

        if (aggregateContainerGroup != AggregateContainerGroup.InMemoryContainer)
        {
            var aggregate = await _singleAggregateService.GetAggregateAsync<SnapshotManager, SnapshotManagerContents>(SnapshotManager.SharedId);
            if (aggregate == null)
            {
                await _aggregateCommandExecutor.ExecCreateCommandAsync<SnapshotManager, SnapshotManagerContents, CreateSnapshotManager>(
                    new CreateSnapshotManager(SnapshotManager.SharedId));
            }

            if (_aggregateSettings.ShouldTakeSnapshotForType(aggregateType.Aggregate))
            {
                var snapshotManagerResponse
                    = await _aggregateCommandExecutor
                        .ExecChangeCommandAsync<SnapshotManager, SnapshotManagerContents, ReportAggregateVersionToSnapshotManger>(
                            SnapshotManager.SharedId,
                            new ReportAggregateVersionToSnapshotManger(
                                aggregateType.Aggregate,
                                notification.AggregateId,
                                notification.Version,
                                null));
                if (snapshotManagerResponse.Events.Any(m => m.DocumentTypeName == nameof(SnapshotManagerSnapshotTaken)))
                {
                    foreach (var taken in snapshotManagerResponse.Events.Where(m => m.DocumentTypeName == nameof(SnapshotManagerSnapshotTaken))
                        .Select(m => (AggregateEvent<SnapshotManagerSnapshotTaken>)m))
                    {
                        if (await _documentPersistentRepository.ExistsSnapshotForAggregateAsync(
                            notification.AggregateId,
                            aggregateType.Aggregate,
                            taken.Payload.NextSnapshotVersion))
                        {
                            continue;
                        }
                        dynamic? awaitable = _singleAggregateService.GetType()
                            ?.GetMethod(nameof(_singleAggregateService.GetAggregateDtoAsync))
                            ?.MakeGenericMethod(aggregateType.Aggregate, aggregateType.Dto)
                            .Invoke(_singleAggregateService, new object[] { notification.AggregateId, taken.Payload.NextSnapshotVersion });
                        if (awaitable == null) { continue; }
                        var aggregateToSnapshot = await awaitable;
                        // var aggregateToSnapshot = await _singleAggregateService.GetAggregateDtoAsync<T, Q>(
                        // command.AggregateId,
                        // taken.NextSnapshotVersion);
                        if (aggregateToSnapshot == null)
                        {
                            continue;
                        }
                        if (taken.Payload.NextSnapshotVersion != aggregateToSnapshot.Version)
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

            foreach (var projection in _sekibanAggregateTypes.ProjectionAggregateTypes.Where(
                m => m.OriginalType.FullName == aggregateType.Aggregate.FullName))
            {
                if (!_aggregateSettings.ShouldTakeSnapshotForType(projection.Aggregate))
                {
                    continue;
                }
                var snapshotManagerResponseP
                    = await _aggregateCommandExecutor
                        .ExecChangeCommandAsync<SnapshotManager, SnapshotManagerContents, ReportAggregateVersionToSnapshotManger>(
                            SnapshotManager.SharedId,
                            new ReportAggregateVersionToSnapshotManger(projection.Aggregate, notification.AggregateId, notification.Version, null));
                if (snapshotManagerResponseP.Events.All(m => m.DocumentTypeName != nameof(SnapshotManagerSnapshotTaken)))
                {
                    continue;
                }

                foreach (var taken in snapshotManagerResponseP.Events.Where(m => m.DocumentTypeName == nameof(SnapshotManagerSnapshotTaken))
                    .Select(m => (AggregateEvent<SnapshotManagerSnapshotTaken>)m))
                {
                    if (await _documentPersistentRepository.ExistsSnapshotForAggregateAsync(
                        notification.AggregateId,
                        projection.Aggregate,
                        taken.Payload.NextSnapshotVersion))
                    {
                        continue;
                    }
                    dynamic? awaitable = _singleAggregateService.GetType()
                        ?.GetMethod(nameof(_singleAggregateService.GetProjectionAsync))
                        ?.MakeGenericMethod(projection.Aggregate)
                        .Invoke(_singleAggregateService, new object[] { notification.AggregateId, taken.Payload.NextSnapshotVersion });
                    if (awaitable == null) { continue; }
                    var aggregateToSnapshot = await awaitable;

                    if (aggregateToSnapshot == null)
                    {
                        continue;
                    }
                    if (taken.Payload.NextSnapshotVersion != aggregateToSnapshot.Version)
                    {
                        continue;
                    }
                    var snapshotDocument = new SnapshotDocument(
                        new AggregateIdPartitionKeyFactory(notification.AggregateId, projection.Aggregate),
                        projection.Aggregate.Name,
                        aggregateToSnapshot,
                        notification.AggregateId,
                        aggregateToSnapshot.LastEventId,
                        aggregateToSnapshot.LastSortableUniqueId,
                        aggregateToSnapshot.Version);
                    await _documentWriter.SaveAsync(snapshotDocument, projection.Aggregate);
                }
            }
        }
    }
}
