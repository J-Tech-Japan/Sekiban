using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sekiban.Dcb.Snapshots;
using System.Security.Cryptography;
using System.Text;

namespace Sekiban.Dcb.BlobStorage.AzureStorage;

/// <summary>
///     Azure Blob Storage implementation of IBlobStorageSnapshotAccessor.
///     Includes SDK-level retry configuration for resilience during transient failures.
/// </summary>
public sealed class AzureBlobStorageSnapshotAccessor : IBlobStorageSnapshotAccessor
{
    private const string DefaultLocalCacheFolderName = "sekiban-snapshot-cache";
    private readonly BlobContainerClient _container;
    private readonly string _prefix;
    private readonly ILogger<AzureBlobStorageSnapshotAccessor> _logger;
    private readonly string _localCacheDirectory;

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
        string? localCacheDirectory = null,
        ILogger<AzureBlobStorageSnapshotAccessor>? logger = null)
    {
        var options = CreateDefaultOptions();
        _container = new BlobContainerClient(connectionString, containerName, options);
        _prefix = prefix ?? string.Empty;
        _localCacheDirectory = localCacheDirectory ?? Path.Combine(Path.GetTempPath(), DefaultLocalCacheFolderName);
        _logger = logger ?? NullLogger<AzureBlobStorageSnapshotAccessor>.Instance;
    }

    public AzureBlobStorageSnapshotAccessor(
        BlobServiceClient blobServiceClient,
        string containerName,
        string? prefix = null,
        string? localCacheDirectory = null,
        ILogger<AzureBlobStorageSnapshotAccessor>? logger = null)
    {
        _container = blobServiceClient.GetBlobContainerClient(containerName);
        _prefix = prefix ?? string.Empty;
        _localCacheDirectory = localCacheDirectory ?? Path.Combine(Path.GetTempPath(), DefaultLocalCacheFolderName);
        _logger = logger ?? NullLogger<AzureBlobStorageSnapshotAccessor>.Instance;
    }

    public async Task<string> WriteAsync(Stream data, string projectorName, CancellationToken cancellationToken = default)
    {
        await _container.CreateIfNotExistsAsync(cancellationToken: cancellationToken);
        var key = data.CanSeek
            ? StreamOffloadHelper.ComputeDeterministicKey(
                projectorName,
                await StreamOffloadHelper.ComputeContentHashAsync(data, cancellationToken).ConfigureAwait(false))
            : BuildKey(projectorName, Guid.NewGuid().ToString("N"));
        var blob = _container.GetBlobClient(key);
        if (data.CanSeek)
        {
            data.Position = 0;
        }
        await blob.UploadAsync(data, overwrite: true, cancellationToken);
        _logger.LogDebug("Blob stream write succeeded: {Key}", key);
        return key;
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var cachePath = BuildCachePath(key);
            if (File.Exists(cachePath))
            {
                return OpenCachedFile(cachePath);
            }

            var blob = _container.GetBlobClient(key);
            Directory.CreateDirectory(_localCacheDirectory);
            var tempPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                await blob.DownloadToAsync(tempPath, cancellationToken: cancellationToken);
                PromoteTempCacheFile(tempPath, cachePath);
                return OpenCachedFile(cachePath);
            }
            finally
            {
                SafeDelete(tempPath);
            }
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

    private string BuildCachePath(string key)
    {
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
        return Path.Combine(_localCacheDirectory, hash + ".bin");
    }

    private static FileStream OpenCachedFile(string cachePath)
        => new(
            cachePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

    private static void PromoteTempCacheFile(string tempPath, string cachePath)
    {
        if (File.Exists(cachePath))
        {
            SafeDelete(tempPath);
            return;
        }

        try
        {
            File.Move(tempPath, cachePath);
        }
        catch (IOException) when (File.Exists(cachePath))
        {
            SafeDelete(tempPath);
        }
    }

    private static void SafeDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
            // Best effort cleanup.
        }
    }
}
