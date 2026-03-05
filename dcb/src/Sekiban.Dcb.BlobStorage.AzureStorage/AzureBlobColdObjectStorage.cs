using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResultBoxes;
using Sekiban.Dcb.ColdEvents;

namespace Sekiban.Dcb.BlobStorage.AzureStorage;

public sealed class AzureBlobColdObjectStorage : IColdObjectStorage
{
    private readonly BlobContainerClient _container;
    private readonly string _prefix;
    private readonly ILogger<AzureBlobColdObjectStorage> _logger;

    public AzureBlobColdObjectStorage(
        string connectionString,
        string containerName,
        string? prefix = null,
        ILogger<AzureBlobColdObjectStorage>? logger = null)
    {
        _container = new BlobContainerClient(connectionString, containerName);
        _prefix = prefix ?? string.Empty;
        _logger = logger ?? NullLogger<AzureBlobColdObjectStorage>.Instance;
    }

    public AzureBlobColdObjectStorage(
        BlobServiceClient blobServiceClient,
        string containerName,
        string? prefix = null,
        ILogger<AzureBlobColdObjectStorage>? logger = null)
    {
        _container = blobServiceClient.GetBlobContainerClient(containerName);
        _prefix = prefix ?? string.Empty;
        _logger = logger ?? NullLogger<AzureBlobColdObjectStorage>.Instance;
    }

    public async Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct)
    {
        try
        {
            var blob = _container.GetBlobClient(BuildBlobName(path));
            var response = await blob.DownloadContentAsync(ct).ConfigureAwait(false);
            var content = response.Value;
            var data = content.Content.ToArray();
            var etag = content.Details.ETag.ToString();
            return ResultBox.FromValue(new ColdStorageObject(data, etag));
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return ResultBox.Error<ColdStorageObject>(new KeyNotFoundException($"Cold object not found: {path}", ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure GetAsync failed: {Path}", path);
            return ResultBox.Error<ColdStorageObject>(ex);
        }
    }

    public async Task<ResultBox<bool>> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct)
    {
        try
        {
            await _container.CreateIfNotExistsAsync(cancellationToken: ct).ConfigureAwait(false);

            var blob = _container.GetBlobClient(BuildBlobName(path));
            var options = new BlobUploadOptions();
            if (!string.IsNullOrWhiteSpace(expectedETag))
            {
                options.Conditions = new BlobRequestConditions
                {
                    IfMatch = new ETag(expectedETag)
                };
            }

            await blob.UploadAsync(BinaryData.FromBytes(data), options, ct).ConfigureAwait(false);
            return ResultBox.FromValue(true);
        }
        catch (RequestFailedException ex) when (ex.Status == 404 && !string.IsNullOrWhiteSpace(expectedETag))
        {
            return ResultBox.Error<bool>(new InvalidOperationException($"Conditional write failed: {path} does not exist", ex));
        }
        catch (RequestFailedException ex) when (ex.Status == 412)
        {
            return ResultBox.Error<bool>(new InvalidOperationException($"ETag mismatch at {path}: expected={expectedETag}", ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure PutAsync failed: {Path}", path);
            return ResultBox.Error<bool>(ex);
        }
    }

    public async Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct)
    {
        try
        {
            var blobPrefix = BuildListPrefix(prefix);
            var paths = new List<string>();
            await foreach (var item in _container.GetBlobsAsync(
                                   traits: BlobTraits.None,
                                   states: BlobStates.None,
                                   prefix: blobPrefix,
                                   cancellationToken: ct)
                               .ConfigureAwait(false))
            {
                paths.Add(ToRelativePath(item.Name));
            }

            return ResultBox.FromValue<IReadOnlyList<string>>(paths);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure ListAsync failed: {Prefix}", prefix);
            return ResultBox.Error<IReadOnlyList<string>>(ex);
        }
    }

    public async Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct)
    {
        try
        {
            var blob = _container.GetBlobClient(BuildBlobName(path));
            var response = await blob.DeleteIfExistsAsync(cancellationToken: ct).ConfigureAwait(false);
            return ResultBox.FromValue(response.Value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Azure DeleteAsync failed: {Path}", path);
            return ResultBox.Error<bool>(ex);
        }
    }

    private string BuildBlobName(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrEmpty(_prefix))
        {
            return normalizedPath;
        }

        if (string.IsNullOrEmpty(normalizedPath))
        {
            return _prefix.TrimEnd('/');
        }

        return $"{_prefix.TrimEnd('/')}/{normalizedPath}";
    }

    private string BuildListPrefix(string path)
    {
        var normalizedPath = NormalizePath(path);
        if (string.IsNullOrEmpty(_prefix))
        {
            return normalizedPath;
        }

        if (string.IsNullOrEmpty(normalizedPath))
        {
            return _prefix.TrimEnd('/') + "/";
        }

        return $"{_prefix.TrimEnd('/')}/{normalizedPath}";
    }

    private string ToRelativePath(string name)
    {
        if (string.IsNullOrEmpty(_prefix))
        {
            return NormalizePath(name);
        }

        var prefixWithSlash = _prefix.TrimEnd('/') + "/";
        if (name.StartsWith(prefixWithSlash, StringComparison.Ordinal))
        {
            return NormalizePath(name[prefixWithSlash.Length..]);
        }

        return NormalizePath(name);
    }

    private static string NormalizePath(string path)
        => path.Replace('\\', '/').TrimStart('/');
}
