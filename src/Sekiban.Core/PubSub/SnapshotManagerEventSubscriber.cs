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
    protected static readonly SemaphoreSlim SemaphoreInMemory = new(1, 1);

    private readonly IAggregateSettings _aggregateSettings;
    private readonly IDocumentPersistentRepository _documentPersistentRepository;
    private readonly IDocumentWriter _documentWriter;
    private readonly SekibanAggregateTypes _sekibanAggregateTypes;
    private readonly ISekibanContext _sekibanContext;
    private readonly IAggregateLoader aggregateLoader;
    private readonly ICommandExecutor commandExecutor;

    public SnapshotManagerEventSubscriber(
        SekibanAggregateTypes sekibanAggregateTypes,
        ICommandExecutor commandExecutor,
        IDocumentPersistentRepository documentPersistentRepository,
        IAggregateLoader aggregateLoader,
        IDocumentWriter documentWriter,
        IAggregateSettings aggregateSettings,
        ISekibanContext sekibanContext)
    {
        _sekibanAggregateTypes = sekibanAggregateTypes;
        this.commandExecutor = commandExecutor;
        _documentPersistentRepository = documentPersistentRepository;
        this.aggregateLoader = aggregateLoader;
        _documentWriter = documentWriter;
        _aggregateSettings = aggregateSettings;
        _sekibanContext = sekibanContext;
    }

    public async Task Handle(TEvent notification, CancellationToken cancellationToken)
    {
        var aggregateType =
            _sekibanAggregateTypes.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == notification.AggregateType);
        if (aggregateType is null)
        {
            return;
        }

        var aggregateContainerGroup =
            AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType.Aggregate);

        if (aggregateContainerGroup != AggregateContainerGroup.InMemoryContainer)
        {
            await SemaphoreInMemory.WaitAsync();

            var aggregate = await aggregateLoader.AsAggregateAsync<SnapshotManager>(SnapshotManager.SharedId);
            if (aggregate is null)
            {
                await commandExecutor.ExecCommandAsync(new CreateSnapshotManager());
            }
            SemaphoreInMemory.Release();

            if (_aggregateSettings.ShouldTakeSnapshotForType(aggregateType.Aggregate))
            {
                var snapshotManagerResponse
                    = await commandExecutor
                        .ExecCommandWithEventsAsync(
                            new ReportVersionToSnapshotManger(
                                SnapshotManager.SharedId,
                                aggregateType.Aggregate,
                                notification.AggregateId,
                                notification.Version,
                                null));
                if (snapshotManagerResponse.Events.Any(m => m.DocumentTypeName == nameof(SnapshotManagerSnapshotTaken)))
                {
                    foreach (var taken in snapshotManagerResponse.Events.Where(m => m.DocumentTypeName == nameof(SnapshotManagerSnapshotTaken))
                        .Select(m => (Event<SnapshotManagerSnapshotTaken>)m))
                    {

                        dynamic? awaitable = aggregateLoader.GetType()
                            ?.GetMethod(nameof(aggregateLoader.AsDefaultStateAsync))
                            ?.MakeGenericMethod(aggregateType.Aggregate)
                            .Invoke(
                                aggregateLoader,
                                new object?[] { notification.AggregateId, taken.Payload.NextSnapshotVersion, null });
                        if (awaitable is null)
                        {
                            continue;
                        }
                        var aggregateToSnapshot = await awaitable;
                        // var aggregateToSnapshot = await aggregateLoader.AsDefaultStateAsync<T, Q>(
                        // command.AggregateId,
                        // taken.NextSnapshotVersion);
                        if (aggregateToSnapshot is null)
                        {
                            continue;
                        }
                        if (await _documentPersistentRepository.ExistsSnapshotForAggregateAsync(
                            notification.AggregateId,
                            aggregateType.Aggregate,
                            aggregateType.Aggregate,
                            taken.Payload.NextSnapshotVersion,
                            aggregateToSnapshot.GetPayloadVersionIdentifier()))
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
                            aggregateType.Aggregate,
                            aggregateToSnapshot,
                            aggregateToSnapshot.LastEventId,
                            aggregateToSnapshot.LastSortableUniqueId,
                            aggregateToSnapshot.Version,
                            aggregateToSnapshot.GetPayloadVersionIdentifier());
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
                var snapshotManagerResponseP
                    = await commandExecutor
                        .ExecCommandWithEventsAsync(
                            new ReportVersionToSnapshotManger(
                                SnapshotManager.SharedId,
                                projection.PayloadType,
                                notification.AggregateId,
                                notification.Version,
                                null));
                if (snapshotManagerResponseP.Events.All(m => m.DocumentTypeName != nameof(SnapshotManagerSnapshotTaken)))
                {
                    continue;
                }

                foreach (var taken in snapshotManagerResponseP.Events.Where(m => m.DocumentTypeName == nameof(SnapshotManagerSnapshotTaken))
                    .Select(m => (Event<SnapshotManagerSnapshotTaken>)m))
                {
                    dynamic? awaitable = aggregateLoader.GetType()
                        ?.GetMethod(nameof(aggregateLoader.AsSingleProjectionStateAsync))
                        ?.MakeGenericMethod(projection.PayloadType)
                        .Invoke(
                            aggregateLoader,
                            new object?[] { notification.AggregateId, taken.Payload.NextSnapshotVersion, null });
                    if (awaitable is null)
                    {
                        continue;
                    }
                    var aggregateToSnapshot = await awaitable;

                    if (aggregateToSnapshot is null)
                    {
                        continue;
                    }
                    if (await _documentPersistentRepository.ExistsSnapshotForAggregateAsync(
                        notification.AggregateId,
                        projection.Aggregate,
                        projection.PayloadType,
                        taken.Payload.NextSnapshotVersion,
                        aggregateToSnapshot.GetPayloadVersionIdentifier()))
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
                        projection.PayloadType,
                        aggregateToSnapshot,
                        aggregateToSnapshot.LastEventId,
                        aggregateToSnapshot.LastSortableUniqueId,
                        aggregateToSnapshot.Version,
                        aggregateToSnapshot.GetPayloadVersionIdentifier());
                    await _documentWriter.SaveAsync(snapshotDocument, projection.Aggregate);
                }
            }
        }
    }
}
