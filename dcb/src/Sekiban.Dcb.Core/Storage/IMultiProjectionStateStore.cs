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
    ///     Opens a read stream for the snapshot payload represented by <paramref name="record" />.
    ///     Implementations are expected to resolve payload location from metadata (inline storage or offload key)
    ///     and return a readable stream.
    /// </summary>
    Task<ResultBox<Stream>> OpenStateDataReadStreamAsync(
        MultiProjectionStateRecord record,
        CancellationToken cancellationToken = default)
    {
        return Task.FromResult(ResultBox.Error<Stream>(
            new NotSupportedException(
                $"OpenStateDataReadStreamAsync is not implemented by {GetType().Name}. "
                + "The store must provide a payload stream implementation for metadata-only records.")));
    }

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
        await stream.CopyToAsync(Stream.Null, cancellationToken).ConfigureAwait(false);
        return ResultBox.Error<bool>(
            new NotSupportedException(
                $"UpsertFromStreamAsync is not implemented by {GetType().Name}. "
                + "Implement this method in the store to persist snapshot payload streams."));
    }
}
