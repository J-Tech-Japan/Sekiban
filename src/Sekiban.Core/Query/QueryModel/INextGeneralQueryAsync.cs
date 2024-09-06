using ResultBoxes;
namespace Sekiban.Core.Query.QueryModel;

public interface INextGeneralQueryAsync<TQuery, TOutput> : INextGeneralQueryCommon<TOutput>,
    INextQueryCommon<TQuery, TOutput>,
    INextQueryAsyncGeneral where TOutput : notnull where TQuery : INextGeneralQueryAsync<TQuery, TOutput>
{
    public Task<ResultBox<TOutput>> HandleFilterAsync(IQueryContext context);
}
