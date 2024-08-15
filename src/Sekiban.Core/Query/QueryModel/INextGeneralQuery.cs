using ResultBoxes;
namespace Sekiban.Core.Query.QueryModel;

public interface INextGeneralQuery<TOutput> : INextGeneralQueryCommon<TOutput>, INextQueryCommon<TOutput>
    where TOutput : notnull
{
    public ResultBox<TOutput> HandleFilter(IQueryContext context);
}
