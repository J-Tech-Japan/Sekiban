using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
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
    private readonly IUpdateNotice _updateNotice;
    private readonly IMultipleAggregateProjectionCache _multipleAggregateProjectionCache;
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
    public async Task<MultipleAggregateProjectionContentsDto<TProjectionContents>> GetMultipleProjectionAsync<TProjection, TProjectionContents>()
        where TProjection : IMultipleAggregateProjector<TProjectionContents>, new()
        where TProjectionContents : IMultipleAggregateProjectionContents, new()
    {
        var savedContainer = _multipleAggregateProjectionCache.Get<TProjection, TProjectionContents>();
        if (savedContainer == null)
        {
            return await GetInitialProjection<TProjection, TProjectionContents>();
        }

        var projector = new TProjection();
        if (savedContainer.SafeDto is null && savedContainer?.SafeSortableUniqueId?.Value is null)
        {
            return await GetInitialProjection<TProjection, TProjectionContents>();
        }
        projector.ApplySnapshot(savedContainer.SafeDto!);

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
            return savedContainer.Dto!;
        }

        var container = new MultipleMemoryProjectionContainer<TProjection, TProjectionContents>();
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
                        container.SafeDto = projector.ToDto();
                        container.SafeSortableUniqueId = container.SafeDto.LastSortableUniqueId;
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
        container.Dto = projector.ToDto();
        if (container.LastSortableUniqueId != null && container.SafeSortableUniqueId == null)
        {
            container.SafeDto = container.Dto;
            container.SafeSortableUniqueId = container.LastSortableUniqueId;
        }
        if (container.SafeDto is not null && container.SafeSortableUniqueId != savedContainer?.SafeSortableUniqueId)
        {
            _multipleAggregateProjectionCache.Set(container);
        }
        return container.Dto;
    }
    private async Task<MultipleAggregateProjectionContentsDto<TContents>> GetInitialProjection<TProjection, TContents>()
        where TProjection : IMultipleAggregateProjector<TContents>, new() where TContents : IMultipleAggregateProjectionContents, new()
    {
        var projector = new TProjection();
        var container = new MultipleMemoryProjectionContainer<TProjection, TContents>();
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
                        container.SafeDto = projector.ToDto();
                        container.SafeSortableUniqueId = container.SafeDto.LastSortableUniqueId;
                    }
                    projector.ApplyEvent(ev);
                    container.LastSortableUniqueId = ev.GetSortableUniqueId();
                    if (ev.GetSortableUniqueId().LaterThan(targetSafeId))
                    {
                        container.UnsafeEvents.Add(ev);
                    }
                }
            });
        container.Dto = projector.ToDto();
        if (container.LastSortableUniqueId != null &&
            container.SafeSortableUniqueId == null &&
            container.LastSortableUniqueId?.EarlierThan(SortableUniqueIdValue.GetSafeIdFromUtc()) == true)
        {
            container.SafeDto = container.Dto;
            container.SafeSortableUniqueId = container.LastSortableUniqueId;
        }
        if (container.SafeDto is not null)
        {
            _multipleAggregateProjectionCache.Set(container);
        }
        return container.Dto;
    }

}
