using Microsoft.Extensions.Caching.Memory;
using Sekiban.EventSourcing.Documents.ValueObjects;
using Sekiban.EventSourcing.Queries.UpdateNotices;
using Sekiban.EventSourcing.Settings;
namespace Sekiban.EventSourcing.Queries.SingleAggregates.SingleProjection
{
    public class MemoryCacheSingleProjection : ISingleProjection
    {
        private readonly IAggregateSettings _aggregateSettings;
        private readonly IDocumentRepository _documentRepository;
        private readonly IMemoryCache _memoryCache;
        private readonly IUpdateNotice _updateNotice;
        public MemoryCacheSingleProjection(
            IDocumentRepository documentRepository,
            IMemoryCache memoryCache,
            IUpdateNotice updateNotice,
            IAggregateSettings aggregateSettings)
        {
            _documentRepository = documentRepository;
            _memoryCache = memoryCache;
            _updateNotice = updateNotice;
            _aggregateSettings = aggregateSettings;
        }
        public async Task<T?> GetAggregateAsync<T, Q, P>(Guid aggregateId, int? toVersion = null)
            where T : ISingleAggregate, ISingleAggregateProjection, ISingleAggregateProjectionDtoConvertible<Q>
            where Q : ISingleAggregate
            where P : ISingleAggregateProjector<T>, new()
        {
            var savedContainer = _memoryCache.Get<SingleMemoryCacheProjectionContainer<T, Q>>(GetCacheKey<T>(aggregateId));
            if (savedContainer == null)
            {
                return await GetAggregateWithoutCacheAsync<T, Q, P>(aggregateId, toVersion);
            }
            var projector = new P();
            if (savedContainer.SafeDto is null && savedContainer?.SafeSortableUniqueId?.Value is null)
            {
                return await GetAggregateWithoutCacheAsync<T, Q, P>(aggregateId, toVersion);
            }
            var aggregate = projector.CreateInitialAggregate(aggregateId);
            aggregate.ApplySnapshot(savedContainer.SafeDto!);

            if (_aggregateSettings.UseUpdateMarkerForType(typeof(T).Name))
            {
                var (updated, type) = _updateNotice.HasUpdateAfter(typeof(T).Name, aggregateId, savedContainer.SafeSortableUniqueId!);
                if (!updated)
                {
                    return aggregate;
                }
            }
            var container = new SingleMemoryCacheProjectionContainer<T, Q>(aggregateId);
            await _documentRepository.GetAllAggregateEventsForAggregateIdAsync(
                aggregateId,
                projector.OriginalAggregateType(),
                PartitionKeyGenerator.ForAggregateEvent(aggregateId, projector.OriginalAggregateType()),
                savedContainer?.SafeSortableUniqueId?.Value,
                events =>
                {
                    var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                    foreach (var e in events)
                    {
                        if (!string.IsNullOrWhiteSpace(savedContainer?.SafeSortableUniqueId?.Value) &&
                            string.CompareOrdinal(savedContainer?.SafeSortableUniqueId?.Value, e.SortableUniqueId) > 0)
                        {
                            throw new SekibanAggregateEventDuplicateException();
                        }
                        if (container.LastSortableUniqueId == null && e.GetSortableUniqueId().LaterThan(targetSafeId) && aggregate.Version > 0)
                        {
                            container.SafeDto = aggregate.ToDto();
                            container.SafeSortableUniqueId = container.SafeDto.LastSortableUniqueId;
                        }
                        aggregate.ApplyEvent(e);
                        container.LastSortableUniqueId = e.SortableUniqueId;
                        if (e.GetSortableUniqueId().LaterThan(targetSafeId))
                        {
                            container.UnsafeEvents.Add(e);
                        }
                        if (toVersion.HasValue && aggregate.Version == toVersion.Value)
                        {
                            break;
                        }
                    }
                });
            if (aggregate.Version == 0) { return default; }
            if (toVersion.HasValue && aggregate.Version < toVersion.Value)
            {
                throw new SekibanVersionNotReachToSpecificVersion();
            }
            container.Dto = aggregate.ToDto();
            if (container.LastSortableUniqueId != null &&
                container.SafeSortableUniqueId == null &&
                container.LastSortableUniqueId?.EarlierThan(SortableUniqueIdValue.GetSafeIdFromUtc()) == true)
            {
                container.SafeDto = container.Dto;
                container.SafeSortableUniqueId = container.LastSortableUniqueId;
            }
            if (container.SafeDto is not null)
            {
                _memoryCache.Set(GetCacheKey<T>(aggregateId), container, GetMemoryCacheOptions());
            }
            return aggregate;
        }

