using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.TestHelpers;

public class MemorySingleAggregateService : ISingleAggregateService
{
    public List<dynamic> Aggregates { get; init; } = new();
    public async Task<T?> GetAggregateFromInitialAsync<T, P>(Guid aggregateId, int? toVersion) where T : ISingleAggregate, ISingleAggregateProjection
        where P : ISingleAggregateProjector<T>, new()
    {
        var aggregate = Aggregates.FirstOrDefault(m => m.GetType().Name == typeof(T).Name && m.AggregateId == aggregateId);
        await Task.CompletedTask;
        return (T?)aggregate;
    }
    public Task<T?> GetAggregateFromInitialDefaultAggregateAsync<T, Q>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase =>
        GetAggregateAsync<T, Q>(aggregateId, toVersion);
    public Task<Q?> GetAggregateFromInitialDefaultAggregateDtoAsync<T, Q>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase =>
        GetAggregateDtoAsync<T, Q>(aggregateId, toVersion);
    public async Task<T?> GetProjectionAsync<T>(Guid aggregateId, int? toVersion = null) where T : SingleAggregateProjectionBase<T>, new()
    {
        var aggregate = Aggregates.FirstOrDefault(m => m.GetType().Name == typeof(T).Name && m.AggregateId == aggregateId);
        await Task.CompletedTask;
        return (T?)aggregate;
    }
    public async Task<T?> GetAggregateAsync<T, Q>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase
    {
        var dto = await GetAggregateDtoAsync<T, Q>(aggregateId, toVersion);
        if (dto == default) { return default; }
        var projection = new DefaultSingleAggregateProjector<T>();
        var aggregate = projection.CreateInitialAggregate(aggregateId);
        aggregate.ApplySnapshot(dto);
        return aggregate;
    }
    public async Task<Q?> GetAggregateDtoAsync<T, Q>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<Q> where Q : AggregateDtoBase
    {
        var aggregate = Aggregates.FirstOrDefault(m => m.GetType().Name == typeof(Q).Name && m.AggregateId == aggregateId);
        await Task.CompletedTask;
        return (Q?)aggregate;
    }
}
