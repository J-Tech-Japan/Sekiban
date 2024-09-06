using ResultBoxes;
namespace Sekiban.Core.Query.QueryModel;

public interface INextGeneralListQuery<TQuery, TOutput> : INextGeneralQueryCommon<TOutput>,
    INextListQueryCommon<TQuery, TOutput> where TOutput : notnull where TQuery : INextGeneralListQuery<TQuery, TOutput>
{
    public ResultBox<IEnumerable<TOutput>> HandleFilter(IQueryContext context);
    public ResultBox<IEnumerable<TOutput>> HandleSort(IEnumerable<TOutput> filteredList, IQueryContext context);
}
