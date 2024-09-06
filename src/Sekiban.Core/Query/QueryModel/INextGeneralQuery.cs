using ResultBoxes;
namespace Sekiban.Core.Query.QueryModel;

public interface INextGeneralQuery<TQuery, TOutput> : INextGeneralQueryCommon<TOutput>,
    INextQueryCommon<TQuery, TOutput> where TQuery : INextGeneralQuery<TQuery, TOutput> where TOutput : notnull
{
    public ResultBox<TOutput> HandleFilter(IQueryContext context);
}
