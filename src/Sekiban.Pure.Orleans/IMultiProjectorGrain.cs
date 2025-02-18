using Sekiban.Pure.Projectors;
using Sekiban.Pure.Query;
namespace Sekiban.Pure.Orleans;

public interface IMultiProjectorGrain : IGrainWithStringKey
{
    Task RebuildStateAsync();
    Task BuildStateAsync();
    Task<MultiProjectionState> GetStateAsync();
    Task<QueryResultGeneral> QueryAsync(IQueryCommon query);
    Task<IListQueryResult> QueryAsync(IListQueryCommon query);
}