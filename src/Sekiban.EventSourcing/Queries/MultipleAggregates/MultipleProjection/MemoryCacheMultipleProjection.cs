using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.EventSourcing.Documents.ValueObjects;
using Sekiban.EventSourcing.Queries.UpdateNotices;
using Sekiban.EventSourcing.Settings;
namespace Sekiban.EventSourcing.Queries.MultipleAggregates.MultipleProjection;

public class MemoryCacheMultipleProjection : IMultipleProjection
{
    private readonly IAggregateSettings _aggregateSettings;
    private readonly IDocumentRepository _documentRepository;
    private readonly IMemoryCache _memoryCache;
    private readonly IServiceProvider _serviceProvider;
    private readonly IUpdateNotice _updateNotice;
    public MemoryCacheMultipleProjection(
        IMemoryCache memoryCache,
        IDocumentRepository documentRepository,
        IServiceProvider serviceProvider,
        IUpdateNotice updateNotice,
        IAggregateSettings aggregateSettings)
    {
        _memoryCache = memoryCache;
        _documentRepository = documentRepository;
        _serviceProvider = serviceProvider;
        _updateNotice = updateNotice;
        _aggregateSettings = aggregateSettings;
    }
    public async Task<Q> GetMultipleProjectionAsync<P, Q>() where P : IMultipleAggregateProjector<Q>, new()
        where Q : IMultipleAggregateProjectionDto, new()
    {
        var savedContainer = _memoryCache.Get<MultipleMemoryProjectionContainer<P, Q>>(GetInMemoryKey<P, Q>());
        if (savedContainer == null)
        {
            return await GetInitialProjection<P, Q>();
        }

        var projector = new P();
        if (savedContainer.SafeDto is null && savedContainer?.SafeSortableUniqueId?.Value is null)
        {
            return await GetInitialProjection<P, Q>();
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

        var container = new MultipleMemoryProjectionContainer<P, Q>();
        await _documentRepository.GetAllAggregateEventsAsync(
            typeof(P),
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
            _memoryCache.Set(GetInMemoryKey<P, Q>(), container, GetMemoryCacheOptions());
        }
        return container.Dto;
    }

    private async Task<Q> GetInitialProjection<P, Q>() where P : IMultipleAggregateProjector<Q>, new()
        where Q : IMultipleAggregateProjectionDto, new()
    {
        var projector = new P();
        var container = new MultipleMemoryProjectionContainer<P, Q>();
        await _documentRepository.GetAllAggregateEventsAsync(
            typeof(P),
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
            _memoryCache.Set(GetInMemoryKey<P, Q>(), container, GetMemoryCacheOptions());
        }
        return container.Dto;
    }
    private static MemoryCacheEntryOptions GetMemoryCacheOptions() =>
        new()
        {
            AbsoluteExpiration = DateTimeOffset.UtcNow.AddHours(2), SlidingExpiration = TimeSpan.FromMinutes(15)
            // 5分読まれなかったら削除するが、2時間経ったらどちらにしても削除する
        };
    private string GetInMemoryKey<P, Q>() where P : IMultipleAggregateProjector<Q>, new() where Q : IMultipleAggregateProjectionDto, new()
    {
        var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
        return "MultipleProjection-" + sekibanContext?.SettingGroupIdentifier + "-" + typeof(P).FullName;
    }
}
