using System.Collections.Concurrent;
using System.Security.Cryptography;
using Sekiban.Dcb.ColdEvents;
namespace Sekiban.Dcb.ColdEvents.Tests;

public sealed class InMemoryColdObjectStorage : IColdObjectStorage
{
    private readonly ConcurrentDictionary<string, StoredBlob> _blobs = new();

    private sealed record StoredBlob(byte[] Data, string ETag);

    public Task<ColdStorageObject?> GetAsync(string path, CancellationToken ct)
    {
        return Task.FromResult(
            _blobs.TryGetValue(path, out var blob) ? new ColdStorageObject(blob.Data, blob.ETag) : null);
    }

    public Task<Stream> OpenReadAsync(string path, CancellationToken ct)
    {
        if (!_blobs.TryGetValue(path, out var blob))
        {
            return Task.FromException<Stream>(new KeyNotFoundException($"Object not found: {path}"));
        }

        Stream stream = new MemoryStream(blob.Data, writable: false);
        return Task.FromResult(stream);
    }

    public Task<bool> PutAsync(
        string path,
        byte[] data,
        string? expectedETag,
        CancellationToken ct)
    {
        if (expectedETag is not null)
        {
            if (!_blobs.TryGetValue(path, out var existing))
            {
                return Task.FromException<bool>(new InvalidOperationException(
                    $"Conditional write failed: object does not exist at {path}"));
            }
            if (existing.ETag != expectedETag)
            {
                return Task.FromException<bool>(new InvalidOperationException(
                    $"ETag mismatch at {path}: expected={expectedETag}, actual={existing.ETag}"));
            }
        }

        var newETag = ComputeETag(data);
        _blobs[path] = new StoredBlob(data, newETag);
        return Task.FromResult(true);
    }

    public async Task<bool> PutAsync(
        string path,
        Stream data,
        string? expectedETag,
        CancellationToken ct)
    {
        using var ms = new MemoryStream();
        if (data.CanSeek)
        {
            data.Position = 0;
        }

        await data.CopyToAsync(ms, ct);
        return await PutAsync(path, ms.ToArray(), expectedETag, ct);
    }

    public Task<IReadOnlyList<string>> ListAsync(string prefix, CancellationToken ct)
    {
        var keys = _blobs.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Order()
            .ToList();
        return Task.FromResult<IReadOnlyList<string>>(keys);
    }

    public Task<bool> DeleteAsync(string path, CancellationToken ct)
    {
        var removed = _blobs.TryRemove(path, out _);
        return Task.FromResult(removed);
    }

    private static string ComputeETag(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }
}
