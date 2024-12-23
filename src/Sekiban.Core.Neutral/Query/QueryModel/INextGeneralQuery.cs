using ResultBoxes;
namespace Sekiban.Core.Query.QueryModel;

public interface INextGeneralQuery<TQuery, TOutput> : INextGeneralQueryCommon<TOutput>,
    INextQueryCommon<TQuery, TOutput> where TQuery : INextGeneralQuery<TQuery, TOutput>, IEquatable<TQuery>
    where TOutput : notnull
{
    public static abstract ResultBox<TOutput> HandleFilter(TQuery query, IQueryContext context);
}
