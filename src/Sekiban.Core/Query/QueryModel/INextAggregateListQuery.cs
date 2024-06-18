using ResultBoxes;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Query.SingleProjections;
namespace Sekiban.Core.Query.QueryModel;

public interface INextAggregateListQuery<TAggregatePayload, TOutput> : INextAggregateQueryCommon<TAggregatePayload, TOutput>,
    INextListQueryCommon<TOutput> where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon
{
    public QueryListType QueryListType => QueryListType.ActiveOnly;
    public ResultBox<IEnumerable<TOutput>> HandleFilter(IEnumerable<AggregateState<TAggregatePayload>> list, IQueryContext context);
    public ResultBox<IEnumerable<TOutput>> HandleSort(IEnumerable<TOutput> filteredList, IQueryContext context);
}
public interface
    INextSingleProjectionListQuery<TSingleProjectionPayloadCommon, TOutput> :
    INextSingleProjectionQueryCommon<TSingleProjectionPayloadCommon, TOutput>,
    INextListQueryCommon<TOutput> where TOutput : notnull where TSingleProjectionPayloadCommon : ISingleProjectionPayloadCommon
{
    public QueryListType QueryListType => QueryListType.ActiveOnly;
    public ResultBox<IEnumerable<TOutput>> HandleFilter(
        IEnumerable<SingleProjectionState<TSingleProjectionPayloadCommon>> list,
        IQueryContext context);
    public ResultBox<IEnumerable<TOutput>> HandleSort(IEnumerable<TOutput> filteredList, IQueryContext context);
}
