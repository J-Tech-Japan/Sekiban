namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Optimized stream-to-byte[] reader used by snapshot persistence/load paths.
///     Avoids extra MemoryStream growth and extra array copies for common stream types.
/// </summary>
public static class StreamReadHelper
{
    public static async Task<byte[]> ReadAllBytesAsync(
        Stream stream,
        CancellationToken cancellationToken = default)
    {
        if (TryReadWholeMemoryStream(stream, out var memoryBytes))
        {
            return memoryBytes;
        }

        var seekableBytes = await TryReadSeekableStreamAsync(stream, cancellationToken).ConfigureAwait(false);
        if (seekableBytes is not null)
        {
            return seekableBytes;
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }

    private static bool TryReadWholeMemoryStream(Stream stream, out byte[] bytes)
    {
        bytes = [];
        if (stream is not MemoryStream memoryStream ||
            memoryStream.Position != 0 ||
            !memoryStream.TryGetBuffer(out var segment) ||
            segment.Offset != 0 ||
            segment.Count != memoryStream.Length)
        {
            return false;
        }

        bytes = segment.Array!.Length == segment.Count
            ? segment.Array
            : segment.ToArray();
        return true;
    }

    private static async Task<byte[]?> TryReadSeekableStreamAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        if (!stream.CanSeek)
        {
            return null;
        }

        var remaining = stream.Length - stream.Position;
        if (remaining <= 0)
        {
            return [];
        }

        if (remaining > int.MaxValue)
        {
            return null;
        }

        var target = new byte[(int)remaining];
        var offset = 0;
        while (offset < target.Length)
        {
            var read = await stream.ReadAsync(
                target.AsMemory(offset, target.Length - offset),
                cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }
            offset += read;
        }

        return offset == target.Length ? target : TrimToLength(target, offset);
    }

    private static byte[] TrimToLength(byte[] source, int length)
    {
        // Stream ended earlier than announced length.
        var trimmed = new byte[length];
        Buffer.BlockCopy(source, 0, trimmed, 0, length);
        return trimmed;
    }
}
