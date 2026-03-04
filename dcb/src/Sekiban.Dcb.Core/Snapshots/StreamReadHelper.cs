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
        if (stream is MemoryStream memoryStream &&
            memoryStream.Position == 0 &&
            memoryStream.TryGetBuffer(out var segment) &&
            segment.Offset == 0 &&
            segment.Count == memoryStream.Length)
        {
            return segment.Array!.Length == segment.Count
                ? segment.Array
                : segment.ToArray();
        }

        if (stream.CanSeek)
        {
            var remaining = stream.Length - stream.Position;
            if (remaining <= 0)
            {
                return [];
            }

            if (remaining <= int.MaxValue)
            {
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

                if (offset == target.Length)
                {
                    return target;
                }

                // Stream ended earlier than announced.
                var trimmed = new byte[offset];
                Buffer.BlockCopy(target, 0, trimmed, 0, offset);
                return trimmed;
            }
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
