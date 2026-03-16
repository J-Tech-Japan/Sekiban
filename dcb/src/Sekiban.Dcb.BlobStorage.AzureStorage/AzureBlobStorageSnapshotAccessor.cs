using Azure;
using Azure.Core;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
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
    private const string DefaultLocalCacheFolderName = "sekiban-snapshot-cache";
    private readonly BlobContainerClient _container;
    private readonly SemaphoreSlim _containerEnsureLock = new(1, 1);
    private readonly string _prefix;
    private readonly ILogger<AzureBlobStorageSnapshotAccessor> _logger;
    private readonly string _localCacheDirectory;
    private readonly string _cacheNamespace;
    private volatile bool _containerEnsured;

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
        _cacheNamespace = BuildCacheNamespace(_container.Uri, _prefix);
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
        _cacheNamespace = BuildCacheNamespace(_container.Uri, _prefix);
        _logger = logger ?? NullLogger<AzureBlobStorageSnapshotAccessor>.Instance;
    }

    public async Task<string> WriteAsync(Stream data, string projectorName, CancellationToken cancellationToken = default)
    {
        await EnsureContainerExistsAsync(cancellationToken).ConfigureAwait(false);
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
        await UploadWithContainerRecoveryAsync(blob, data, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("Blob stream write succeeded: {Key}", key);
        return key;
    }

    private async Task EnsureContainerExistsAsync(CancellationToken cancellationToken)
    {
        if (_containerEnsured)
        {
            return;
        }

        await _containerEnsureLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_containerEnsured)
            {
                return;
            }

            var exists = await _container.ExistsAsync(cancellationToken).ConfigureAwait(false);
            if (!exists.Value)
            {
                try
                {
                    await _container.CreateAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                }
                catch (RequestFailedException ex) when (IsContainerAlreadyExistsConflict(ex))
                {
                    _logger.LogDebug(ex, "Blob container already created by another writer: {ContainerUri}", _container.Uri);
                }
            }

            _containerEnsured = true;
        }
        finally
        {
            _containerEnsureLock.Release();
        }
    }

    private async Task UploadWithContainerRecoveryAsync(
        BlobClient blob,
        Stream data,
        CancellationToken cancellationToken)
    {
        try
        {
            await blob.UploadAsync(data, overwrite: true, cancellationToken).ConfigureAwait(false);
        }
        catch (RequestFailedException ex) when (IsMissingContainerError(ex))
        {
            ResetContainerEnsureState();

            if (!data.CanSeek)
            {
                _logger.LogWarning(
                    ex,
                    "Blob container disappeared during upload, but the stream cannot be retried: {ContainerUri}",
                    _container.Uri);
                throw;
            }

            _logger.LogWarning(
                ex,
                "Blob container disappeared during upload. Recreating container and retrying once: {ContainerUri}",
                _container.Uri);

            data.Position = 0;
            await EnsureContainerExistsAsync(cancellationToken).ConfigureAwait(false);
            await blob.UploadAsync(data, overwrite: true, cancellationToken).ConfigureAwait(false);
        }
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
            Directory.CreateDirectory(Path.GetDirectoryName(cachePath)!);
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
        => SnapshotLocalCachePath.Build(_localCacheDirectory, ProviderName, _cacheNamespace, key);

    private static string BuildCacheNamespace(Uri containerUri, string prefix)
    {
        var normalizedPrefix = prefix.Replace('\\', '/').Trim('/');
        var containerPath = containerUri.GetLeftPart(UriPartial.Path);
        return string.IsNullOrEmpty(normalizedPrefix)
            ? containerPath
            : $"{containerPath}|{normalizedPrefix}";
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

    private void ResetContainerEnsureState()
    {
        _containerEnsured = false;
    }

    private static bool IsContainerAlreadyExistsConflict(RequestFailedException ex)
    {
        return ex.Status == 409 &&
            string.Equals(ex.ErrorCode, BlobErrorCode.ContainerAlreadyExists.ToString(), StringComparison.Ordinal);
    }

    private static bool IsMissingContainerError(RequestFailedException ex)
    {
        return ex.Status == 404 &&
            (string.IsNullOrWhiteSpace(ex.ErrorCode) ||
                string.Equals(ex.ErrorCode, BlobErrorCode.ContainerNotFound.ToString(), StringComparison.Ordinal));
    }
}
