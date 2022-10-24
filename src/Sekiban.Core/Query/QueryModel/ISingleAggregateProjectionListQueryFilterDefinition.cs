using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ISingleAggregateProjectionListQueryFilterDefinition<TAggregate, in TSingleAggregateProjection, TSingleAggregateProjectionContents, in TQueryParam,
        TResponseQueryModel> where TAggregate : AggregateCommonBase, new()
    where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>
    where TSingleAggregateProjectionContents : ISingleAggregateProjectionPayload
    where TQueryParam : IQueryParameter
{
    public IEnumerable<TResponseQueryModel> HandleFilter(
        TQueryParam queryParam,
        IEnumerable<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>> list);
    public IEnumerable<TResponseQueryModel> HandleSort(TQueryParam queryParam, IEnumerable<TResponseQueryModel> projections);
}
