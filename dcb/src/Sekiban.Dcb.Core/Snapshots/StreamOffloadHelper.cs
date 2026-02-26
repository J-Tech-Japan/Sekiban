namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Shared helper that decides whether snapshot state data should be
///     inlined (stored as byte[] in the DB row) or offloaded to blob storage.
/// </summary>
public static class StreamOffloadHelper
{
    /// <summary>
    ///     Reads the stream, compares its length to the threshold, and either
    ///     returns inline data or uploads to blob and returns offload metadata.
    ///     Seekable streams (e.g. FileStream) above threshold are streamed directly
    ///     to blob without buffering to byte[].
    /// </summary>
    public static async Task<OffloadResult> ProcessAsync(
        Stream stream,
        string projectorName,
        int thresholdBytes,
        IBlobStorageSnapshotAccessor? blobAccessor,
        CancellationToken cancellationToken)
    {
        // Seekable stream optimization: use Length to decide without buffering
        if (stream.CanSeek && stream.Length > thresholdBytes && blobAccessor is not null)
        {
            stream.Position = 0;
            var key = await blobAccessor.WriteAsync(stream, projectorName, cancellationToken)
                .ConfigureAwait(false);
            return new OffloadResult(
                IsOffloaded: true,
                InlineData: null,
                OffloadKey: key,
                OffloadProvider: blobAccessor.ProviderName);
        }

        var buffer = await BufferStreamAsync(stream, cancellationToken).ConfigureAwait(false);
        return await ProcessAsync(buffer, projectorName, thresholdBytes, blobAccessor, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    ///     Compares byte[] length to the threshold, and either returns inline data
    ///     or uploads to blob and returns offload metadata.
    /// </summary>
    public static async Task<OffloadResult> ProcessAsync(
        byte[]? data,
        string projectorName,
        int thresholdBytes,
        IBlobStorageSnapshotAccessor? blobAccessor,
        CancellationToken cancellationToken)
    {
        if (data is not null && data.Length > thresholdBytes && blobAccessor is not null)
        {
            using var uploadStream = new MemoryStream(data, writable: false);
            var key = await blobAccessor.WriteAsync(uploadStream, projectorName, cancellationToken)
                .ConfigureAwait(false);
            return new OffloadResult(
                IsOffloaded: true,
                InlineData: null,
                OffloadKey: key,
                OffloadProvider: blobAccessor.ProviderName);
        }

        return new OffloadResult(
            IsOffloaded: false,
            InlineData: data,
            OffloadKey: null,
            OffloadProvider: null);
    }

    private static async Task<byte[]> BufferStreamAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        if (stream is MemoryStream ms && ms.TryGetBuffer(out var segment) && segment.Offset == 0 && segment.Count == (int)ms.Length)
        {
            return segment.Array!.Length == segment.Count
                ? segment.Array
                : segment.ToArray();
        }

        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        return buffer.ToArray();
    }
}
