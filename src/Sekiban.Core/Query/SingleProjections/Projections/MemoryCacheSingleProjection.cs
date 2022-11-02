using Sekiban.Core.Cache;
using Sekiban.Core.Document;
using Sekiban.Core.Document.ValueObjects;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.UpdateNotice;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
namespace Sekiban.Core.Query.SingleProjections.Projections;

public class MemoryCacheSingleProjection : ISingleProjection
{
    private readonly IAggregateSettings _aggregateSettings;
    private readonly IDocumentRepository _documentRepository;
    private readonly IUpdateNotice _updateNotice;
    private readonly ISingleProjectionCache singleProjectionCache;
    public MemoryCacheSingleProjection(
        IDocumentRepository documentRepository,
        IUpdateNotice updateNotice,
        IAggregateSettings aggregateSettings,
        ISingleProjectionCache singleProjectionCache)
    {
        _documentRepository = documentRepository;
        _updateNotice = updateNotice;
        _aggregateSettings = aggregateSettings;
        this.singleProjectionCache = singleProjectionCache;
    }
    public async Task<T?> GetAggregateAsync<T, Q, P>(Guid aggregateId, int? toVersion = null)
        where T : IAggregateCommon, SingleProjections.ISingleProjection, ISingleProjectionStateConvertible<Q>
        where Q : IAggregateCommon
        where P : ISingleProjector<T>, new()
    {
        var savedContainer = singleProjectionCache.GetContainer<T, Q>(aggregateId);
        if (savedContainer == null)
        {
            return await GetAggregateWithoutCacheAsync<T, Q, P>(aggregateId, toVersion);
        }
        var projector = new P();
        if (savedContainer.SafeState is null && savedContainer?.SafeSortableUniqueId?.Value is null)
        {
            return await GetAggregateWithoutCacheAsync<T, Q, P>(aggregateId, toVersion);
        }
        var aggregate = projector.CreateInitialAggregate(aggregateId);
        aggregate.ApplySnapshot(savedContainer.SafeState!);

        if (_aggregateSettings.UseUpdateMarkerForType(projector.OriginalAggregateType().Name))
        {
            var (updated, type) = _updateNotice.HasUpdateAfter(
                projector.OriginalAggregateType().Name,
                aggregateId,
                savedContainer.SafeSortableUniqueId!);
            if (!updated)
            {
                return aggregate;
            }
        }
        var container = new SingleMemoryCacheProjectionContainer<T, Q>(aggregateId);
        await _documentRepository.GetAllEventsForAggregateIdAsync(
            aggregateId,
            projector.OriginalAggregateType(),
            PartitionKeyGenerator.ForEvent(aggregateId, projector.OriginalAggregateType()),
            savedContainer?.SafeSortableUniqueId?.Value,
            events =>
            {
                var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                foreach (var e in events)
                {
                    if (!string.IsNullOrWhiteSpace(savedContainer?.SafeSortableUniqueId?.Value) &&
                        string.CompareOrdinal(savedContainer?.SafeSortableUniqueId?.Value, e.SortableUniqueId) > 0)
                    {
                        throw new SekibanEventDuplicateException();
                    }
                    if (container.LastSortableUniqueId == null && e.GetSortableUniqueId().LaterThan(targetSafeId) && aggregate.Version > 0)
                    {
                        container.SafeState = aggregate.ToState();
                        container.SafeSortableUniqueId = container.SafeState.LastSortableUniqueId;
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
        container.State = aggregate.ToState();
        if (container.LastSortableUniqueId != null &&
            container.SafeSortableUniqueId == null &&
            container.LastSortableUniqueId?.EarlierThan(SortableUniqueIdValue.GetSafeIdFromUtc()) == true)
        {
            container.SafeState = container.State;
            container.SafeSortableUniqueId = container.LastSortableUniqueId;
        }
        if (container.SafeState is not null)
        {
            singleProjectionCache.SetContainer(aggregateId, container);
        }
        return aggregate;
    }

    private async Task<T?> GetAggregateWithoutCacheAsync<T, Q, P>(Guid aggregateId, int? toVersion = null)
        where T : IAggregateCommon, SingleProjections.ISingleProjection, ISingleProjectionStateConvertible<Q>
        where Q : IAggregateCommon
        where P : ISingleProjector<T>, new()
    {
        var projector = new P();
        var aggregate = projector.CreateInitialAggregate(aggregateId);
        var container = new SingleMemoryCacheProjectionContainer<T, Q>(aggregateId);

        var snapshotDocument = await _documentRepository.GetLatestSnapshotForAggregateAsync(aggregateId, typeof(T));
        var state = snapshotDocument is null ? default : snapshotDocument.ToState<Q>();
        if (state is not null)
        {
            aggregate.ApplySnapshot(state);
        }
        if (toVersion.HasValue && aggregate.Version >= toVersion.Value)
        {
            return await GetAggregateFromInitialAsync<T, Q, P>(aggregateId, toVersion.Value);
        }

        await _documentRepository.GetAllEventsForAggregateIdAsync(
            aggregateId,
            projector.OriginalAggregateType(),
            PartitionKeyGenerator.ForEvent(aggregateId, projector.OriginalAggregateType()),
            state?.LastSortableUniqueId,
            events =>
            {
                var someSafeId = SortableUniqueIdValue.Generate(SekibanDateProducer.GetRegistered().UtcNow, Guid.Empty);
                var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                foreach (var e in events)
                {
                    if (!string.IsNullOrWhiteSpace(state?.LastSortableUniqueId) &&
                        string.CompareOrdinal(state?.LastSortableUniqueId, e.SortableUniqueId) > 0)
                    {
                        throw new SekibanEventDuplicateException();
                    }
                    if (container.LastSortableUniqueId == null && e.GetSortableUniqueId().EarlierThan(targetSafeId) && aggregate.Version > 0)
                    {
                        container.SafeState = aggregate.ToState();
                        container.SafeSortableUniqueId = container.SafeState.LastSortableUniqueId;
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
        container.State = aggregate.ToState();
        if (container.LastSortableUniqueId != null &&
            container.SafeSortableUniqueId == null &&
            container.LastSortableUniqueId?.EarlierThan(SortableUniqueIdValue.GetSafeIdFromUtc()) == true)
        {
            container.SafeState = container.State;
            container.SafeSortableUniqueId = container.LastSortableUniqueId;
        }
        if (container.SafeState is not null)
        {
            singleProjectionCache.SetContainer(aggregateId, container);
        }
        return aggregate;
    }

    public async Task<T?> GetAggregateFromInitialAsync<T, Q, P>(Guid aggregateId, int? toVersion)
        where T : IAggregateCommon, SingleProjections.ISingleProjection, ISingleProjectionStateConvertible<Q>
        where Q : IAggregateCommon
        where P : ISingleProjector<T>, new()
    {
        var projector = new P();
        var container = new SingleMemoryCacheProjectionContainer<T, Q>(aggregateId);
        var aggregate = projector.CreateInitialAggregate(aggregateId);
        var addFinished = false;
        await _documentRepository.GetAllEventsForAggregateIdAsync(
            aggregateId,
            typeof(T),
            PartitionKeyGenerator.ForEvent(aggregateId, projector.OriginalAggregateType()),
            null,
            events =>
            {
                if (events.Count() != events.Select(m => m.Id).Distinct().Count())
                {
                    throw new SekibanEventDuplicateException();
                }
                if (addFinished) { return; }
                var someSafeId = SortableUniqueIdValue.Generate(SekibanDateProducer.GetRegistered().UtcNow, Guid.Empty);
                var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                foreach (var e in events)
                {
                    if (container.LastSortableUniqueId == null && e.GetSortableUniqueId().EarlierThan(targetSafeId) && aggregate.Version > 0)
                    {
                        container.SafeState = aggregate.ToState();
                        container.SafeSortableUniqueId = container.SafeState.LastSortableUniqueId;
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

        container.State = aggregate.ToState();
        if (container.LastSortableUniqueId != null &&
            container.SafeSortableUniqueId == null &&
            container.LastSortableUniqueId?.EarlierThan(SortableUniqueIdValue.GetSafeIdFromUtc()) == true)
        {
            container.SafeState = container.State;
            container.SafeSortableUniqueId = container.LastSortableUniqueId;
        }
        if (container.SafeState is not null)
        {
            singleProjectionCache.SetContainer(aggregateId, container);
        }
        return aggregate;
    }
}
