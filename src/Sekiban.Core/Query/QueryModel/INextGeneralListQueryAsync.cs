using ResultBoxes;
namespace Sekiban.Core.Query.QueryModel;

public interface INextGeneralListQueryAsync<TOutput> : INextGeneralQueryCommon<TOutput>,
    INextListQueryCommon<TOutput>,
    INextQueryAsyncGeneral where TOutput : notnull
{
    public Task<ResultBox<IEnumerable<TOutput>>> HandleFilterAsync(IQueryContext context);
    public Task<ResultBox<IEnumerable<TOutput>>> HandleSortAsync(
        IEnumerable<TOutput> filteredList,
        IQueryContext context);
}
