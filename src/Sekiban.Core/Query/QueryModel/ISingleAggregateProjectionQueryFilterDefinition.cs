using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.QueryModel.Parameters;
using Sekiban.Core.Query.SingleAggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface
    ISingleAggregateProjectionQueryFilterDefinition<TAggregate, in TSingleAggregateProjection, TSingleAggregateProjectionContents, in TQueryParam,
        TResponseQueryModel> where TAggregate : IAggregatePayload, new()
    where TSingleAggregateProjection : SingleAggregateProjectionBase<TAggregate, TSingleAggregateProjection, TSingleAggregateProjectionContents>
    where TSingleAggregateProjectionContents : ISingleAggregateProjectionPayload
    where TQueryParam : IQueryParameter
{
    public TResponseQueryModel HandleFilter(
        TQueryParam queryParam,
        IEnumerable<SingleAggregateProjectionDto<TSingleAggregateProjectionContents>> list);
}
