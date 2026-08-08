using ResultBoxes;

namespace Sekiban.Dcb.Storage;

/// <summary>
///     Durable storage for passive projection heartbeat rows. Implementations must enforce the expected-sequence
///     compare-and-set atomically for a single (service, projector, version, cluster) row. ActivationId is data in
///     that row, so a replacement activation cannot create a second physical row or bypass the CAS fence.
/// </summary>
public interface IProjectionStatusStore
{
    Task<ResultBox<ProjectionStatusWriteResult>> UpsertAsync(
        ProjectionStatusHeartbeat heartbeat,
        long expectedSequence,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Convenience overload for a heartbeat whose sequence is the next sequence after the caller's last write.
    /// </summary>
    Task<ResultBox<ProjectionStatusWriteResult>> UpsertAsync(
        ProjectionStatusHeartbeat heartbeat,
        CancellationToken cancellationToken = default) =>
        UpsertAsync(heartbeat, Math.Max(0, heartbeat.Sequence - 1), cancellationToken);

    Task<ResultBox<IReadOnlyList<ProjectionStatusHeartbeat>>> ListAsync(
        string? projectorName = null,
        string? projectorVersion = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    ///     Compatibility alias for providers and hosts that call the read operation <c>ListAllAsync</c>.
    /// </summary>
    Task<ResultBox<IReadOnlyList<ProjectionStatusHeartbeat>>> ListAllAsync(
        CancellationToken cancellationToken = default) =>
        ListAsync(cancellationToken: cancellationToken);
}
