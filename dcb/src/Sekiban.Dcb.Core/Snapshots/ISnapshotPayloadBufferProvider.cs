namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Provides a readable, writable, seekable stream used by the stream-first
///     multi-projection snapshot persistence path to buffer the serialized payload
///     before the inline/offload decision is made.
///     Implementations must return a stream that supports <see cref="Stream.Length" /> and
///     <see cref="Stream.Position" /> after write completion, so the caller can either
///     rewind and upload the payload to blob storage or rewind and materialize it for
///     inline envelope JSON emission. Because both of those follow-up operations need to
///     re-read the serialized payload, the underlying stream MUST report
///     <see cref="Stream.CanRead" /> == true in addition to <see cref="Stream.CanWrite" />
///     and <see cref="Stream.CanSeek" />.
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
///     The underlying <see cref="Stream" /> must be readable, writable, and seekable:
///     the consumer writes the serialized payload, rewinds the stream, and then either
///     reads it back for inline envelope emission or uploads it to blob storage for an
///     offloaded envelope.
///     Disposing the buffer is expected to clean up any backing resources (e.g. temp file).
/// </summary>
public interface ISnapshotPayloadBuffer : IAsyncDisposable, IDisposable
{
    /// <summary>
    ///     The readable/writable/seekable stream receiving the serialized snapshot payload.
    /// </summary>
    Stream Stream { get; }

    /// <summary>
    ///     A short, human-readable description of where the buffer is stored (e.g. "memory", "tempfile:/tmp/...").
    ///     Intended for diagnostics and tests; not used for routing decisions.
    /// </summary>
    string Location { get; }
}
