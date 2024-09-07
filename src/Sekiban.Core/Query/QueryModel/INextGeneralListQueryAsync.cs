using ResultBoxes;
namespace Sekiban.Core.Query.QueryModel;

public interface INextGeneralListQueryAsync<TQuery, TOutput> : INextGeneralQueryCommon<TOutput>,
    INextListQueryCommon<TQuery, TOutput>,
    INextQueryAsyncGeneral where TOutput : notnull where TQuery : INextGeneralListQueryAsync<TQuery, TOutput>
{
    public static abstract Task<ResultBox<IEnumerable<TOutput>>> HandleFilterAsync(TQuery query, IQueryContext context);
    public static abstract Task<ResultBox<IEnumerable<TOutput>>> HandleSortAsync(
        IEnumerable<TOutput> filteredList,
        TQuery query,
        IQueryContext context);
}
