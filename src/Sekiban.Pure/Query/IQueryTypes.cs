using ResultBoxes;
using Sekiban.Pure.Projectors;
namespace Sekiban.Pure.Query;

public interface IQueryTypes
{
    public IEnumerable<Type> GetQueryTypes();
    public IEnumerable<Type> GetQueryResponseTypes();
    public Task<ResultBox<IQueryResult>> ExecuteAsQueryResult(
        IQueryCommon query,
        Func<IMultiProjectionEventSelector, Task<ResultBox<IMultiProjectorStateCommon>>> repositoryLoader,
        IServiceProvider serviceProvider);

    public Task<ResultBox<IListQueryResult>> ExecuteAsQueryResult(
        IListQueryCommon query,
        Func<IMultiProjectionEventSelector, Task<ResultBox<IMultiProjectorStateCommon>>> repositoryLoader,
        IServiceProvider serviceProvider);

    public ResultBox<IQueryResult> ToTypedQueryResult(QueryResultGeneral general);
    public ResultBox<IListQueryResult> ToTypedListQueryResult(ListQueryResultGeneral general);
    public ResultBox<IMultiProjectorCommon> GetMultiProjector(IQueryCommon query);
    public ResultBox<IMultiProjectorCommon> GetMultiProjector(IListQueryCommon query);
}
