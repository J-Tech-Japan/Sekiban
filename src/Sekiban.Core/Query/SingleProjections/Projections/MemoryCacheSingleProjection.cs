using Sekiban.Core.Aggregate;
using Sekiban.Core.Cache;
using Sekiban.Core.Documents;
using Sekiban.Core.Documents.ValueObjects;
using Sekiban.Core.Events;
using Sekiban.Core.Exceptions;
using Sekiban.Core.Partition;
using Sekiban.Core.Query.UpdateNotice;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Core.Types;
namespace Sekiban.Core.Query.SingleProjections.Projections;

public class MemoryCacheSingleProjection : ISingleProjection
{
    private readonly IAggregateSettings _aggregateSettings;
    private readonly IDocumentRepository _documentRepository;
    private readonly SekibanAggregateTypes _sekibanAggregateTypes;
    private readonly IUpdateNotice _updateNotice;
    private readonly ISingleProjectionCache singleProjectionCache;
    public MemoryCacheSingleProjection(
        IDocumentRepository documentRepository,
        IUpdateNotice updateNotice,
        IAggregateSettings aggregateSettings,
        ISingleProjectionCache singleProjectionCache,
        SekibanAggregateTypes sekibanAggregateTypes)
    {
        _documentRepository = documentRepository;
        _updateNotice = updateNotice;
        _aggregateSettings = aggregateSettings;
        this.singleProjectionCache = singleProjectionCache;
        _sekibanAggregateTypes = sekibanAggregateTypes;
    }

