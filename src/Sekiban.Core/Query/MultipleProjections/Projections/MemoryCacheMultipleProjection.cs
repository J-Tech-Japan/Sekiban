using Microsoft.Extensions.Caching.Memory;
using Sekiban.Core.Cache;
using Sekiban.Core.Document;
using Sekiban.Core.Document.ValueObjects;
using Sekiban.Core.Query.UpdateNotice;
using Sekiban.Core.Setting;
namespace Sekiban.Core.Query.MultipleProjections.Projections;

public class MemoryCacheMultipleProjection : IMultipleProjection
{
    private readonly IAggregateSettings _aggregateSettings;
    private readonly IDocumentRepository _documentRepository;
    private readonly IUpdateNotice _updateNotice;
    private readonly IMultiProjectionCache multiProjectionCache;
    public MemoryCacheMultipleProjection(
        IMemoryCache memoryCache,
        IDocumentRepository documentRepository,
        IServiceProvider serviceProvider,
        IUpdateNotice updateNotice,
        IAggregateSettings aggregateSettings,
        IMultiProjectionCache multiProjectionCache)
    {
        _documentRepository = documentRepository;
        _updateNotice = updateNotice;
        _aggregateSettings = aggregateSettings;
        this.multiProjectionCache = multiProjectionCache;
    }
    public async Task<MultiProjectionState<TProjectionPayload>> GetMultipleProjectionAsync<TProjection, TProjectionPayload>()
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
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
        await _documentRepository.GetAllEventsAsync(
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
            multiProjectionCache.Set(container);
        }
        return container.State;
    }
    private async Task<MultiProjectionState<TProjectionPayload>> GetInitialProjection<TProjection, TProjectionPayload>()
        where TProjection : IMultiProjector<TProjectionPayload>, new()
        where TProjectionPayload : IMultiProjectionPayload, new()
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
            multiProjectionCache.Set(container);
        }
        return container.State;
    }
}