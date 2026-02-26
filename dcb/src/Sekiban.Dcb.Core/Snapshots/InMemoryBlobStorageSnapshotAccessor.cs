using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace Sekiban.Dcb.Snapshots;

/// <summary>
///     In-memory implementation for concept verification and unit tests.
/// </summary>
public sealed class InMemoryBlobStorageSnapshotAccessor : IBlobStorageSnapshotAccessor
{
    private readonly ConcurrentDictionary<string, byte[]> _store = new();

    public string ProviderName => "InMemoryBlobStorage";

    public Task<string> WriteAsync(byte[] data, string projectorName, CancellationToken cancellationToken = default)
    {
        // Generate a deterministic-ish key based on projector and content hash
        var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var key = $"{Sanitize(projectorName)}/{hash}";
        _store[key] = data;
        return Task.FromResult(key);
    }

    public Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(key, out var data))
        {
            return Task.FromResult(data);
        }
        throw new FileNotFoundException($"Snapshot not found for key: {key}");
    }

    public async Task<string> WriteAsync(Stream data, string projectorName, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await data.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        return await WriteAsync(ms.ToArray(), projectorName, cancellationToken).ConfigureAwait(false);
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(key, out var data))
        {
            return Task.FromResult<Stream>(new MemoryStream(data, writable: false));
        }
        throw new FileNotFoundException($"Snapshot not found for key: {key}");
    }

    private static string Sanitize(string name)
    {
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            sb.Append(char.IsLetterOrDigit(ch) || ch is '-' or '_' ? ch : '_');
        }
        return sb.ToString();
    }
}

