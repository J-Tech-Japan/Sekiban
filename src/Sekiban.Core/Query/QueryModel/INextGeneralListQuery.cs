using ResultBoxes;
namespace Sekiban.Core.Query.QueryModel;

public interface INextGeneralListQuery<TOutput> : INextGeneralQueryCommon<TOutput>, INextListQueryCommon<TOutput> where TOutput : notnull
{
    public ResultBox<IEnumerable<TOutput>> HandleFilter(IQueryContext context);
    public ResultBox<IEnumerable<TOutput>> HandleSort(IEnumerable<TOutput> filteredList, IQueryContext context);
}