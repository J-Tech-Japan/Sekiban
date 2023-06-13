using Sekiban.Core.Cache;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Query.UpdateNotice;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
namespace Sekiban.Core.Query.MultiProjections.Projections;

public class MemoryCacheMultiProjection : IMultiProjection
{
    private readonly IAggregateSettings _aggregateSettings;
    private readonly IDocumentRepository _documentRepository;
    private readonly RegisteredEventTypes _registeredEventTypes;
    private readonly IUpdateNotice _updateNotice;
    private readonly IMultiProjectionCache multiProjectionCache;
    private readonly IMultiProjectionSnapshotGenerator multiProjectionSnapshotGenerator;
    public MemoryCacheMultiProjection(
        IDocumentRepository documentRepository,
        IUpdateNotice updateNotice,
        IAggregateSettings aggregateSettings,
        IMultiProjectionCache multiProjectionCache,
        RegisteredEventTypes registeredEventTypes,
        IMultiProjectionSnapshotGenerator multiProjectionSnapshotGenerator)
    {
        _documentRepository = documentRepository;
        _updateNotice = updateNotice;
        _aggregateSettings = aggregateSettings;
        this.multiProjectionCache = multiProjectionCache;
        _registeredEventTypes = registeredEventTypes;
        this.multiProjectionSnapshotGenerator = multiProjectionSnapshotGenerator;
    }

    public async Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionAsync<TProjection, TProjectionPayload>(
        string rootPartitionKey,
        SortableUniqueIdValue? includesSortableUniqueIdValue) where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        var savedContainerCache = multiProjectionCache.Get<TProjection, TProjectionPayload>(rootPartitionKey);
        var savedContainerBlob
            = savedContainerCache != null ? null : await GetContainerFromSnapshot<TProjection, TProjectionPayload>(rootPartitionKey);

        if (savedContainerBlob is not null && savedContainerCache is null)
        {
            multiProjectionCache.Set(rootPartitionKey, savedContainerBlob);
        }

        var savedContainer = savedContainerCache ?? savedContainerBlob;
        if (savedContainer == null)
        {
            return await GetInitialProjection<TProjection, TProjectionPayload>(rootPartitionKey);
        }

