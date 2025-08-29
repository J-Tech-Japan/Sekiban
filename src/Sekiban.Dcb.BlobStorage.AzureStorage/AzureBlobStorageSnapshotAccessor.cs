using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.BlobStorage.AzureStorage;

/// <summary>
///     Azure Blob Storage implementation of IBlobStorageSnapshotAccessor.
/// </summary>
public sealed class AzureBlobStorageSnapshotAccessor : IBlobStorageSnapshotAccessor
{
    private readonly BlobContainerClient _container;
    private readonly string _prefix;

    public string ProviderName => "AzureBlobStorage";

    public AzureBlobStorageSnapshotAccessor(string connectionString, string containerName, string? prefix = null)
    {
        _container = new BlobContainerClient(connectionString, containerName);
        _prefix = prefix ?? string.Empty;
    }

    public async Task<string> WriteAsync(byte[] data, string projectorName, CancellationToken cancellationToken = default)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        var key = BuildKey(projectorName, Guid.NewGuid().ToString("N"));
        var blob = _container.GetBlobClient(key);
        using var ms = new MemoryStream(data, writable: false);
        await blob.UploadAsync(ms, overwrite: true, cancellationToken);
        return key;
    }

    public async Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var blob = _container.GetBlobClient(key);
        var resp = await blob.DownloadContentAsync(cancellationToken);
        return resp.Value.Content.ToArray();
    }

    private string BuildKey(string projectorName, string name)
    {
        var folder = string.IsNullOrEmpty(_prefix) ? projectorName : $"{_prefix.TrimEnd('/')}/{projectorName}";
        return $"{folder}/{name}.bin";
    }
}
