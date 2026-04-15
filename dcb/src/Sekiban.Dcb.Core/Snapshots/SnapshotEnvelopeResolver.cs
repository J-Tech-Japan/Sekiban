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
        var payloadBytes = await ReadPayloadBytesAsync(stream, offloaded.PayloadLength, cancellationToken)
            .ConfigureAwait(false);

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

    private static async Task<byte[]> ReadPayloadBytesAsync(
        Stream stream,
        long payloadLength,
        CancellationToken cancellationToken)
    {
        if (payloadLength <= 0 || payloadLength > int.MaxValue)
        {
            return await StreamReadHelper.ReadAllBytesAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        var buffer = new byte[(int)payloadLength];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            offset += read;
        }

        if (offset == buffer.Length)
        {
            return buffer;
        }

        if (offset == 0)
        {
            return [];
        }

        var trimmed = new byte[offset];
        Buffer.BlockCopy(buffer, 0, trimmed, 0, offset);
        return trimmed;
    }
}
