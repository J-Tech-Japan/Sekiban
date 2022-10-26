using Microsoft.Extensions.Caching.Memory;
using Sekiban.Core.Cache;
using Sekiban.Core.Document;
using Sekiban.Core.Document.ValueObjects;
using Sekiban.Core.Query.UpdateNotice;
using Sekiban.Core.Setting;
namespace Sekiban.Core.Query.MultipleAggregate.MultipleProjection;

public class MemoryCacheMultipleProjection : IMultipleProjection
{
    private readonly IAggregateSettings _aggregateSettings;
    private readonly IDocumentRepository _documentRepository;
    private readonly IMultipleAggregateProjectionCache _multipleAggregateProjectionCache;
    private readonly IUpdateNotice _updateNotice;
    public MemoryCacheMultipleProjection(
        IMemoryCache memoryCache,
        IDocumentRepository documentRepository,
        IServiceProvider serviceProvider,
        IUpdateNotice updateNotice,
        IAggregateSettings aggregateSettings,
        IMultipleAggregateProjectionCache multipleAggregateProjectionCache)
    {
        _documentRepository = documentRepository;
        _updateNotice = updateNotice;
        _aggregateSettings = aggregateSettings;
        _multipleAggregateProjectionCache = multipleAggregateProjectionCache;
    }
    public async Task<MultipleAggregateProjectionState<TProjectionPayload>> GetMultipleProjectionAsync<TProjection, TProjectionPayload>()
        where TProjection : IMultipleAggregateProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultipleAggregateProjectionPayload, new()
    {
        var savedContainer = _multipleAggregateProjectionCache.Get<TProjection, TProjectionPayload>();
        if (savedContainer == null)
        {
            return await GetInitialProjection<TProjection, TProjectionPayload>();
        }

        var projector = new TProjection();
        if (savedContainer.SafeState is null && savedContainer?.SafeSortableUniqueId?.Value is null)
        {
            return await GetInitialProjection<TProjection, TProjectionPayload>();
        }
        projector.ApplySnapshot(savedContainer.SafeState!);

        bool? canUseCache = null;
        foreach (var targetAggregateName in projector.TargetAggregateNames())
        {
            if (canUseCache == false) { continue; }
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

        var container = new MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>();
        await _documentRepository.GetAllAggregateEventsAsync(
            typeof(TProjection),
            projector.TargetAggregateNames(),
            savedContainer.SafeSortableUniqueId?.Value,
            events =>
            {
                var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                foreach (var ev in events)
                {
                    if (container.LastSortableUniqueId == null && ev.GetSortableUniqueId().EarlierThan(targetSafeId) && projector.Version > 0)
                    {
                        container.SafeState = projector.ToState();
                        container.SafeSortableUniqueId = container.SafeState.LastSortableUniqueId;
                    }
                    if (ev.GetSortableUniqueId().LaterThan(savedContainer.SafeSortableUniqueId!))
                    {
                        projector.ApplyEvent(ev);
                        container.LastSortableUniqueId = ev.GetSortableUniqueId();
                    }
                    if (ev.GetSortableUniqueId().LaterThan(targetSafeId))
                    {
                        container.UnsafeEvents.Add(ev);
                    }
                }
            });
        container.State = projector.ToState();
        if (container.LastSortableUniqueId != null && container.SafeSortableUniqueId == null)
        {
            container.SafeState = container.State;
            container.SafeSortableUniqueId = container.LastSortableUniqueId;
        }
        if (container.SafeState is not null && container.SafeSortableUniqueId != savedContainer?.SafeSortableUniqueId)
        {
            _multipleAggregateProjectionCache.Set(container);
        }
        return container.State;
    }
    private async Task<MultipleAggregateProjectionState<TProjectionPayload>> GetInitialProjection<TProjection, TProjectionPayload>()
        where TProjection : IMultipleAggregateProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultipleAggregateProjectionPayload, new()
    {
        var projector = new TProjection();
        var container = new MultipleMemoryProjectionContainer<TProjection, TProjectionPayload>();
        await _documentRepository.GetAllAggregateEventsAsync(
            typeof(TProjection),
            projector.TargetAggregateNames(),
            null,
            events =>
            {
                var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                foreach (var ev in events)
                {
                    if (container.LastSortableUniqueId == null && ev.GetSortableUniqueId().EarlierThan(targetSafeId) && projector.Version > 0)
                    {
                        container.SafeState = projector.ToState();
                        container.SafeSortableUniqueId = container.SafeState.LastSortableUniqueId;
                    }
                    projector.ApplyEvent(ev);
                    container.LastSortableUniqueId = ev.GetSortableUniqueId();
                    if (ev.GetSortableUniqueId().LaterThan(targetSafeId))
                    {
                        container.UnsafeEvents.Add(ev);
                    }
                }
            });
        container.State = projector.ToState();
        if (container.LastSortableUniqueId != null &&
            container.SafeSortableUniqueId == null &&
            container.LastSortableUniqueId?.EarlierThan(SortableUniqueIdValue.GetSafeIdFromUtc()) == true)
        {
            container.SafeState = container.State;
            container.SafeSortableUniqueId = container.LastSortableUniqueId;
        }
        if (container.SafeState is not null)
        {
            _multipleAggregateProjectionCache.Set(container);
        }
        return container.State;
    }
}
