using ResultBoxes;

namespace Sekiban.Dcb.Actors;

/// <summary>
///     Passive projection status reader.  Implementations compose registry rows with event-store counts and must never
///     resolve or invoke a projection grain.
/// </summary>
public interface IProjectionStatusReader
{
    Task<ResultBox<IReadOnlyList<ProjectionStatusSnapshot>>> ReadAsync(
        ProjectionStatusReadRequest? request = null,
        CancellationToken cancellationToken = default);

    Task<ResultBox<IReadOnlyList<ProjectionStatusSnapshot>>> GetSnapshotsAsync(
        ProjectionStatusReadRequest? request = null,
        CancellationToken cancellationToken = default) =>
        ReadAsync(request, cancellationToken);
}

/// <summary>
///     Additive serialized boundary for passive projection status.  It is deliberately separate from
///     <see cref="ISerializedSekibanDcbExecutor"/> so existing WASM implementors remain source and binary compatible.
/// </summary>
public interface ISerializedProjectionStatusReader
{
    Task<ResultBox<byte[]>> ReadSerializedAsync(
        ProjectionStatusReadRequest? request = null,
        CancellationToken cancellationToken = default);

    Task<ResultBox<byte[]>> GetSerializedSnapshotsAsync(
        ProjectionStatusReadRequest? request = null,
        CancellationToken cancellationToken = default) =>
        ReadSerializedAsync(request, cancellationToken);
}
