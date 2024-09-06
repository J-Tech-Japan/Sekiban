using ResultBoxes;
namespace Sekiban.Core.Query.QueryModel;

public interface INextGeneralListQueryAsync<TQuery, TOutput> : INextGeneralQueryCommon<TOutput>,
    INextListQueryCommon<TQuery, TOutput>,
    INextQueryAsyncGeneral where TOutput : notnull where TQuery : INextGeneralListQueryAsync<TQuery, TOutput>
{
    public Task<ResultBox<IEnumerable<TOutput>>> HandleFilterAsync(IQueryContext context);
    public Task<ResultBox<IEnumerable<TOutput>>> HandleSortAsync(
        IEnumerable<TOutput> filteredList,
        IQueryContext context);
}