        private async Task<T?> GetAggregateWithoutCacheAsync<T, Q, P>(Guid aggregateId, int? toVersion = null)
            where T : ISingleAggregate, ISingleAggregateProjection, ISingleAggregateProjectionDtoConvertible<Q>
            where Q : ISingleAggregate
            where P : ISingleAggregateProjector<T>, new()
        {
            var projector = new P();
            var aggregate = projector.CreateInitialAggregate(aggregateId);
            var container = new SingleMemoryCacheProjectionContainer<T, Q>(aggregateId);

            var snapshotDocument = await _documentRepository.GetLatestSnapshotForAggregateAsync(aggregateId, typeof(T));
            var dto = snapshotDocument is null ? default : snapshotDocument.ToDto<Q>();
            if (dto is not null)
            {
                aggregate.ApplySnapshot(dto);
            }
            if (toVersion.HasValue && aggregate.Version >= toVersion.Value)
            {
                return await GetAggregateFromInitialAsync<T, Q, P>(aggregateId, toVersion.Value);
            }

            await _documentRepository.GetAllAggregateEventsForAggregateIdAsync(
                aggregateId,
                projector.OriginalAggregateType(),
                PartitionKeyGenerator.ForAggregateEvent(aggregateId, projector.OriginalAggregateType()),
                dto?.LastSortableUniqueId,
                events =>
                {
                    var someSafeId = SortableUniqueIdValue.Generate(DateTime.UtcNow, Guid.Empty);
                    var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                    foreach (var e in events)
                    {
                        if (!string.IsNullOrWhiteSpace(dto?.LastSortableUniqueId) &&
                            string.CompareOrdinal(dto?.LastSortableUniqueId, e.SortableUniqueId) > 0)
                        {
                            throw new SekibanAggregateEventDuplicateException();
                        }
                        if (container.LastSortableUniqueId == null && e.GetSortableUniqueId().EarlierThan(targetSafeId) && aggregate.Version > 0)
                        {
                            container.SafeDto = aggregate.ToDto();
                            container.SafeSortableUniqueId = container.SafeDto.LastSortableUniqueId;
                        }
                        aggregate.ApplyEvent(e);
                        container.LastSortableUniqueId = e.GetSortableUniqueId();
                        if (e.GetSortableUniqueId().LaterThan(targetSafeId))
                        {
                            container.UnsafeEvents.Add(e);
                        }
                        if (toVersion.HasValue && aggregate.Version == toVersion.Value)
                        {
                            break;
                        }
                    }
                });
            if (aggregate.Version == 0) { return default; }
            if (toVersion.HasValue && aggregate.Version < toVersion.Value)
            {
                throw new SekibanVersionNotReachToSpecificVersion();
            }
            container.Dto = aggregate.ToDto();
            if (container.LastSortableUniqueId != null &&
                container.SafeSortableUniqueId == null &&
                container.LastSortableUniqueId?.EarlierThan(SortableUniqueIdValue.GetSafeIdFromUtc()) == true)
            {
                container.SafeDto = container.Dto;
                container.SafeSortableUniqueId = container.LastSortableUniqueId;
            }
            if (container.SafeDto is not null)
            {
                _memoryCache.Set(GetCacheKey<T>(aggregateId), container, GetMemoryCacheOptions());
            }
            return aggregate;
        }

        public async Task<T?> GetAggregateFromInitialAsync<T, Q, P>(Guid aggregateId, int? toVersion)
            where T : ISingleAggregate, ISingleAggregateProjection, ISingleAggregateProjectionDtoConvertible<Q>
            where Q : ISingleAggregate
            where P : ISingleAggregateProjector<T>, new()
        {
            var projector = new P();
            var container = new SingleMemoryCacheProjectionContainer<T, Q>(aggregateId);
            var aggregate = projector.CreateInitialAggregate(aggregateId);
            var addFinished = false;
            await _documentRepository.GetAllAggregateEventsForAggregateIdAsync(
                aggregateId,
                typeof(T),
                PartitionKeyGenerator.ForAggregateEvent(aggregateId, projector.OriginalAggregateType()),
                null,
                events =>
                {
                    if (events.Count() != events.Select(m => m.Id).Distinct().Count())
                    {
                        throw new SekibanAggregateEventDuplicateException();
                    }
                    if (addFinished) { return; }
                    var someSafeId = SortableUniqueIdValue.Generate(DateTime.UtcNow, Guid.Empty);
                    var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                    foreach (var e in events)
                    {
                        if (container.LastSortableUniqueId == null && e.GetSortableUniqueId().EarlierThan(targetSafeId) && aggregate.Version > 0)
                        {
                            container.SafeDto = aggregate.ToDto();
                            container.SafeSortableUniqueId = container.SafeDto.LastSortableUniqueId;
                        }
                        aggregate.ApplyEvent(e);
                        container.LastSortableUniqueId = e.GetSortableUniqueId();
                        if (e.GetSortableUniqueId().LaterThan(targetSafeId))
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
            if (aggregate.Version == 0) { return default; }

            container.Dto = aggregate.ToDto();
            if (container.LastSortableUniqueId != null &&
                container.SafeSortableUniqueId == null &&
                container.LastSortableUniqueId?.EarlierThan(SortableUniqueIdValue.GetSafeIdFromUtc()) == true)
            {
                container.SafeDto = container.Dto;
                container.SafeSortableUniqueId = container.LastSortableUniqueId;
            }
            if (container.SafeDto is not null)
            {
                _memoryCache.Set(GetCacheKey<T>(aggregateId), container, GetMemoryCacheOptions());
            }
            return aggregate;
        }

        private static MemoryCacheEntryOptions GetMemoryCacheOptions() =>
            new()
            {
                AbsoluteExpiration = DateTimeOffset.UtcNow.AddHours(2), SlidingExpiration = TimeSpan.FromMinutes(15)
                // 5分読まれなかったら削除するが、2時間経ったらどちらにしても削除する
            };

        public string GetCacheKey<TAggregate>(Guid aggregateId) =>
            "SingleAggregate" + typeof(TAggregate).Name + aggregateId;
    }
}
