using Sekiban.EventSourcing.Queries.MultipleAggregates;
using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.QueryModels;

public interface IQueryFilterService
{
    public Task<TQueryFilterResponse>
        GetProjectionQueryFilterAsync<TProjection, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param)
        where TProjection : MultipleAggregateProjectionBase<TProjection>, IMultipleAggregateProjectionDto, new()
        where TQueryFilter : IProjectionQueryFilterDefinition<TProjection, TQueryFilterParameter, TQueryFilterResponse>, new()
        where TQueryFilterParameter : IQueryParameter, new()
        where TQueryFilterResponse : new();
    public Task<IEnumerable<TQueryFilterResponse>>
        GetProjectionListQueryFilterAsync<TProjection, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(TQueryFilterParameter param)
        where TProjection : MultipleAggregateProjectionBase<TProjection>, IMultipleAggregateProjectionDto, new()
        where TQueryFilter : IProjectionListQueryFilterDefinition<TProjection, TQueryFilterParameter, TQueryFilterResponse>, new()
        where TQueryFilterParameter : IQueryParameter, new()
        where TQueryFilterResponse : new();
    public Task<IEnumerable<TQueryFilterResponse>>
        GetAggregateListQueryFilterAsync<TAggregate, TAggregateContents, TQueryFilter, TQueryFilterParameter, TQueryFilterResponse>(
            TQueryFilterParameter param) where TAggregate : TransferableAggregateBase<TAggregateContents>
        where TAggregateContents : IAggregateContents, new()
        where TQueryFilter : IAggregateListQueryFilterDefinition<TAggregate, TAggregateContents, TQueryFilterParameter, TQueryFilterResponse>, new()
        where TQueryFilterParameter : IQueryParameter;
    public Task<IEnumerable<TQueryFilterResponse>>
        GetSingleAggregateProjectionListQueryFilterAsync<TAggregate, TSingleAggregateProjection, TQueryFilter, TQueryFilterParameter,
            TQueryFilterResponse>(TQueryFilterParameter param) where TAggregate : AggregateBase, new()
        where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection>, new()
        where TQueryFilter : ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, TSingleAggregateProjection, TQueryFilterParameter,
            TQueryFilterResponse>, new()
        where TQueryFilterParameter : IQueryParameter, new()
        where TQueryFilterResponse : new();
}
