using ResultBoxes;
using Sekiban.Dcb.MultiProjections;
namespace Sekiban.Dcb.Orleans.Grains;

/// <summary>
///     Helper service for working with multi-projection grains
/// </summary>
public class MultiProjectionGrainService
{
    private readonly IClusterClient _clusterClient;

    public MultiProjectionGrainService(IClusterClient clusterClient) =>
        _clusterClient = clusterClient ?? throw new ArgumentNullException(nameof(clusterClient));

    /// <summary>
    ///     Get the state of a multi-projection
    /// </summary>
    public async Task<ResultBox<MultiProjectionState>> GetProjectionStateAsync(
        string projectorName,
        bool canGetUnsafeState = true)
    {
        var grain = _clusterClient.GetMultiProjectionGrain(projectorName);
        return await grain.GetStateAsync(canGetUnsafeState);
    }

    /// <summary>
    ///     Get the status of a multi-projection grain
    /// </summary>
    public async Task<MultiProjectionGrainStatus> GetProjectionStatusAsync(string projectorName)
    {
        var grain = _clusterClient.GetMultiProjectionGrain(projectorName);
        return await grain.GetStatusAsync();
    }

    /// <summary>
    ///     Force persist the state of a multi-projection
    /// </summary>
    public async Task<ResultBox<bool>> PersistProjectionStateAsync(string projectorName)
    {
        var grain = _clusterClient.GetMultiProjectionGrain(projectorName);
        return await grain.PersistStateAsync();
    }

    /// <summary>
    ///     Start or restart subscription for a multi-projection
    /// </summary>
    public async Task StartProjectionSubscriptionAsync(string projectorName)
    {
        var grain = _clusterClient.GetMultiProjectionGrain(projectorName);
        await grain.StartSubscriptionAsync();
    }

    /// <summary>
    ///     Stop subscription for a multi-projection
    /// </summary>
    public async Task StopProjectionSubscriptionAsync(string projectorName)
    {
        var grain = _clusterClient.GetMultiProjectionGrain(projectorName);
        await grain.StopSubscriptionAsync();
    }

    /// <summary>
    ///     Get status of all known multi-projections
    /// </summary>
    public async Task<Dictionary<string, MultiProjectionGrainStatus>> GetAllProjectionStatusesAsync(
        IEnumerable<string> projectorNames)
    {
        var tasks = projectorNames.Select(async name =>
        {
            try
            {
                var status = await GetProjectionStatusAsync(name);
                return (name, status);
            }
            catch
            {
                // Return a default status if grain fails
                return (name,
                    new MultiProjectionGrainStatus(
                        name,
                        false,
                        false,
                        null,
                        0,
                        null,
                        null,
                        0,
                        true,
                        "Failed to get status"));
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.ToDictionary(r => r.name, r => r.Item2);
    }
}
