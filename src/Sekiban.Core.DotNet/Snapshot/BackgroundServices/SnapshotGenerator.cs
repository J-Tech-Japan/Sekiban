using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Events;
using Sekiban.Core.Query.SingleProjections;
using Sekiban.Core.Query.SingleProjections.Projections;
using Sekiban.Core.Setting;
using Sekiban.Core.Snapshot.Aggregate;
using Sekiban.Core.Snapshot.Aggregate.Commands;
using Sekiban.Core.Snapshot.Aggregate.Events;
namespace Sekiban.Core.Snapshot.BackgroundServices;

/// <summary>
///     Single projection snapshot generator
/// </summary>
public class SnapshotGenerator(
    SekibanAggregateTypes sekibanAggregateTypes,
    ICommandExecutor commandExecutor,
    IDocumentPersistentRepository documentPersistentRepository,
    IAggregateLoader aggregateLoader,
    IDocumentWriter documentWriter,
    IAggregateSettings aggregateSettings,
    ISingleProjectionSnapshotAccessor singleProjectionSnapshotAccessor)
{
    public static readonly SemaphoreSlim SemaphoreInMemory = new(1, 1);

    public async Task Generate<TEvent>(TEvent notification) where TEvent : IEvent
    {
        var aggregateType
            = sekibanAggregateTypes.AggregateTypes.FirstOrDefault(m => m.Aggregate.Name == notification.AggregateType);
        if (aggregateType is null)
        {
            return;
        }

        var aggregateContainerGroup
            = AggregateContainerGroupAttribute.FindAggregateContainerGroup(aggregateType.Aggregate);

        if (aggregateContainerGroup != AggregateContainerGroup.InMemory)
        {
            await SemaphoreInMemory.WaitAsync();

            var aggregate = await aggregateLoader.AsAggregateAsync<SnapshotManager>(SnapshotManager.SharedId);
            if (aggregate is null)
            {
                await commandExecutor.ExecCommandAsync(new CreateSnapshotManager());
            }
            SemaphoreInMemory.Release();

            if (aggregateSettings.ShouldTakeSnapshotForType(aggregateType.Aggregate))
            {
                var snapshotManagerResponse = await commandExecutor.ExecCommandWithEventsAsync(
                    new ReportVersionToSnapshotManger(
                        SnapshotManager.SharedId,
                        aggregateType.Aggregate,
                        notification.AggregateId,
                        notification.Version,
                        null));
                if (snapshotManagerResponse.Events.Any(m => m.DocumentTypeName == nameof(SnapshotManagerSnapshotTaken)))
                {
                    foreach (var taken in snapshotManagerResponse
                        .Events
                        .Where(m => m.DocumentTypeName == nameof(SnapshotManagerSnapshotTaken))
                        .Select(m => (Event<SnapshotManagerSnapshotTaken>)m))
                    {

                        dynamic? awaitable = aggregateLoader
                            .GetType()
                            .GetMethod(nameof(aggregateLoader.AsDefaultStateAsync))
                            ?.MakeGenericMethod(aggregateType.Aggregate)
                            .Invoke(
                                aggregateLoader,
                                [
                                    notification.AggregateId, notification.RootPartitionKey,
                                    taken.Payload.NextSnapshotVersion, null
                                ]);
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
                        if (await documentPersistentRepository.ExistsSnapshotForAggregateAsync(
                            notification.AggregateId,
                            aggregateType.Aggregate,
                            aggregateType.Aggregate,
                            taken.Payload.NextSnapshotVersion,
                            taken.RootPartitionKey,
                            aggregateToSnapshot.GetPayloadVersionIdentifier()))
                        {
                            continue;
                        }
                        if (taken.Payload.NextSnapshotVersion != aggregateToSnapshot.Version)
                        {
                            continue;
                        }

                        var snapshotDocument
                            = await singleProjectionSnapshotAccessor.SnapshotDocumentFromAggregateStateAsync(
                                aggregateToSnapshot);
                        // var snapshotDocument = new SnapshotDocument(
                        //     notification.AggregateId,
                        //     aggregateType.Aggregate,
                        //     aggregateType.Aggregate,
                        //     aggregateToSnapshot,
                        //     aggregateToSnapshot.LastEventId,
                        //     aggregateToSnapshot.LastSortableUniqueId,
                        //     aggregateToSnapshot.Version,
                        //     aggregateToSnapshot.GetPayloadVersionIdentifier());
                        await documentWriter.SaveItemAsync(
                            snapshotDocument,
                            new AggregateWriteStream(aggregateType.Aggregate));
                    }
                }
            }

            foreach (var projection in sekibanAggregateTypes.SingleProjectionTypes.Where(
                m => m.OriginalAggregate.FullName == aggregateType.Aggregate.FullName))
            {
                if (!aggregateSettings.ShouldTakeSnapshotForType(projection.OriginalAggregate))
                {
                    continue;
                }
                var snapshotManagerResponseP = await commandExecutor.ExecCommandWithEventsAsync(
                    new ReportVersionToSnapshotManger(
                        SnapshotManager.SharedId,
                        projection.SingleProjectionPayloadType,
                        notification.AggregateId,
                        notification.Version,
                        null));
                if (snapshotManagerResponseP.Events.All(
                    m => m.DocumentTypeName != nameof(SnapshotManagerSnapshotTaken)))
                {
                    continue;
                }

                foreach (var taken in snapshotManagerResponseP
                    .Events
                    .Where(m => m.DocumentTypeName == nameof(SnapshotManagerSnapshotTaken))
                    .Select(m => (Event<SnapshotManagerSnapshotTaken>)m))
                {
                    dynamic? awaitable = aggregateLoader
                        .GetType()
                        .GetMethod(nameof(aggregateLoader.AsSingleProjectionStateAsync))
                        ?.MakeGenericMethod(projection.SingleProjectionPayloadType)
                        .Invoke(
                            aggregateLoader,
                            [
                                notification.AggregateId, notification.RootPartitionKey,
                                taken.Payload.NextSnapshotVersion, null
                            ]);
                    if (awaitable is null)
                    {
                        continue;
                    }
                    var aggregateToSnapshot = await awaitable;

                    if (aggregateToSnapshot is null)
                    {
                        continue;
                    }
                    if (await documentPersistentRepository.ExistsSnapshotForAggregateAsync(
                        notification.AggregateId,
                        projection.OriginalAggregate,
                        projection.SingleProjectionPayloadType,
                        taken.Payload.NextSnapshotVersion,
                        taken.RootPartitionKey,
                        aggregateToSnapshot.GetPayloadVersionIdentifier()))
                    {
                        continue;
                    }
                    if (taken.Payload.NextSnapshotVersion != aggregateToSnapshot.Version)
                    {
                        continue;
                    }
                    var snapshotDocument
                        = await singleProjectionSnapshotAccessor.SnapshotDocumentFromSingleProjectionStateAsync(
                            aggregateToSnapshot,
                            projection.OriginalAggregate);
                    await documentWriter.SaveItemAsync(
                        snapshotDocument,
                        new AggregateWriteStream(projection.OriginalAggregate));
                }
            }
        }
    }
}
