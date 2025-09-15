using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
/// Mock implementation of IBlobStorageSnapshotAccessor for testing
/// </summary>
public class MockBlobStorageSnapshotAccessor : IBlobStorageSnapshotAccessor
{
    private readonly Dictionary<string, byte[]> _storage = new();

    public string ProviderName => "MockStorage";

    public Task<string> WriteAsync(byte[] data, string projectorName, CancellationToken cancellationToken = default)
    {
        var key = $"{projectorName}-{Guid.NewGuid():N}";
        _storage[key] = data;
        return Task.FromResult(key);
    }

    public Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        _storage.TryGetValue(key, out var data);
        return Task.FromResult(data ?? Array.Empty<byte>());
    }
}