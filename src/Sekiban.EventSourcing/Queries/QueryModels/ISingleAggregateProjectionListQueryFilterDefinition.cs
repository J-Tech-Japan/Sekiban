using Sekiban.EventSourcing.Queries.QueryModels.Parameters;
using Sekiban.EventSourcing.Queries.SingleAggregates;
namespace Sekiban.EventSourcing.Queries.QueryModels;

public interface
    ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, in TSingleAggregateProjection, TSingleAggregateProjectionContents, in TQueryParam,
        TResponseQueryModel> where TAggregate : AggregateCommonBase, new()
    where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>
    where TSingleAggregateProjectionContents : ISingleAggregateProjectionContents
    where TQueryParam : IQueryParameter
{
    public IEnumerable<TResponseQueryModel> HandleFilter(
        TQueryParam queryParam,
        IEnumerable<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>> list);
    public IEnumerable<TResponseQueryModel> HandleSort(TQueryParam queryParam, IEnumerable<TResponseQueryModel> projections);
}
