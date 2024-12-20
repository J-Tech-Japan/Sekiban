using Sekiban.Core.Cache;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.Pools;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.UpdateNotice;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
namespace Sekiban.Core.Query.MultiProjections.Projections;

/// <summary>
///     Multi Projection using Memory Cache
/// </summary>
public class MemoryCacheMultiProjection(
    EventRepository documentRepository,
    IUpdateNotice updateNotice,
    IAggregateSettings aggregateSettings,
    IMultiProjectionCache multiProjectionCache,
    RegisteredEventTypes registeredEventTypes,
    IMultiProjectionSnapshotGenerator multiProjectionSnapshotGenerator,
    ISekibanDateProducer dateProducer) : IMultiProjection
{
    public async Task<MultiProjectionState<TProjectionPayload>>
        GetInitialMultiProjectionFromStreamAsync<TProjection, TProjectionPayload>(
            Stream stream,
            string rootPartitionKey,
            MultiProjectionRetrievalOptions? retrievalOptions)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon
    {
        await Task.CompletedTask;
        var list = JsonSerializer.Deserialize<List<JsonElement>>(stream) ??
            throw new SekibanSerializerException("Could not deserialize file");
        var events = (IList<IEvent>)
        [
            .. list
                .Select(m => SekibanJsonHelper.DeserializeToEvent(m, registeredEventTypes.RegisteredTypes))
                .Where(m => m is not null)
                .Cast<IEvent>()
                .OrderBy(m => m is null ? string.Empty : m.SortableUniqueId)
                .ToList()
        ];
        var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
        var safeEvents = events.Where(m => m.GetSortableUniqueId().IsEarlierThan(targetSafeId)).ToList();
        var unsafeEvents = events.Where(m => m.GetSortableUniqueId().IsLaterThanOrEqual(targetSafeId)).ToList();
        var safeState = safeEvents.Aggregate(
            new MultiProjectionState<TProjectionPayload>(),
            (projection, ev) => projection.ApplyEvent(ev));
        var state = unsafeEvents.Aggregate(safeState, (projection, ev) => projection.ApplyEvent(ev));
        var container = new MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>
        {
            UnsafeEvents = unsafeEvents,
            LastSortableUniqueId = events.LastOrDefault()?.GetSortableUniqueId(),
            SafeSortableUniqueId = safeEvents.LastOrDefault()?.GetSortableUniqueId(),
            SafeState = safeEvents.Count == 0 ? null : safeState,
            State = state
        };

        if (container.SafeState is not null)
        {
            multiProjectionCache.Set(rootPartitionKey, container);
        }

        return container.State;
    }
    public async Task<MultiProjectionState<TProjectionPayload>>
        GetMultiProjectionFromMultipleStreamAsync<TProjection, TProjectionPayload>(
            Func<Task<Stream?>> stream,
            string rootPartitionKey,
            MultiProjectionRetrievalOptions? retrievalOptions)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon
    {
        var eventStream = await stream();
        var multiProjectionState = new MultiProjectionState<TProjectionPayload>();

        var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
        var unsafeEvents = new List<IEvent>();
        while (eventStream != null)
        {
            var list = JsonSerializer.Deserialize<List<JsonElement>>(eventStream) ??
                throw new SekibanSerializerException("Could not deserialize file");
            var events = (IList<IEvent>)list
                .Select(m => SekibanJsonHelper.DeserializeToEvent(m, registeredEventTypes.RegisteredTypes))
                .Where(m => m is not null)
                .ToList();
            var safeEvents = events.Where(m => m.GetSortableUniqueId().IsEarlierThan(targetSafeId)).ToList();
            multiProjectionState = safeEvents.Aggregate(
                multiProjectionState,
                (projection, ev) => projection.ApplyEvent(ev));

            unsafeEvents.AddRange(events.Where(m => m.GetSortableUniqueId().IsLaterThanOrEqual(targetSafeId)));
            eventStream = await stream();
        }

        var unsafeState = unsafeEvents.Aggregate(multiProjectionState, (projection, ev) => projection.ApplyEvent(ev));

        var container = new MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>
        {
            UnsafeEvents = unsafeEvents,
            LastSortableUniqueId = unsafeState.Version == 0
                ? null
                : new SortableUniqueIdValue(unsafeState.LastSortableUniqueId),
            SafeSortableUniqueId = multiProjectionState.Version == 0
                ? null
                : new SortableUniqueIdValue(multiProjectionState.LastSortableUniqueId),
            SafeState = multiProjectionState.Version == 0 ? null : multiProjectionState,
            State = unsafeState.Version == 0 ? null : unsafeState
        };
        if (container.SafeState is not null)
        {
            multiProjectionCache.Set(rootPartitionKey, container);
        }
        return multiProjectionState;
    }

    public async Task<MultiProjectionState<TProjectionPayload>>
        GetMultiProjectionAsync<TProjection, TProjectionPayload>(
            string rootPartitionKey,
            MultiProjectionRetrievalOptions? retrievalOptions = null)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon
    {
        var savedContainerCache = multiProjectionCache.Get<TProjection, TProjectionPayload>(rootPartitionKey);
        var savedContainerBlob = savedContainerCache != null
            ? null
            : await GetContainerFromSnapshot<TProjection, TProjectionPayload>(rootPartitionKey);

        if (savedContainerBlob is not null)
        {
            multiProjectionCache.Set(rootPartitionKey, savedContainerBlob);
        }

        var savedContainer = savedContainerCache ?? savedContainerBlob;
        if (savedContainer == null)
        {
            return await GetInitialProjection<TProjection, TProjectionPayload>(rootPartitionKey);
        }

        var projector = new TProjection();
        if (savedContainer.SafeState is null && savedContainer.SafeSortableUniqueId?.Value is null)
        {
            return await GetInitialProjection<TProjection, TProjectionPayload>(rootPartitionKey);
        }
        if (retrievalOptions?.IncludesSortableUniqueIdValue is not null &&
            savedContainer.SafeSortableUniqueId is not null &&
            retrievalOptions.IncludesSortableUniqueIdValue.IsEarlierThan(savedContainer.SafeSortableUniqueId) &&
            savedContainer.State is not null)
        {
            return savedContainer.State;
        }
        if (retrievalOptions is not null &&
            !retrievalOptions.RetrieveNewEvents &&
            !savedContainer.FromSnapshot &&
            savedContainer.State is not null)
        {
            return savedContainer.State;
        }

        if (retrievalOptions?.PostponeEventFetchBySeconds is not null &&
            savedContainer.State is not null &&
            retrievalOptions.ShouldPostponeFetch(savedContainer.CachedAt, dateProducer.UtcNow))
        {
            return savedContainer.State;
        }

        projector.ApplySnapshot(savedContainer.SafeState!);

        bool? canUseCache = null;
        if (savedContainerCache != null)
        {
            foreach (var targetAggregateName in projector.TargetAggregateNames())
            {
                if (canUseCache == false)
                {
                    continue;
                }
                if (!aggregateSettings.UseUpdateMarkerForType(targetAggregateName))
                {
                    canUseCache = false;
                    continue;
                }

                var (updated, _) = updateNotice.HasUpdateAfter(
                    rootPartitionKey,
                    targetAggregateName,
                    savedContainer.SafeSortableUniqueId!);
                canUseCache = !updated;
            }

            if (canUseCache == true)
            {
                return savedContainer.State!;
            }
        }

        var container = new MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>();


        try
        {
            await documentRepository.GetEvents(
                EventRetrievalInfo.FromNullableValues(
                    rootPartitionKey,
                    new MultiProjectionTypeStream(typeof(TProjection), projector.TargetAggregateNames()),
                    null,
                    ISortableIdCondition.FromMemoryCacheContainer(savedContainer)),
                events =>
                {
                    var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                    foreach (var ev in events)
                    {
                        if (container.LastSortableUniqueId == null &&
                            ev.GetSortableUniqueId().IsEarlierThan(targetSafeId) &&
                            projector.Version > 0)
                        {
                            container = container with
                            {
                                SafeState = projector.ToState(),
                                SafeSortableUniqueId = container.SafeState?.LastSortableUniqueId != null
                                    ? new SortableUniqueIdValue(container.SafeState.LastSortableUniqueId)
                                    : null
                            };
                        }

                        if (ev.GetSortableUniqueId().IsLaterThanOrEqual(savedContainer.SafeSortableUniqueId!))
                        {
                            if (!projector.EventShouldBeApplied(ev)) { throw new SekibanEventOrderMixedUpException(); }
                            projector.ApplyEvent(ev);
                            container = container with { LastSortableUniqueId = ev.GetSortableUniqueId() };
                        }

                        if (ev.GetSortableUniqueId().IsLaterThanOrEqual(targetSafeId))
                        {
                            container.UnsafeEvents.Add(ev);
                        }
                    }
                });
        }
        catch (SekibanEventOrderMixedUpException)
        {
            return await GetInitialProjection<TProjection, TProjectionPayload>(rootPartitionKey);
        }
        container = container with
        {
            State = projector.ToState(), FromSnapshot = false, CachedAt = dateProducer.UtcNow
        };
        if (container.LastSortableUniqueId != null && container.SafeSortableUniqueId == null)
        {
            container = container with
            {
                SafeState = container.State, SafeSortableUniqueId = container.LastSortableUniqueId
            };
        }

        if (container.SafeState is not null && container.SafeSortableUniqueId != savedContainer.SafeSortableUniqueId)
        {
            multiProjectionCache.Set(rootPartitionKey, container);
        }
        return container.State;
    }

    private async Task<MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>?>
        GetContainerFromSnapshot<TProjection, TProjectionPayload>(string rootPartitionKey)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon
    {
        var state = await multiProjectionSnapshotGenerator.GetCurrentStateAsync<TProjectionPayload>(rootPartitionKey);
        if (state.Version == 0)
        {
            return null;
        }
        var container = new MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>();
        container = container with
        {
            SafeState = state,
            State = state,
            SafeSortableUniqueId = string.IsNullOrEmpty(state.LastSortableUniqueId)
                ? null
                : new SortableUniqueIdValue(state.LastSortableUniqueId),
            LastSortableUniqueId = string.IsNullOrEmpty(state.LastSortableUniqueId)
                ? null
                : new SortableUniqueIdValue(state.LastSortableUniqueId),
            FromSnapshot = true
        };
        return container;
    }

    private async Task<MultiProjectionState<TProjectionPayload>>
        GetInitialProjection<TProjection, TProjectionPayload>(string rootPartitionKey)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon
    {
        var projector = new TProjection();
        var container = new MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>();

        await documentRepository.GetEvents(
            EventRetrievalInfo.FromNullableValues(
                rootPartitionKey,
                new MultiProjectionTypeStream(typeof(TProjection), projector.TargetAggregateNames()),
                null,
                ISortableIdCondition.None),
            events =>
            {
                var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                foreach (var ev in events)
                {
                    if (container.LastSortableUniqueId == null &&
                        ev.GetSortableUniqueId().IsEarlierThan(targetSafeId) &&
                        projector.Version > 0)
                    {
                        container = container with
                        {
                            SafeState = projector.ToState(),
                            SafeSortableUniqueId = container.SafeState?.LastSortableUniqueId != null
                                ? new SortableUniqueIdValue(container.SafeState.LastSortableUniqueId)
                                : null
                        };
                    }

                    projector.ApplyEvent(ev);
                    container = container with { LastSortableUniqueId = ev.GetSortableUniqueId() };
                    if (ev.GetSortableUniqueId().IsLaterThanOrEqual(targetSafeId))
                    {
                        container.UnsafeEvents.Add(ev);
                    }
                }
            });
        container = container with { State = projector.ToState(), FromSnapshot = false };
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
            multiProjectionCache.Set(rootPartitionKey, container);
        }
        return container.State;
    }
}
