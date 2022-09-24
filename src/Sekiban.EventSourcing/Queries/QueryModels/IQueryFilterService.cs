using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.QueryModels;

public interface IQueryFilterService
{
    public Task<TQueryFilterResponse>
        GetProjectionQueryFilterAsync<TProjection, TQueryFilter, TQueryFilterParam, TQueryFilterResponse>(TQueryFilterParam param)
        where TProjection : MultipleAggregateProjectionBase<TProjection>, IMultipleAggregateProjectionDto, new()
        where TQueryFilter : IProjectionQueryFilterDefinition<TProjection, TQueryFilterParam, TQueryFilterResponse>, new()
        where TQueryFilterParam : IQueryParameter, new()
        where TQueryFilterResponse : new();
    public Task<IEnumerable<TQueryFilterResponse>>
        GetProjectionListQueryFilterAsync<TProjection, TQueryFilter, TQueryFilterParam, TQueryFilterResponse>(TQueryFilterParam param)
        where TProjection : MultipleAggregateProjectionBase<TProjection>, IMultipleAggregateProjectionDto, new()
        where TQueryFilter : IProjectionListQueryFilterDefinition<TProjection, TQueryFilterParam, TQueryFilterResponse>, new()
        where TQueryFilterParam : IQueryParameter, new()
        where TQueryFilterResponse : new();
    public Task<IEnumerable<TQueryFilterResponse>>
        GetAggregateListQueryFilterAsync<TAggregate, TAggregateContents, TQueryFilter, TQueryFilterParam, TQueryFilterResponse>(
            TQueryFilterParam param) where TAggregate : TransferableAggregateBase<TAggregateContents>
        where TAggregateContents : IAggregateContents, new()
        where TQueryFilter : IAggregateDtoListQueryFilterDefinition<TAggregateContents, IQueryParameter, TQueryFilterResponse>, new()
        where TQueryFilterParam : IQueryParameter, new()
        where TQueryFilterResponse : new();
    public Task<IEnumerable<TQueryFilterResponse>>
        GetSingleAggregateProjectionListQueryFilterAsync<TAggregate, TSingleAggregateProjection, TQueryFilter, TQueryFilterParam,
            TQueryFilterResponse>(TQueryFilterParam param) where TAggregate : AggregateBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection>, new()
        where TQueryFilter : ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, TSingleAggregateProjection, IQueryParameter,
            TQueryFilterResponse>, new()
        where TQueryFilterParam : IQueryParameter, new()
        where TQueryFilterResponse : new();
}
