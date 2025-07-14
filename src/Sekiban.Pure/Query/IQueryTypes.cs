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
    
    /// <summary>
    /// Gets the type from the payload type name。
    /// </summary>
    /// <param name="payloadTypeName">Name of the payload type</param>
    /// <returns>The found type, or null if not found</returns>
    public Type? GetPayloadTypeByName(string payloadTypeName);
}
