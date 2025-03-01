using Sekiban.Core.Aggregate;
using Sekiban.Core.Cache;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.UpdateNotice;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Core.Types;
namespace Sekiban.Core.Query.SingleProjections.Projections;

/// <summary>
///     Single projection implementation with memory cache.
/// </summary>
public class MemoryCacheSingleProjection(
    EventRepository eventRepository,
    IDocumentRepository documentRepository,
    IUpdateNotice updateNotice,
    IAggregateSettings aggregateSettings,
    ISingleProjectionCache singleProjectionCache,
    ISekibanDateProducer dateProducer) : ISingleProjection
{

    public async Task<TProjection?> GetAggregateAsync<TProjection, TState, TProjector>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        SingleProjectionRetrievalOptions? retrievalOptions = null)
        where TProjection : IAggregateCommon, SingleProjections.ISingleProjection,
        ISingleProjectionStateConvertible<TState>
        where TState : IAggregateStateCommon
        where TProjector : ISingleProjector<TProjection>, new()
    {
        var savedContainer = singleProjectionCache.GetContainer<TProjection, TState>(aggregateId);
        if (savedContainer == null)
        {
            return await GetAggregateWithoutCacheAsync<TProjection, TState, TProjector>(
                aggregateId,
                rootPartitionKey,
                toVersion);
        }
        var projector = new TProjector();
        if (savedContainer.SafeState is null && savedContainer.SafeSortableUniqueId?.Value is null)
        {
            return await GetAggregateWithoutCacheAsync<TProjection, TState, TProjector>(
                aggregateId,
                rootPartitionKey,
                toVersion);
        }
        var aggregate = projector.CreateInitialAggregate(aggregateId);
        if (savedContainer.SafeState is not null && aggregate.CanApplySnapshot(savedContainer.SafeState))
        {
            aggregate.ApplySnapshot(savedContainer.SafeState);
        }
        if (retrievalOptions?.IncludesSortableUniqueIdValue is not null &&
            savedContainer.SafeSortableUniqueId is not null &&
            retrievalOptions.IncludesSortableUniqueIdValue.IsEarlierThan(savedContainer.SafeSortableUniqueId))
        {
            return aggregate;
        }

        if (aggregateSettings.UseUpdateMarkerForType(projector.GetOriginalAggregatePayloadType().Name))
        {
            var (updated, _) = updateNotice.HasUpdateAfter(
                rootPartitionKey,
                projector.GetOriginalAggregatePayloadType().Name,
                aggregateId,
                savedContainer.SafeSortableUniqueId!);
            if (!updated)
            {
                return aggregate;
            }
        }
        if (retrievalOptions is not null && !retrievalOptions.RetrieveNewEvents)
        {
            return aggregate;
        }
        if (retrievalOptions?.PostponeEventFetchBySeconds is not null &&
            retrievalOptions.ShouldPostponeFetch(savedContainer.CachedAt, dateProducer.UtcNow))
        {
            return aggregate;
        }
        var container = new SingleMemoryCacheProjectionContainer<TProjection, TState> { AggregateId = aggregateId };

        try
        {
            await eventRepository.GetEvents(
                EventRetrievalInfo.FromNullableValues(
                    rootPartitionKey,
                    new AggregateTypeStream(projector.GetOriginalAggregatePayloadType()),
                    aggregateId,
                    ISortableIdCondition.FromMemoryCacheContainer(savedContainer)),
                events =>
                {
                    var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                    foreach (var e in events)
                    {
                        if (!string.IsNullOrWhiteSpace(savedContainer?.SafeSortableUniqueId?.Value) &&
                            string.CompareOrdinal(savedContainer.SafeSortableUniqueId?.Value, e.SortableUniqueId) > 0)
                        {
                            throw new SekibanEventDuplicateException();
                        }
                        if (container.LastSortableUniqueId == null &&
                            e.GetSortableUniqueId().IsLaterThanOrEqual(targetSafeId) &&
                            aggregate.Version > 0)
                        {
                            container = container with
                            {
                                SafeState = aggregate.ToState(),
                                SafeSortableUniqueId = container.SafeState?.LastSortableUniqueId != null
                                    ? new SortableUniqueIdValue(container.SafeState.LastSortableUniqueId)
                                    : null
                            };
                        }
                        if (!aggregate.EventShouldBeApplied(e)) { throw new SekibanEventOrderMixedUpException(); }
                        aggregate.ApplyEvent(e);
                        container = container with { LastSortableUniqueId = e.SortableUniqueId };
                        if (e.GetSortableUniqueId().IsLaterThanOrEqual(targetSafeId))
                        {
                            container.UnsafeEvents.Add(e);
                        }
                        if (toVersion.HasValue && aggregate.Version == toVersion.Value)
                        {
                            break;
                        }
                    }
                });
        }
        catch (SekibanEventOrderMixedUpException)
        {
            return await GetAggregateWithoutCacheAsync<TProjection, TState, TProjector>(
                aggregateId,
                rootPartitionKey,
                toVersion);
        }
        if (aggregate.Version == 0)
        {
            return default;
        }
        if (toVersion.HasValue && aggregate.Version < toVersion.Value)
        {
            throw new SekibanVersionNotReachToSpecificVersion();
        }
        if (aggregate.IsAggregateType() &&
            !aggregate.GetPayloadTypeIs(aggregate.GetAggregatePayloadTypeFromAggregate()))
        {
            return aggregate;
        }
        container = container with { State = aggregate.ToState(), CachedAt = dateProducer.UtcNow };
        if (container.LastSortableUniqueId != null &&
            container.SafeSortableUniqueId == null &&
            container.LastSortableUniqueId?.IsEarlierThan(SortableUniqueIdValue.GetSafeIdFromUtc()) == true)
        {
            container = container with
            {
                SafeState = container.State, SafeSortableUniqueId = container.LastSortableUniqueId
            };
        }

        if (container.SafeState is not null)
        {
            singleProjectionCache.SetContainer(aggregateId, container);
        }
        return aggregate;
    }

    private async Task<TProjection?> GetAggregateWithoutCacheAsync<TProjection, TState, TProjector>(
        Guid aggregateId,
        string rootPartitionKey,
        int? toVersion = null)
        where TProjection : IAggregateCommon, SingleProjections.ISingleProjection,
        ISingleProjectionStateConvertible<TState>
        where TState : IAggregateStateCommon
        where TProjector : ISingleProjector<TProjection>, new()
    {
        var projector = new TProjector();
        var aggregate = projector.CreateInitialAggregate(aggregateId);
        var container = new SingleMemoryCacheProjectionContainer<TProjection, TState> { AggregateId = aggregateId };
        var payloadVersion = projector.GetPayloadVersionIdentifier();
        var snapshotDocument = await documentRepository.GetLatestSnapshotForAggregateAsync(
            aggregateId,
            projector.GetOriginalAggregatePayloadType(),
            projector.GetPayloadType(),
            rootPartitionKey,
            payloadVersion);
        var state = snapshotDocument?.GetState();
        if (state is not null && aggregate.CanApplySnapshot(state))
        {
            aggregate.ApplySnapshot(state);
        }
        if (toVersion.HasValue && aggregate.Version >= toVersion.Value)
        {
            return await GetAggregateFromInitialAsync<TProjection, TState, TProjector>(
                aggregateId,
                rootPartitionKey,
                toVersion.Value);
        }
        await eventRepository.GetEvents(
            EventRetrievalInfo.FromNullableValues(
                rootPartitionKey,
                new AggregateTypeStream(projector.GetOriginalAggregatePayloadType()),
                aggregateId,
                ISortableIdCondition.FromState(state)),
            events =>
            {
                var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                foreach (var e in events)
                {
                    if (!string.IsNullOrWhiteSpace(state?.LastSortableUniqueId) &&
                        string.CompareOrdinal(state.LastSortableUniqueId, e.SortableUniqueId) > 0)
                    {
                        throw new SekibanEventDuplicateException();
                    }
                    if (container.LastSortableUniqueId == null &&
                        e.GetSortableUniqueId().IsEarlierThan(targetSafeId) &&
                        aggregate.Version > 0)
                    {
                        container = container with
                        {
                            SafeState = aggregate.ToState(),
                            SafeSortableUniqueId = container.SafeState?.LastSortableUniqueId != null
                                ? new SortableUniqueIdValue(container.SafeState.LastSortableUniqueId)
                                : null
                        };
                    }

                    aggregate.ApplyEvent(e);
                    container = container with { LastSortableUniqueId = e.GetSortableUniqueId() };
                    if (e.GetSortableUniqueId().IsLaterThanOrEqual(targetSafeId))
                    {
                        container.UnsafeEvents.Add(e);
                    }
                    if (toVersion.HasValue && aggregate.Version == toVersion.Value)
                    {
                        break;
                    }
                }
            });
        if (aggregate.Version == 0)
        {
            return default;
        }
        if (toVersion.HasValue && aggregate.Version < toVersion.Value)
        {
            throw new SekibanVersionNotReachToSpecificVersion();
        }
        if (aggregate.IsAggregateType() &&
            !aggregate.GetPayloadTypeIs(aggregate.GetAggregatePayloadTypeFromAggregate()))
        {
            return aggregate;
        }
        container = container with { State = aggregate.ToState() };
        if (container.LastSortableUniqueId != null &&
            container.SafeSortableUniqueId == null &&
            container.LastSortableUniqueId?.IsEarlierThan(SortableUniqueIdValue.GetSafeIdFromUtc()) == true)
        {
            container = container with
            {
                SafeState = container.State, SafeSortableUniqueId = container.LastSortableUniqueId
            };
        }

        if (container.SafeState is not null)
        {
            singleProjectionCache.SetContainer(aggregateId, container);
        }
        return aggregate;
    }

    public async Task<TProjection?> GetAggregateFromInitialAsync<TProjection, TState, TProjector>(
        Guid aggregateId,
        string rootPartitionKey,
        int? toVersion)
        where TProjection : IAggregateCommon, SingleProjections.ISingleProjection,
        ISingleProjectionStateConvertible<TState>
        where TState : IAggregateStateCommon
        where TProjector : ISingleProjector<TProjection>, new()
    {
        var projector = new TProjector();
        var container = new SingleMemoryCacheProjectionContainer<TProjection, TState> { AggregateId = aggregateId };
        var aggregate = projector.CreateInitialAggregate(aggregateId);
        var addFinished = false;

        await eventRepository.GetEvents(
            EventRetrievalInfo.FromNullableValues(
                rootPartitionKey,
                new AggregateTypeStream(projector.GetOriginalAggregatePayloadType()),
                aggregateId,
                ISortableIdCondition.None),
            events =>
            {
                events = events.ToList();
                if (events.Count() != events.Select(m => m.Id).Distinct().Count())
                {
                    throw new SekibanEventDuplicateException();
                }
                if (addFinished)
                {
                    return;
                }
                var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                foreach (var e in events)
                {
                    if (container.LastSortableUniqueId == null &&
                        e.GetSortableUniqueId().IsEarlierThan(targetSafeId) &&
                        aggregate.Version > 0)
                    {
                        container = container with
                        {
                            SafeState = aggregate.ToState(),
                            SafeSortableUniqueId = container.SafeState?.LastSortableUniqueId != null
                                ? new SortableUniqueIdValue(container.SafeState.LastSortableUniqueId)
                                : null
                        };
                    }

                    aggregate.ApplyEvent(e);
                    container = container with { LastSortableUniqueId = e.GetSortableUniqueId() };
                    if (e.GetSortableUniqueId().IsLaterThanOrEqual(targetSafeId))
                    {
                        container.UnsafeEvents.Add(e);
                    }
                    if (toVersion.HasValue && toVersion.Value == aggregate.Version)
                    {
                        addFinished = true;
                        break;
                    }
                }
            });
        if (aggregate.Version == 0)
        {
            return default;
        }

        container = container with { State = aggregate.ToState() };
        if (container.LastSortableUniqueId != null &&
            container.SafeSortableUniqueId == null &&
            container.LastSortableUniqueId?.IsEarlierThan(SortableUniqueIdValue.GetSafeIdFromUtc()) == true)
        {
            container = container with
            {
                SafeState = container.State, SafeSortableUniqueId = container.LastSortableUniqueId
            };
        }

        if (container.SafeState is not null)
        {
            singleProjectionCache.SetContainer(aggregateId, container);
        }
        return aggregate;
    }
}
