using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Resolves a snapshot envelope into a form that can be applied by the actor.
///     The restore path retains an opened offloaded payload stream so a stream-capable projector registry can deserialize
///     it without first materializing a whole payload array.
/// </summary>
public static class SnapshotEnvelopeResolver
{
    /// <summary>
    ///     Resolves an envelope for restore. For an offloaded envelope the returned input owns an open payload stream;
    ///     callers must dispose it after the actor restore completes. This is the production restore seam.
    /// </summary>
    public static async Task<ResolvedSnapshotRestore> ResolveForRestoreAsync(
        SerializableMultiProjectionStateEnvelope envelope,
        IBlobStorageSnapshotAccessor? blobAccessor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!envelope.IsOffloaded)
        {
            if (envelope.InlineState is null)
            {
                throw new InvalidOperationException("Inline snapshot missing InlineState.");
            }

            return new ResolvedSnapshotRestore(envelope.InlineState, payloadStream: null, isOffloaded: false);
        }

        if (envelope.OffloadedState is null)
        {
            throw new InvalidOperationException("Offloaded snapshot missing OffloadedState.");
        }

        if (blobAccessor is null)
        {
            throw new InvalidOperationException(
                "Offloaded snapshot payload cannot be restored without an IBlobStorageSnapshotAccessor.");
        }

        var offloaded = envelope.OffloadedState;
        var stream = await blobAccessor.OpenReadAsync(offloaded.OffloadKey, cancellationToken).ConfigureAwait(false);
        try
        {
            // The payload intentionally stays absent from this runtime metadata state. SetResolvedSnapshotAsync chooses
            // the optional streaming capability or the explicit compatibility fallback, never an implicit resolver buffer.
            var state = new SerializableMultiProjectionState(
                payloadJson: null,
                payloadBase64: null,
                offloaded.MultiProjectionPayloadType,
                offloaded.ProjectorName,
                offloaded.ProjectorVersion,
                offloaded.LastSortableUniqueId,
                offloaded.LastEventId,
                offloaded.Version,
                offloaded.IsCatchedUp,
                offloaded.IsSafeState,
                offloaded.OriginalSizeBytes,
                offloaded.CompressedSizeBytes);

            return new ResolvedSnapshotRestore(state, stream, isOffloaded: true);
        }
        catch
        {
            await stream.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    ///     Compatibility API for callers that explicitly require an inline envelope. New restore call chains must use
    ///     <see cref="ResolveForRestoreAsync" /> so a stream-capable registry never receives a materialized payload.
    /// </summary>
    public static async Task<SerializableMultiProjectionStateEnvelope> ResolveInlineAsync(
        SerializableMultiProjectionStateEnvelope envelope,
        IBlobStorageSnapshotAccessor? blobAccessor,
        CancellationToken cancellationToken = default)
    {
        if (!envelope.IsOffloaded)
        {
            return envelope;
        }

        await using var resolved = await ResolveForRestoreAsync(envelope, blobAccessor, cancellationToken)
            .ConfigureAwait(false);
        var payloadBytes = await StreamReadHelper.ReadAllBytesAsync(
                resolved.PayloadStream ?? throw new InvalidOperationException("Offloaded restore stream was not opened."),
                cancellationToken)
            .ConfigureAwait(false);
        var offloaded = envelope.OffloadedState!;

        var inlineState = SerializableMultiProjectionState.FromRuntimeBytes(
            payloadBytes,
            offloaded.MultiProjectionPayloadType,
            offloaded.ProjectorName,
            offloaded.ProjectorVersion,
            offloaded.LastSortableUniqueId,
            offloaded.LastEventId,
            offloaded.Version,
            offloaded.IsCatchedUp,
            offloaded.IsSafeState,
            offloaded.OriginalSizeBytes,
            offloaded.CompressedSizeBytes);

        return new SerializableMultiProjectionStateEnvelope(
            IsOffloaded: false,
            InlineState: inlineState,
            OffloadedState: null);
    }
}
