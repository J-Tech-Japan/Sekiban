using Sekiban.Dcb.MultiProjections;

namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Resolves a snapshot envelope into a form that can be applied by the actor.
///     Inline snapshots are returned as-is; offloaded payload snapshots are materialized
///     back into an inline envelope using the provided blob accessor.
/// </summary>
public static class SnapshotEnvelopeResolver
{
    public static async Task<SerializableMultiProjectionStateEnvelope> ResolveInlineAsync(
        SerializableMultiProjectionStateEnvelope envelope,
        IBlobStorageSnapshotAccessor? blobAccessor,
        CancellationToken cancellationToken = default)
    {
        if (!envelope.IsOffloaded || envelope.OffloadedState is null)
        {
            return envelope;
        }

        if (blobAccessor is null)
        {
            throw new InvalidOperationException(
                "Offloaded snapshot payload cannot be restored without an IBlobStorageSnapshotAccessor.");
        }

        var offloaded = envelope.OffloadedState;
        await using var stream = await blobAccessor.OpenReadAsync(offloaded.OffloadKey, cancellationToken)
            .ConfigureAwait(false);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);

        var inlineState = SerializableMultiProjectionState.FromBytes(
            buffer.ToArray(),
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
