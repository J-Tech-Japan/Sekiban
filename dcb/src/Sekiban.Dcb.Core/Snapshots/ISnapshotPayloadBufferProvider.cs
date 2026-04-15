namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Provides a writable, seekable stream used by the stream-first multi-projection
///     snapshot persistence path to buffer the serialized payload before the
///     inline/offload decision is made.
///     Implementations must return a stream that supports <see cref="Stream.Length" /> and
///     <see cref="Stream.Position" /> after write completion, so the caller can either
///     rewind and upload the payload to blob storage or rewind and materialize it for
///     inline envelope JSON emission.
/// </summary>
public interface ISnapshotPayloadBufferProvider
{
    /// <summary>
    ///     Create a new empty buffer that will receive the serialized snapshot payload.
    ///     The returned buffer must be disposed by the caller once it is no longer needed.
    /// </summary>
    /// <param name="projectorName">Logical projector name (used for diagnostics / naming).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ISnapshotPayloadBuffer> CreateBufferAsync(string projectorName, CancellationToken cancellationToken = default);
}

/// <summary>
///     A transient buffer used by the stream-first snapshot persistence path.
///     The underlying <see cref="Stream" /> must be seekable and writable; reading and
///     rewinding is done by the consumer after the serialized payload has been written.
///     Disposing the buffer is expected to clean up any backing resources (e.g. temp file).
/// </summary>
public interface ISnapshotPayloadBuffer : IAsyncDisposable, IDisposable
{
    /// <summary>
    ///     The writable/seekable stream receiving the serialized snapshot payload.
    /// </summary>
    Stream Stream { get; }

    /// <summary>
    ///     A short, human-readable description of where the buffer is stored (e.g. "memory", "tempfile:/tmp/...").
    ///     Intended for diagnostics and tests; not used for routing decisions.
    /// </summary>
    string Location { get; }
}
