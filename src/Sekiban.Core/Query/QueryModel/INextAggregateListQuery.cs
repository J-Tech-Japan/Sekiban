using ResultBoxes;
using Sekiban.Core.Aggregate;
namespace Sekiban.Core.Query.QueryModel;

public interface INextAggregateListQuery<TAggregatePayload, TOutput> : INextAggregateQueryCommon<TAggregatePayload, TOutput>,
    INextListQueryCommon<TOutput> where TOutput : notnull where TAggregatePayload : IAggregatePayloadCommon
{
    public QueryListType QueryListType => QueryListType.ActiveOnly;
    public ResultBox<IEnumerable<TOutput>> HandleFilter(IEnumerable<AggregateState<TAggregatePayload>> list, IQueryContext context);
    public ResultBox<IEnumerable<TOutput>> HandleSort(IEnumerable<TOutput> filteredList, IQueryContext context);
}
public interface INextGeneralListQuery<TOutput> : INextGeneralQueryCommon<TOutput>, INextListQueryCommon<TOutput> where TOutput : notnull
{
    public ResultBox<IEnumerable<TOutput>> HandleFilter(IQueryContext context);
    public ResultBox<IEnumerable<TOutput>> HandleSort(IEnumerable<TOutput> filteredList, IQueryContext context);
}
