using Sekiban.Dcb.Common;
using Sekiban.Dcb.Orleans.Grains;
using Sekiban.Dcb.Orleans.Serialization;
using Sekiban.Dcb.Orleans.ServiceId;
using Sekiban.Dcb.Queries;
using Sekiban.Dcb.ServiceId;

namespace Sekiban.Dcb.Orleans;

/// <summary>
///     Shared Orleans query transport used by both public executor facades. The facades retain their own
///     result/error conversion; this class owns only grain selection, sortable-id waiting, serialization, and the
///     provider call.
/// </summary>
internal sealed class OrleansProjectionQueryExecutor
{
    private readonly IClusterClient _clusterClient;
    private readonly DcbDomainTypes _domainTypes;
    private readonly IServiceIdProvider _serviceIdProvider;
    private readonly SortableUniqueIdWaitPolicy _sortableUniqueIdWaitPolicy;

    internal OrleansProjectionQueryExecutor(
        IClusterClient clusterClient,
        DcbDomainTypes domainTypes,
        IServiceIdProvider serviceIdProvider,
        SortableUniqueIdWaitPolicy sortableUniqueIdWaitPolicy)
    {
        _clusterClient = clusterClient;
        _domainTypes = domainTypes;
        _serviceIdProvider = serviceIdProvider;
        _sortableUniqueIdWaitPolicy = sortableUniqueIdWaitPolicy;
    }

    internal async Task<SerializableQueryResult> ExecuteQueryAsync(
        object query,
        string projectorName,
        SortableUniqueIdWaitSurface surface)
    {
        var grain = GetGrain(projectorName);
        await WaitForSortableUniqueIdIfNeeded(grain, query, surface);

        var serializableQuery = await SerializableQueryParameter.CreateFromAsync(
            query,
            _domainTypes.JsonSerializerOptions);

        return await grain.ExecuteQueryAsync(serializableQuery);
    }

    internal async Task<SerializableListQueryResult> ExecuteListQueryAsync(
        object query,
        string projectorName,
        SortableUniqueIdWaitSurface surface)
    {
        var grain = GetGrain(projectorName);
        await WaitForSortableUniqueIdIfNeeded(grain, query, surface);

        var serializableQuery = await SerializableQueryParameter.CreateFromAsync(
            query,
            _domainTypes.JsonSerializerOptions);

        return await grain.ExecuteListQueryAsync(serializableQuery);
    }

    private IMultiProjectionGrain GetGrain(string projectorName)
    {
        var grainId = ServiceIdGrainKey.Build(_serviceIdProvider.GetCurrentServiceId(), projectorName);
        return _clusterClient.GetGrain<IMultiProjectionGrain>(grainId);
    }

    /// <summary>
    ///     Waits for a sortable unique ID to be processed when the query requests it. Strict marker queries fail
    ///     before serialization when the wait times out; legacy queries keep their fail-open behavior.
    /// </summary>
    private async Task WaitForSortableUniqueIdIfNeeded(
        IMultiProjectionGrain grain,
        object query,
        SortableUniqueIdWaitSurface surface)
    {
        if (query is not IWaitForSortableUniqueId waitForQuery ||
            string.IsNullOrEmpty(waitForQuery.WaitForSortableUniqueId))
        {
            return;
        }

        var sortableUniqueId = waitForQuery.WaitForSortableUniqueId;
        var strict = query is IStrictWaitForSortableUniqueId;
        var wait = await _sortableUniqueIdWaitPolicy.WaitAsync(
            sortableUniqueId,
            surface,
            strict ? SortableUniqueIdWaitMode.Strict : SortableUniqueIdWaitMode.Legacy,
            cancellationToken => ProbeSortableUniqueIdAsync(grain, sortableUniqueId, cancellationToken),
            strict
                ? cancellationToken => ReadCurrentSortableUniqueIdAsync(grain, cancellationToken)
                : null);

        if (strict && wait.TimedOut)
        {
            throw new SortableUniqueIdWaitTimeoutException(
                sortableUniqueId,
                wait.Timeout,
                wait.Elapsed,
                wait.LastObservedSortableUniqueId);
        }
    }

    private static async Task<bool> ProbeSortableUniqueIdAsync(
        IMultiProjectionGrain grain,
        string sortableUniqueId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await grain.IsSortableUniqueIdReceived(sortableUniqueId).ConfigureAwait(false);
    }

    private static async Task<string?> ReadCurrentSortableUniqueIdAsync(
        IMultiProjectionGrain grain,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var status = await grain.GetProjectionHeadStatusAsync().ConfigureAwait(false);
        return status.CurrentLastSortableUniqueId;
    }
}
