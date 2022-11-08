using MediatR;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Document;
using Sekiban.Core.Event;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot;
using Sekiban.Core.Snapshot.Aggregate;
using Sekiban.Core.Snapshot.Aggregate.Commands;
using Sekiban.Core.Snapshot.Aggregate.Events;
namespace Sekiban.Core.PubSub;

public class SnapshotManagerEventSubscriber<TEvent> : INotificationHandler<TEvent> where TEvent : IEvent
{
    private static readonly SemaphoreSlim _semaphoreInMemory = new(1, 1);

    private readonly IAggregateSettings _aggregateSettings;
    private readonly IDocumentPersistentRepository _documentPersistentRepository;
    private readonly IDocumentWriter _documentWriter;
    private readonly SekibanAggregateTypes _sekibanAggregateTypes;
    private readonly ISekibanContext _sekibanContext;
    private readonly ICommandExecutor commandExecutor;
    private readonly ISingleProjectionService singleProjectionService;
    public SnapshotManagerEventSubscriber(
        SekibanAggregateTypes sekibanAggregateTypes,
        ICommandExecutor commandExecutor,
        IDocumentPersistentRepository documentPersistentRepository,
        ISingleProjectionService singleProjectionService,
        IDocumentWriter documentWriter,
        IAggregateSettings aggregateSettings,
        ISekibanContext sekibanContext)
    {
        _sekibanAggregateTypes = sekibanAggregateTypes;
        this.commandExecutor = commandExecutor;
        _documentPersistentRepository = documentPersistentRepository;
        this.singleProjectionService = singleProjectionService;
        _documentWriter = documentWriter;
        _aggregateSettings = aggregateSettings;
        _sekibanContext = sekibanContext;
    }

    public async Task Handle(TEvent notification, CancellationToken cancellationToken)
    {
        var aggregateType = _sekibanAggregateTypes.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == notification.AggregateType);
        if (aggregateType is null) { return; }

        var aggregateContainerGroup = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType.Aggregate);

        if (aggregateContainerGroup != AggregateContainerGroup.InMemoryContainer)
        {
            await _semaphoreInMemory.WaitAsync();

            var aggregate = await singleProjectionService.GetAggregateAsync<SnapshotManager>(SnapshotManager.SharedId);
            if (aggregate is null)
            {
                await commandExecutor.ExecCreateCommandAsync<SnapshotManager, CreateSnapshotManager>(
                    new CreateSnapshotManager());
            }
            _semaphoreInMemory.Release();

            if (_aggregateSettings.ShouldTakeSnapshotForType(aggregateType.Aggregate))
            {
                var (snapshotManagerResponse, events)
                    = await commandExecutor
                        .ExecChangeCommandAsync<SnapshotManager, ReportVersionToSnapshotManger>(
                            new ReportVersionToSnapshotManger(
                                SnapshotManager.SharedId,
                                aggregateType.Aggregate,
                                notification.AggregateId,
                                notification.Version,
                                null));
                if (events.Any(m => m.DocumentTypeName == nameof(SnapshotManagerSnapshotTaken)))
                {
                    foreach (var taken in events.Where(m => m.DocumentTypeName == nameof(SnapshotManagerSnapshotTaken))
                        .Select(m => (Event<SnapshotManagerSnapshotTaken>)m))
                    {
                        if (await _documentPersistentRepository.ExistsSnapshotForAggregateAsync(
                            notification.AggregateId,
                            aggregateType.Aggregate,
                            taken.Payload.NextSnapshotVersion))
                        {
                            continue;
                        }
                        dynamic? awaitable = singleProjectionService.GetType()
                            ?.GetMethod(nameof(singleProjectionService.GetAggregateStateAsync))
                            ?.MakeGenericMethod(aggregateType.Aggregate)
                            .Invoke(singleProjectionService, new object[] { notification.AggregateId, taken.Payload.NextSnapshotVersion });
                        if (awaitable is null) { continue; }
                        var aggregateToSnapshot = await awaitable;
                        // var aggregateToSnapshot = await singleProjectionService.GetAggregateStateAsync<T, Q>(
                        // command.AggregateId,
                        // taken.NextSnapshotVersion);
                        if (aggregateToSnapshot is null)
                        {
                            continue;
                        }
                        if (taken.Payload.NextSnapshotVersion != aggregateToSnapshot.Version)
                        {
                            continue;
                        }
                        var snapshotDocument = new SnapshotDocument(
                            notification.AggregateId,
                            aggregateType.Aggregate,
                            aggregateToSnapshot,
                            aggregateToSnapshot.LastEventId,
                            aggregateToSnapshot.LastSortableUniqueId,
                            aggregateToSnapshot.Version);
                        await _documentWriter.SaveAsync(snapshotDocument, aggregateType.Aggregate);
                    }
                }
            }

            foreach (var projection in _sekibanAggregateTypes.SingleProjectionTypes.Where(
                m => m.Aggregate.FullName == aggregateType.Aggregate.FullName))
            {
                if (!_aggregateSettings.ShouldTakeSnapshotForType(projection.Aggregate))
                {
                    continue;
                }
                var (snapshotManagerResponseP, eventsP)
                    = await commandExecutor
                        .ExecChangeCommandAsync<SnapshotManager, ReportVersionToSnapshotManger>(
                            new ReportVersionToSnapshotManger(
                                SnapshotManager.SharedId,
                                projection.Aggregate,
                                notification.AggregateId,
                                notification.Version,
                                null));
                if (eventsP.All(m => m.DocumentTypeName != nameof(SnapshotManagerSnapshotTaken)))
                {
                    continue;
                }

                foreach (var taken in eventsP.Where(m => m.DocumentTypeName == nameof(SnapshotManagerSnapshotTaken))
                    .Select(m => (Event<SnapshotManagerSnapshotTaken>)m))
                {
                    if (await _documentPersistentRepository.ExistsSnapshotForAggregateAsync(
                        notification.AggregateId,
                        projection.Aggregate,
                        taken.Payload.NextSnapshotVersion))
                    {
                        continue;
                    }
                    dynamic? awaitable = singleProjectionService.GetType()
                        ?.GetMethod(nameof(singleProjectionService.GetProjectionAsync))
                        ?.MakeGenericMethod(projection.Aggregate, projection.Projection, projection.PayloadType)
                        .Invoke(singleProjectionService, new object[] { notification.AggregateId, taken.Payload.NextSnapshotVersion });
                    if (awaitable is null) { continue; }
                    var aggregateToSnapshot = await awaitable;

                    if (aggregateToSnapshot is null)
                    {
                        continue;
                    }
                    if (taken.Payload.NextSnapshotVersion != aggregateToSnapshot.Version)
                    {
                        continue;
                    }
                    var snapshotDocument = new SnapshotDocument(
                        notification.AggregateId,
                        projection.Aggregate,
                        aggregateToSnapshot,
                        aggregateToSnapshot.LastEventId,
                        aggregateToSnapshot.LastSortableUniqueId,
                        aggregateToSnapshot.Version);
                    await _documentWriter.SaveAsync(snapshotDocument, projection.Aggregate);
                }
            }
        }
    }
}
