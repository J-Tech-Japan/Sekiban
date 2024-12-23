using ResultBoxes;
namespace Sekiban.Core.Query.QueryModel;

public interface INextGeneralListQuery<TQuery, TOutput> : INextGeneralQueryCommon<TOutput>,
    INextListQueryCommon<TQuery, TOutput> where TOutput : notnull
    where TQuery : INextGeneralListQuery<TQuery, TOutput>, IEquatable<TQuery>
{
    public static abstract ResultBox<IEnumerable<TOutput>> HandleFilter(TQuery query, IQueryContext context);
    public static abstract ResultBox<IEnumerable<TOutput>> HandleSort(
        IEnumerable<TOutput> filteredList,
        TQuery query,
        IQueryContext context);
}
