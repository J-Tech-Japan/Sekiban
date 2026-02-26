using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.Orleans.Tests;

/// <summary>
/// Mock implementation of IBlobStorageSnapshotAccessor for testing
/// </summary>
public class MockBlobStorageSnapshotAccessor : IBlobStorageSnapshotAccessor
{
    private readonly Dictionary<string, byte[]> _storage = new();

    public string ProviderName => "MockStorage";

    public async Task<string> WriteAsync(Stream data, string projectorName, CancellationToken cancellationToken = default)
    {
        using var ms = new MemoryStream();
        await data.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
        var key = $"{projectorName}-{Guid.NewGuid():N}";
        _storage[key] = ms.ToArray();
        return key;
    }

    public Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        if (_storage.TryGetValue(key, out var data))
        {
            return Task.FromResult<Stream>(new MemoryStream(data, writable: false));
        }
        throw new FileNotFoundException($"Snapshot not found for key: {key}");
    }
}