        var projector = new TProjection();
        if (savedContainer.SafeState is null && savedContainer?.SafeSortableUniqueId?.Value is null)
        {
            return await GetInitialProjection<TProjection, TProjectionPayload>(rootPartitionKey);
        }
        if (includesSortableUniqueIdValue is not null &&
            savedContainer.SafeSortableUniqueId is not null &&
            includesSortableUniqueIdValue.EarlierThan(savedContainer.SafeSortableUniqueId))
        {
            return savedContainer.State!;
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
                if (!_aggregateSettings.UseUpdateMarkerForType(targetAggregateName))
                {
                    canUseCache = false;
                    continue;
                }

                var (updated, type) = _updateNotice.HasUpdateAfter(targetAggregateName, savedContainer.SafeSortableUniqueId!);
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
            await _documentRepository.GetAllEventsAsync(
                typeof(TProjection),
                projector.TargetAggregateNames(),
                savedContainer.SafeSortableUniqueId?.Value,
                rootPartitionKey,
                events =>
                {
                    var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                    foreach (var ev in events)
                    {
                        if (container.LastSortableUniqueId == null && ev.GetSortableUniqueId().EarlierThan(targetSafeId) && projector.Version > 0)
                        {
                            container = container with
                            {
                                SafeState = projector.ToState(),
                                SafeSortableUniqueId = container.SafeState?.LastSortableUniqueId != null
                                    ? new SortableUniqueIdValue(container.SafeState.LastSortableUniqueId)
                                    : null
                            };
                        }

                        if (ev.GetSortableUniqueId().LaterThanOrEqual(savedContainer.SafeSortableUniqueId!))
                        {
                            if (!projector.EventShouldBeApplied(ev)) { throw new SekibanEventOrderMixedUpException(); }
                            projector.ApplyEvent(ev);
                            container = container with { LastSortableUniqueId = ev.GetSortableUniqueId() };
                        }

                        if (ev.GetSortableUniqueId().LaterThanOrEqual(targetSafeId))
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
        container = container with { State = projector.ToState() };
        if (container.LastSortableUniqueId != null && container.SafeSortableUniqueId == null)
        {
            container = container with { SafeState = container.State, SafeSortableUniqueId = container.LastSortableUniqueId };
        }

        if (container.SafeState is not null && container.SafeSortableUniqueId != savedContainer?.SafeSortableUniqueId)
        {
            multiProjectionCache.Set(rootPartitionKey, container);
        }
        return container.State;
    }
    public async Task<MultiProjectionState<TProjectionPayload>> GetInitialMultiProjectionFromStreamAsync<TProjection, TProjectionPayload>(
        Stream stream,
        string rootPartitionKey,
        SortableUniqueIdValue? includesSortableUniqueIdValue) where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        await Task.CompletedTask;
        var list = JsonSerializer.Deserialize<List<JsonElement>>(stream) ?? throw new Exception("Could not deserialize file");
        var events = (IList<IEvent>)list.Select(m => SekibanJsonHelper.DeserializeToEvent(m, _registeredEventTypes.RegisteredTypes))
            .Where(m => m is not null)
            .OrderBy(m => m is null ? string.Empty : m.SortableUniqueId)
            .ToList();
        var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
        var safeEvents = events.Where(m => m.GetSortableUniqueId().EarlierThan(targetSafeId)).ToList();
        var unsafeEvents = events.Where(m => m.GetSortableUniqueId().LaterThanOrEqual(targetSafeId)).ToList();
        var safeState = safeEvents.Aggregate(new MultiProjectionState<TProjectionPayload>(), (projection, ev) => projection.ApplyEvent(ev));
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
    public async Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionFromMultipleStreamAsync<TProjection, TProjectionPayload>(
        Func<Task<Stream?>> stream,
        string rootPartitionKey,
        SortableUniqueIdValue? includesSortableUniqueIdValue) where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        var eventStream = await stream();
        var multiProjectionState = new MultiProjectionState<TProjectionPayload>();

        var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
        var unsafeEvents = new List<IEvent>();
        SortableUniqueIdValue? lastSortableUniqueId = null;


        while (eventStream != null)
        {
            var list = JsonSerializer.Deserialize<List<JsonElement>>(eventStream) ?? throw new Exception("Could not deserialize file");
            var events = (IList<IEvent>)list.Select(m => SekibanJsonHelper.DeserializeToEvent(m, _registeredEventTypes.RegisteredTypes))
                .Where(m => m is not null)
                .ToList();
            lastSortableUniqueId = events.LastOrDefault()?.GetSortableUniqueId();
            var safeEvents = events.Where(m => m.GetSortableUniqueId().EarlierThan(targetSafeId)).ToList();
            multiProjectionState = safeEvents.Aggregate(multiProjectionState, (projection, ev) => projection.ApplyEvent(ev));

            unsafeEvents.AddRange(events.Where(m => m.GetSortableUniqueId().LaterThanOrEqual(targetSafeId)));
            eventStream = await stream();
        }

        var unsafeState = unsafeEvents.Aggregate(multiProjectionState, (projection, ev) => projection.ApplyEvent(ev));

        var container = new MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>
        {
            UnsafeEvents = unsafeEvents,
            LastSortableUniqueId = unsafeState.Version == 0 ? null : new SortableUniqueIdValue(unsafeState.LastSortableUniqueId),
            SafeSortableUniqueId
                = multiProjectionState.Version == 0 ? null : new SortableUniqueIdValue(multiProjectionState.LastSortableUniqueId),
            SafeState = multiProjectionState.Version == 0 ? null : multiProjectionState,
            State = unsafeState.Version == 0 ? null : unsafeState
        };
        if (container.SafeState is not null)
        {
            multiProjectionCache.Set(rootPartitionKey, container);
        }
        return multiProjectionState;
    }

    private async Task<MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>?>
        GetContainerFromSnapshot<TProjection, TProjectionPayload>(string rootPartitionKey)
        where TProjection : IMultiProjector<TProjectionPayload>, new() where TProjectionPayload : IMultiProjectionPayloadCommon, new()
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
            SafeSortableUniqueId
            = string.IsNullOrEmpty(state.LastSortableUniqueId) ? null : new SortableUniqueIdValue(state.LastSortableUniqueId),
            LastSortableUniqueId = string.IsNullOrEmpty(state.LastSortableUniqueId) ? null : new SortableUniqueIdValue(state.LastSortableUniqueId)
        };
        return container;
    }

    private async Task<MultiProjectionState<TProjectionPayload>> GetInitialProjection<TProjection, TProjectionPayload>(string rootPartitionKey)
        where TProjection : IMultiProjector<TProjectionPayload>, new() where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        var projector = new TProjection();
        var container = new MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>();
        await _documentRepository.GetAllEventsAsync(
            typeof(TProjection),
            projector.TargetAggregateNames(),
            null,
            rootPartitionKey,
            events =>
            {
                var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                foreach (var ev in events)
                {
                    if (container.LastSortableUniqueId == null && ev.GetSortableUniqueId().EarlierThan(targetSafeId) && projector.Version > 0)
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
                    if (ev.GetSortableUniqueId().LaterThanOrEqual(targetSafeId))
                    {
                        container.UnsafeEvents.Add(ev);
                    }
                }
            });
        container = container with { State = projector.ToState() };
        if (container.LastSortableUniqueId != null &&
            container.SafeSortableUniqueId == null &&
            container.LastSortableUniqueId?.EarlierThan(SortableUniqueIdValue.GetSafeIdFromUtc()) == true)
        {
            container = container with { SafeState = container.State, SafeSortableUniqueId = container.LastSortableUniqueId };
        }

        if (container.SafeState is not null)
        {
            multiProjectionCache.Set(rootPartitionKey, container);
        }
        return container.State;
    }
}
