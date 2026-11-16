using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Storage;

/// <summary>
///     Storage for multi projection safe state.
/// </summary>
public interface IMultiProjectionStateStore
{
    /// <summary>Returns the stored record for the projector version, or <c>null</c> when none is stored.</summary>
    Task<MultiProjectionStateRecord?> GetLatestForVersionAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the latest stored record of any version, or <c>null</c> when none is stored.</summary>
    Task<MultiProjectionStateRecord?> GetLatestAnyVersionAsync(
        string projectorName,
        CancellationToken cancellationToken = default);

    Task<bool> UpsertAsync(
        MultiProjectionStateRecord record,
        int offloadThresholdBytes = 1_000_000,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ProjectorStateInfo>> ListAllAsync(
        CancellationToken cancellationToken = default);

    Task<bool> DeleteAsync(
        string projectorName,
        string projectorVersion,
        CancellationToken cancellationToken = default);

    Task<int> DeleteAllAsync(
        string? projectorName = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Opens a read stream for the snapshot payload represented by <paramref name="record" />.
    ///     Implementations are expected to resolve payload location from metadata (inline storage or offload key)
    ///     and return a readable stream.
    /// </summary>
    Task<Stream> OpenStateDataReadStreamAsync(
        MultiProjectionStateRecord record,
        CancellationToken cancellationToken = default)
    {
        return Task.FromException<Stream>(
            new NotSupportedException(
                $"OpenStateDataReadStreamAsync is not implemented by {GetType().Name}. "
                + "The store must provide a payload stream implementation for metadata-only records."));
    }

    /// <summary>
    ///     Upserts a projection state from a stream.
    ///     The default implementation is not supported and must be overridden by concrete stores.
    /// </summary>
    Task<bool> UpsertFromStreamAsync(
        MultiProjectionStateWriteRequest request,
        Stream stream,
        int offloadThresholdBytes,
        CancellationToken cancellationToken = default)
    {
        return Task.FromException<bool>(
            new NotSupportedException(
                $"UpsertFromStreamAsync is not implemented by {GetType().Name}. "
                + "Implement this method in the store to persist snapshot payload streams."));
    }
}
