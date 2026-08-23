using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Runtime-only snapshot restore input. Offloaded payloads retain their opened stream through the resolver-to-actor
///     seam instead of being re-encoded into an inline envelope.
/// </summary>
/// <remarks>
///     This object owns an offloaded payload stream when present. The code that obtains it from
///     <see cref="SnapshotEnvelopeResolver.ResolveForRestoreAsync" /> is responsible for disposing this input after the
///     actor has completed. The actor and projector registry deliberately do not dispose the stream.
/// </remarks>
public sealed class ResolvedSnapshotRestore : IAsyncDisposable, IDisposable
{
    internal ResolvedSnapshotRestore(
        SerializableMultiProjectionState state,
        Stream? payloadStream,
        bool isOffloaded)
    {
        State = state;
        PayloadStream = payloadStream;
        IsOffloaded = isOffloaded;
    }

    /// <summary>Snapshot tracking metadata and, for inline snapshots, the inline payload.</summary>
    public SerializableMultiProjectionState State { get; }

    /// <summary>
    ///     Opened offloaded payload stream, or <see langword="null" /> for an inline envelope. It may be non-seekable
    ///     and is positioned at the payload's current read position.
    /// </summary>
    public Stream? PayloadStream { get; }

    /// <summary>Whether this restore came from an offloaded payload.</summary>
    public bool IsOffloaded { get; }

    public void Dispose() => PayloadStream?.Dispose();

    public ValueTask DisposeAsync() => PayloadStream?.DisposeAsync() ?? ValueTask.CompletedTask;
}
