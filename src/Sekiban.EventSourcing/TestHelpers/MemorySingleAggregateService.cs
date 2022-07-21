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
    public Task<T?> GetAggregateFromInitialDefaultAggregateAsync<T, TContents>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents =>
        GetAggregateAsync<T, TContents>(aggregateId, toVersion);
    public Task<AggregateDtoBase<TContents>?> GetAggregateFromInitialDefaultAggregateDtoAsync<T, TContents>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents =>
        GetAggregateDtoAsync<T, TContents>(aggregateId, toVersion);
    public async Task<T?> GetProjectionAsync<T>(Guid aggregateId, int? toVersion = null) where T : SingleAggregateProjectionBase<T>, new()
    {
        var aggregate = Aggregates.FirstOrDefault(m => m.GetType().Name == typeof(T).Name && m.AggregateId == aggregateId);
        await Task.CompletedTask;
        return (T?)aggregate;
    }
    public async Task<T?> GetAggregateAsync<T, TContents>(Guid aggregateId, int? toVersion = null) where T : TransferableAggregateBase<TContents>
        where TContents : IAggregateContents
    {
        var dto = await GetAggregateDtoAsync<T, TContents>(aggregateId, toVersion);
        if (dto == default) { return default; }
        var projection = new DefaultSingleAggregateProjector<T>();
        var aggregate = projection.CreateInitialAggregate(aggregateId);
        aggregate.ApplySnapshot(dto);
        return aggregate;
    }
    public async Task<AggregateDtoBase<TContents>?> GetAggregateDtoAsync<T, TContents>(Guid aggregateId, int? toVersion = null)
        where T : TransferableAggregateBase<TContents> where TContents : IAggregateContents
    {
        var aggregate = Aggregates.FirstOrDefault(m => m.Contents.GetType().Name == typeof(TContents).Name && m.AggregateId == aggregateId);
        await Task.CompletedTask;
        return (AggregateDtoBase<TContents>?)aggregate;
    }
}
