using ResultBoxes;
using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Storage;

/// <summary>
///     Storage for multi projection safe state.
/// </summary>
public interface IMultiProjectionStateStore
{
    Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestForVersionAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default);

    Task<ResultBox<OptionalValue<MultiProjectionStateRecord>>> GetLatestAnyVersionAsync(
        string projectorName,
        CancellationToken cancellationToken = default);

    Task<ResultBox<bool>> UpsertAsync(
        MultiProjectionStateRecord record,
        int offloadThresholdBytes = 1_000_000,
        CancellationToken cancellationToken = default);

    Task<ResultBox<IReadOnlyList<ProjectorStateInfo>>> ListAllAsync(
        CancellationToken cancellationToken = default);

    Task<ResultBox<bool>> DeleteAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default);

    Task<ResultBox<int>> DeleteAllAsync(
        string? projectorName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Upserts a projection state from a stream. The default implementation buffers the
    ///     stream to byte[] and delegates to <see cref="UpsertAsync" />.
    ///     Cosmos/Postgres/DynamoDB implementations override this for true streaming offload.
    /// </summary>
    async Task<ResultBox<bool>> UpsertFromStreamAsync(
        MultiProjectionStateWriteRequest request,
        Stream stream,
        int offloadThresholdBytes,
        CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await stream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        var data = ms.ToArray();
        var record = request with { StateData = data };
        return await UpsertAsync(record.ToRecord(), offloadThresholdBytes, cancellationToken)
            .ConfigureAwait(false);
    }
}
