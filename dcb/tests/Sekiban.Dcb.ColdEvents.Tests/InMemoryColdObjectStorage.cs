using System.Collections.Concurrent;
using System.Security.Cryptography;
using ResultBoxes;
using Sekiban.Dcb.ColdEvents;
namespace Sekiban.Dcb.ColdEvents.Tests;

public sealed class InMemoryColdObjectStorage : IColdObjectStorage
{
    private readonly ConcurrentDictionary<string, StoredBlob> _blobs = new();

    private sealed record StoredBlob(byte[] Data, string ETag);

    public Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct)
    {
        if (!_blobs.TryGetValue(path, out var blob))
        {
            return Task.FromResult(
                ResultBox.Error<ColdStorageObject>(new KeyNotFoundException($"Object not found: {path}")));
        }
        return Task.FromResult(
            ResultBox.FromValue(new ColdStorageObject(blob.Data, blob.ETag)));
    }

    public Task<ResultBox<bool>> PutAsync(
        string path,
        byte[] data,
        string? expectedETag,
        CancellationToken ct)
    {
        if (expectedETag is not null)
        {
            if (!_blobs.TryGetValue(path, out var existing))
            {
                return Task.FromResult(
                    ResultBox.Error<bool>(new InvalidOperationException(
                        $"Conditional write failed: object does not exist at {path}")));
            }
            if (existing.ETag != expectedETag)
            {
                return Task.FromResult(
                    ResultBox.Error<bool>(new InvalidOperationException(
                        $"ETag mismatch at {path}: expected={expectedETag}, actual={existing.ETag}")));
            }
        }

        var newETag = ComputeETag(data);
        _blobs[path] = new StoredBlob(data, newETag);
        return Task.FromResult(ResultBox.FromValue(true));
    }

    public Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct)
    {
        var keys = _blobs.Keys
            .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
            .Order()
            .ToList();
        return Task.FromResult(
            ResultBox.FromValue<IReadOnlyList<string>>(keys));
    }

    public Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct)
    {
        var removed = _blobs.TryRemove(path, out _);
        return Task.FromResult(ResultBox.FromValue(removed));
    }

    private static string ComputeETag(byte[] data)
    {
        var hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }
}
