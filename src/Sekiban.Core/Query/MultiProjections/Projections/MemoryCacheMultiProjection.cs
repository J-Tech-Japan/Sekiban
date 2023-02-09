using Microsoft.Extensions.Caching.Memory;
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
    public MemoryCacheMultiProjection(
        IMemoryCache memoryCache,
        IDocumentRepository documentRepository,
        IServiceProvider serviceProvider,
        IUpdateNotice updateNotice,
        IAggregateSettings aggregateSettings,
        IMultiProjectionCache multiProjectionCache,
        RegisteredEventTypes registeredEventTypes)
    {
        _documentRepository = documentRepository;
        _updateNotice = updateNotice;
        _aggregateSettings = aggregateSettings;
        this.multiProjectionCache = multiProjectionCache;
        _registeredEventTypes = registeredEventTypes;
    }

    public async Task<MultiProjectionState<TProjectionPayload>>
        GetMultiProjectionAsync<TProjection, TProjectionPayload>(SortableUniqueIdValue? includesSortableUniqueIdValue)
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        var savedContainer = multiProjectionCache.Get<TProjection, TProjectionPayload>();
        if (savedContainer == null)
        {
            return await GetInitialProjection<TProjection, TProjectionPayload>();
        }

        var projector = new TProjection();
        if (savedContainer.SafeState is null && savedContainer?.SafeSortableUniqueId?.Value is null)
        {
            return await GetInitialProjection<TProjection, TProjectionPayload>();
        }
        if (includesSortableUniqueIdValue is not null &&
            savedContainer.SafeSortableUniqueId is not null &&
            includesSortableUniqueIdValue.EarlierThan(savedContainer.SafeSortableUniqueId))
        {
            return savedContainer.State!;
        }
        projector.ApplySnapshot(savedContainer.SafeState!);

        bool? canUseCache = null;
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

            var (updated, type) =
                _updateNotice.HasUpdateAfter(targetAggregateName, savedContainer.SafeSortableUniqueId!);
            canUseCache = !updated;
        }

        if (canUseCache == true)
        {
            return savedContainer.State!;
        }

        var container = new MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>();
        try
        {
            await _documentRepository.GetAllEventsAsync(
                typeof(TProjection),
                projector.TargetAggregateNames(),
                savedContainer.SafeSortableUniqueId?.Value,
                events =>
                {
                    var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                    foreach (var ev in events)
                    {
                        if (container.LastSortableUniqueId == null &&
                            ev.GetSortableUniqueId().EarlierThan(targetSafeId) &&
                            projector.Version > 0)
                        {
                            container = container with
                            {
                                SafeState = projector.ToState(),
                                SafeSortableUniqueId = container.SafeState?.LastSortableUniqueId != null
                                    ? new SortableUniqueIdValue(container.SafeState.LastSortableUniqueId) : null
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
            return await GetInitialProjection<TProjection, TProjectionPayload>();
        }
        container = container with { State = projector.ToState() };
        if (container.LastSortableUniqueId != null && container.SafeSortableUniqueId == null)
        {
            container = container with { SafeState = container.State, SafeSortableUniqueId = container.LastSortableUniqueId };
        }

        if (container.SafeState is not null && container.SafeSortableUniqueId != savedContainer?.SafeSortableUniqueId)
        {
            multiProjectionCache.Set(container);
        }
        return container.State;
    }
    public async Task<MultiProjectionState<TProjectionPayload>> GetInitialMultiProjectionFromStreamAsync<TProjection, TProjectionPayload>(
        Stream stream,
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
        var safeState = safeEvents.Aggregate(
            new MultiProjectionState<TProjectionPayload>(),
            (projection, ev) => projection.ApplyEvent(ev));
        var state = unsafeEvents.Aggregate(
            safeState,
            (projection, ev) => projection.ApplyEvent(ev));
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
            multiProjectionCache.Set(container);
        }

        return container.State;
    }
    public async Task<MultiProjectionState<TProjectionPayload>> GetMultiProjectionFromMultipleStreamAsync<TProjection, TProjectionPayload>(
        Func<Task<Stream?>> stream,
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
            multiProjectionState = safeEvents.Aggregate(
                multiProjectionState,
                (projection, ev) => projection.ApplyEvent(ev));

            unsafeEvents.AddRange(events.Where(m => m.GetSortableUniqueId().LaterThanOrEqual(targetSafeId)));
            eventStream = await stream();
        }

        var unsafeState = unsafeEvents.Aggregate(
            multiProjectionState,
            (projection, ev) => projection.ApplyEvent(ev));

        var container = new MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>
        {
            UnsafeEvents = unsafeEvents,
            LastSortableUniqueId = unsafeState.Version == 0 ? null : new SortableUniqueIdValue(unsafeState.LastSortableUniqueId),
            SafeSortableUniqueId = multiProjectionState.Version == 0 ? null : new SortableUniqueIdValue(multiProjectionState.LastSortableUniqueId),
            SafeState = multiProjectionState.Version == 0 ? null : multiProjectionState,
            State = unsafeState.Version == 0 ? null : unsafeState
        };
        if (container.SafeState is not null)
        {
            multiProjectionCache.Set(container);
        }
        return multiProjectionState;
    }

    private async Task<MultiProjectionState<TProjectionPayload>> GetInitialProjection<TProjection, TProjectionPayload>()
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayloadCommon, new()
    {
        var projector = new TProjection();
        var container = new MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>();
        await _documentRepository.GetAllEventsAsync(
            typeof(TProjection),
            projector.TargetAggregateNames(),
            null,
            events =>
            {
                var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                foreach (var ev in events)
                {
                    if (container.LastSortableUniqueId == null &&
                        ev.GetSortableUniqueId().EarlierThan(targetSafeId) &&
                        projector.Version > 0)
                    {
                        container = container with
                        {
                            SafeState = projector.ToState(),
                            SafeSortableUniqueId = container.SafeState?.LastSortableUniqueId != null
                                ? new SortableUniqueIdValue(container.SafeState.LastSortableUniqueId) : null
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
            multiProjectionCache.Set(container);
        }
        return container.State;
    }
}