    public async Task<TProjection?> GetAggregateAsync<TProjection, TState, TProjector>(
        Guid aggregateId,
        string rootPartitionKey = IDocument.DefaultRootPartitionKey,
        int? toVersion = null,
        SortableUniqueIdValue? includesSortableUniqueId = null)
        where TProjection : IAggregateCommon, SingleProjections.ISingleProjection, ISingleProjectionStateConvertible<TState>
        where TState : IAggregateStateCommon
        where TProjector : ISingleProjector<TProjection>, new()
    {
        var savedContainer = singleProjectionCache.GetContainer<TProjection, TState>(aggregateId);
        if (savedContainer == null)
        {
            return await GetAggregateWithoutCacheAsync<TProjection, TState, TProjector>(aggregateId, rootPartitionKey, toVersion);
        }
        var projector = new TProjector();
        if (savedContainer.SafeState is null && savedContainer?.SafeSortableUniqueId?.Value is null)
        {
            return await GetAggregateWithoutCacheAsync<TProjection, TState, TProjector>(aggregateId, rootPartitionKey, toVersion);
        }
        var aggregate = projector.CreateInitialAggregate(aggregateId);
        if (savedContainer.SafeState is not null && aggregate.CanApplySnapshot(savedContainer.SafeState))
        {
            aggregate.ApplySnapshot(savedContainer.SafeState);
        }
        if (includesSortableUniqueId is not null &&
            savedContainer?.SafeSortableUniqueId is not null &&
            includesSortableUniqueId.EarlierThan(savedContainer.SafeSortableUniqueId))
        {
            return aggregate;
        }

        if (_aggregateSettings.UseUpdateMarkerForType(projector.GetOriginalAggregatePayloadType().Name))
        {
            var (updated, type) = _updateNotice.HasUpdateAfter(
                projector.GetOriginalAggregatePayloadType().Name,
                aggregateId,
                savedContainer?.SafeSortableUniqueId!);
            if (!updated)
            {
                return aggregate;
            }
        }

        var container = new SingleMemoryCacheProjectionContainer<TProjection, TState> { AggregateId = aggregateId };

        try
        {
            await _documentRepository.GetAllEventsForAggregateIdAsync(
                aggregateId,
                projector.GetOriginalAggregatePayloadType(),
                PartitionKeyGenerator.ForEvent(aggregateId, projector.GetOriginalAggregatePayloadType(), rootPartitionKey),
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
                        if (container.LastSortableUniqueId == null && e.GetSortableUniqueId().LaterThanOrEqual(targetSafeId) && aggregate.Version > 0)
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
                        if (e.GetSortableUniqueId().LaterThanOrEqual(targetSafeId))
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
            return await GetAggregateWithoutCacheAsync<TProjection, TState, TProjector>(aggregateId, rootPartitionKey, toVersion);
        }
        if (aggregate.Version == 0)
        {
            return default;
        }
        if (toVersion.HasValue && aggregate.Version < toVersion.Value)
        {
            throw new SekibanVersionNotReachToSpecificVersion();
        }
        if (aggregate.IsAggregateType() && !aggregate.GetPayloadTypeIs(aggregate.GetAggregatePayloadTypeFromAggregate()))
        {
            return aggregate;
        }
        container = container with { State = aggregate.ToState() };
        if (container.LastSortableUniqueId != null &&
            container.SafeSortableUniqueId == null &&
            container.LastSortableUniqueId?.EarlierThan(SortableUniqueIdValue.GetSafeIdFromUtc()) == true)
        {
            container = container with { SafeState = container.State, SafeSortableUniqueId = container.LastSortableUniqueId };
        }

        if (container.SafeState is not null)
        {
            singleProjectionCache.SetContainer(aggregateId, container);
        }
        return aggregate;
    }

    private async Task<TProjection?>
        GetAggregateWithoutCacheAsync<TProjection, TState, TProjector>(Guid aggregateId, string rootPartitionKey, int? toVersion = null)
        where TProjection : IAggregateCommon, SingleProjections.ISingleProjection, ISingleProjectionStateConvertible<TState>
        where TState : IAggregateStateCommon
        where TProjector : ISingleProjector<TProjection>, new()
    {
        var projector = new TProjector();
        var aggregate = projector.CreateInitialAggregate(aggregateId);
        var container = new SingleMemoryCacheProjectionContainer<TProjection, TState> { AggregateId = aggregateId };
        var payloadVersion = projector.GetPayloadVersionIdentifier();
        var snapshotDocument = await _documentRepository.GetLatestSnapshotForAggregateAsync(
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
            return await GetAggregateFromInitialAsync<TProjection, TState, TProjector>(aggregateId, rootPartitionKey, toVersion.Value);
        }

        await _documentRepository.GetAllEventsForAggregateIdAsync(
            aggregateId,
            projector.GetOriginalAggregatePayloadType(),
            PartitionKeyGenerator.ForEvent(aggregateId, projector.GetOriginalAggregatePayloadType(), rootPartitionKey),
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
                    if (e.GetSortableUniqueId().LaterThanOrEqual(targetSafeId))
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
        if (aggregate.IsAggregateType() && !aggregate.GetPayloadTypeIs(aggregate.GetAggregatePayloadTypeFromAggregate()))
        {
            return aggregate;
        }
        container = container with { State = aggregate.ToState() };
        if (container.LastSortableUniqueId != null &&
            container.SafeSortableUniqueId == null &&
            container.LastSortableUniqueId?.EarlierThan(SortableUniqueIdValue.GetSafeIdFromUtc()) == true)
        {
            container = container with { SafeState = container.State, SafeSortableUniqueId = container.LastSortableUniqueId };
        }

        if (container.SafeState is not null)
        {
            singleProjectionCache.SetContainer(aggregateId, container);
        }
        return aggregate;
    }

    public async Task<TProjection?>
        GetAggregateFromInitialAsync<TProjection, TState, TProjector>(Guid aggregateId, string rootPartitionKey, int? toVersion)
        where TProjection : IAggregateCommon, SingleProjections.ISingleProjection, ISingleProjectionStateConvertible<TState>
        where TState : IAggregateStateCommon
        where TProjector : ISingleProjector<TProjection>, new()
    {
        var projector = new TProjector();
        var container = new SingleMemoryCacheProjectionContainer<TProjection, TState> { AggregateId = aggregateId };
        var aggregate = projector.CreateInitialAggregate(aggregateId);
        var addFinished = false;
        await _documentRepository.GetAllEventsForAggregateIdAsync(
            aggregateId,
            projector.GetOriginalAggregatePayloadType(),
            PartitionKeyGenerator.ForEvent(aggregateId, projector.GetOriginalAggregatePayloadType(), rootPartitionKey),
            null,
            events =>
            {
                events = events?.ToList() ?? new List<IEvent>();
                if (events.Count() != events.Select(m => m.Id).Distinct().Count())
                {
                    throw new SekibanEventDuplicateException();
                }
                if (addFinished)
                {
                    return;
                }
                var someSafeId = SortableUniqueIdValue.Generate(SekibanDateProducer.GetRegistered().UtcNow, Guid.Empty);
                var targetSafeId = SortableUniqueIdValue.GetSafeIdFromUtc();
                foreach (var e in events)
                {
                    if (container.LastSortableUniqueId == null && e.GetSortableUniqueId().EarlierThan(targetSafeId) && aggregate.Version > 0)
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
                    if (e.GetSortableUniqueId().LaterThanOrEqual(targetSafeId))
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
            container.LastSortableUniqueId?.EarlierThan(SortableUniqueIdValue.GetSafeIdFromUtc()) == true)
        {
            container = container with { SafeState = container.State, SafeSortableUniqueId = container.LastSortableUniqueId };
        }

        if (container.SafeState is not null)
        {
            singleProjectionCache.SetContainer(aggregateId, container);
        }
        return aggregate;
    }
}
