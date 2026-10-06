using System.Security.Cryptography;

namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     Shared helper that decides whether snapshot state data should be
///     inlined (stored as byte[] in the DB row) or offloaded to blob storage.
/// </summary>
public static class StreamOffloadHelper
{
    /// <summary>Maximum length of a deterministic snapshot key when a prefix is configured.</summary>
    public const int MaxDeterministicKeyLength = 512;

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

    public static string ComputeDeterministicKey(string projectorName, string contentHash)
    {
        var normalized = projectorName.Replace('\\', '/').Trim('/');
        return $"{normalized}/{contentHash}.bin";
    }

    /// <summary>
    ///     Applies the configured prefix to a deterministic key and rejects prefixed keys
    ///     that exceed the persistence limit before the caller uploads the snapshot.
    ///     Null or empty prefixes preserve the existing key without a length check.
    /// </summary>
    public static string ComputeDeterministicKey(string? prefix, string projectorName, string contentHash)
    {
        var key = ComputeDeterministicKey(projectorName, contentHash);
        if (string.IsNullOrEmpty(prefix))
        {
            return key;
        }

        key = $"{prefix.TrimEnd('/')}/{key}";
        if (key.Length > MaxDeterministicKeyLength)
        {
            throw new InvalidOperationException(
                $"Snapshot prefix '{prefix}' produces a deterministic key of length {key.Length}, " +
                $"exceeding the limit of {MaxDeterministicKeyLength} characters.");
        }

        return key;
    }

    public static async Task<string> ComputeContentHashAsync(Stream stream, CancellationToken cancellationToken)
    {
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);

        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        return Convert.ToHexStringLower(hash);
    }
}
