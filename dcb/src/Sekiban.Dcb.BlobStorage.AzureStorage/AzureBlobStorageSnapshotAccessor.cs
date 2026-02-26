using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.BlobStorage.AzureStorage;

/// <summary>
///     Azure Blob Storage implementation of IBlobStorageSnapshotAccessor.
///     Includes SDK-level retry configuration for resilience during transient failures.
/// </summary>
public sealed class AzureBlobStorageSnapshotAccessor : IBlobStorageSnapshotAccessor
{
    private readonly BlobContainerClient _container;
    private readonly string _prefix;
    private readonly ILogger<AzureBlobStorageSnapshotAccessor> _logger;

    public string ProviderName => "AzureBlobStorage";

    /// <summary>
    ///     Creates default BlobClientOptions with retry configuration for resilience.
    /// </summary>
    private static BlobClientOptions CreateDefaultOptions()
    {
        return new BlobClientOptions
        {
            Retry =
            {
                MaxRetries = 3,
                Delay = TimeSpan.FromMilliseconds(200),
                MaxDelay = TimeSpan.FromSeconds(5),
                Mode = RetryMode.Exponential,
                NetworkTimeout = TimeSpan.FromSeconds(30)
            }
        };
    }

    public AzureBlobStorageSnapshotAccessor(
        string connectionString,
        string containerName,
        string? prefix = null,
        ILogger<AzureBlobStorageSnapshotAccessor>? logger = null)
    {
        var options = CreateDefaultOptions();
        _container = new BlobContainerClient(connectionString, containerName, options);
        _prefix = prefix ?? string.Empty;
        _logger = logger ?? NullLogger<AzureBlobStorageSnapshotAccessor>.Instance;
    }

    public AzureBlobStorageSnapshotAccessor(
        BlobServiceClient blobServiceClient,
        string containerName,
        string? prefix = null,
        ILogger<AzureBlobStorageSnapshotAccessor>? logger = null)
    {
        _container = blobServiceClient.GetBlobContainerClient(containerName);
        _prefix = prefix ?? string.Empty;
        _logger = logger ?? NullLogger<AzureBlobStorageSnapshotAccessor>.Instance;
    }

    public async Task<string> WriteAsync(Stream data, string projectorName, CancellationToken cancellationToken = default)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        var key = BuildKey(projectorName, Guid.NewGuid().ToString("N"));
        var blob = _container.GetBlobClient(key);
        await blob.UploadAsync(data, overwrite: true, cancellationToken);
        _logger.LogDebug("Blob stream write succeeded: {Key}", key);
        return key;
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var blob = _container.GetBlobClient(key);
            return await blob.OpenReadAsync(cancellationToken: cancellationToken);
        }
        catch (RequestFailedException ex)
        {
            _logger.LogError(ex, "Blob OpenRead failed: {Key}, Status: {Status}", key, ex.Status);
            throw;
        }
    }

    private string BuildKey(string projectorName, string name)
    {
        var folder = string.IsNullOrEmpty(_prefix) ? projectorName : $"{_prefix.TrimEnd('/')}/{projectorName}";
        return $"{folder}/{name}.bin";
    }
}
