using ResultBoxes;
namespace Sekiban.Core.Query.QueryModel;

public interface INextGeneralQueryAsync< TOutput> : INextGeneralQueryCommon< TOutput>,
    INextQueryCommon<TOutput>, INextQueryAsyncGeneral where TOutput : notnull
{
    public Task<ResultBox<TOutput>> HandleFilterAsync( IQueryContext context);
}