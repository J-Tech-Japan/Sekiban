using Orleans.Concurrency;

namespace Sekiban.Dcb.MaterializedView.Orleans;

public interface IMaterializedViewGrain : IGrainWithStringKey
{
    Task EnsureStartedAsync();
    Task RefreshAsync();
    Task<bool> IsSortableUniqueIdReceived(string sortableUniqueId);

    [AlwaysInterleave]
    Task<MaterializedViewGrainStatus> GetStatusAsync();

    Task RequestDeactivationAsync();
}
