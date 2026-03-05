using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResultBoxes;
using Sekiban.Dcb.ColdEvents;

namespace Sekiban.Dcb.BlobStorage.S3;

public sealed class S3ColdObjectStorage : IColdObjectStorage
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _prefix;
    private readonly bool _enableEncryption;
    private readonly ILogger<S3ColdObjectStorage> _logger;

    public S3ColdObjectStorage(
        IAmazonS3 s3Client,
        string bucketName,
        string? prefix = null,
        bool enableEncryption = true,
        ILogger<S3ColdObjectStorage>? logger = null)
    {
        _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
        _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
        _prefix = prefix ?? string.Empty;
        _enableEncryption = enableEncryption;
        _logger = logger ?? NullLogger<S3ColdObjectStorage>.Instance;
    }

    public async Task<ResultBox<ColdStorageObject>> GetAsync(string path, CancellationToken ct)
    {
        try
        {
            var key = BuildKey(path);
            var response = await _s3Client.GetObjectAsync(new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            }, ct).ConfigureAwait(false);

            await using var stream = response.ResponseStream;
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms, ct).ConfigureAwait(false);
            var data = ms.ToArray();
            var etag = response.ETag ?? string.Empty;
            return ResultBox.FromValue(new ColdStorageObject(data, etag));
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return ResultBox.Error<ColdStorageObject>(new KeyNotFoundException($"Cold object not found: {path}", ex));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "S3 GetAsync failed: {Bucket}/{Path}", _bucketName, path);
            return ResultBox.Error<ColdStorageObject>(ex);
        }
    }

    public async Task<ResultBox<bool>> PutAsync(string path, byte[] data, string? expectedETag, CancellationToken ct)
    {
        try
        {
            var key = BuildKey(path);
            if (expectedETag is not null)
            {
                // S3 PutObject does not provide atomic destination If-Match semantics.
                // Returning NotSupported avoids false safety from non-atomic check-then-put.
                return ResultBox.Error<bool>(new NotSupportedException(
                    "S3ColdObjectStorage does not support atomic expectedETag writes. " +
                    "Use AzureBlobColdObjectStorage for optimistic concurrency paths."));
            }

            using var ms = new MemoryStream(data);
            var request = new PutObjectRequest
            {
                BucketName = _bucketName,
                Key = key,
                InputStream = ms,
                ContentType = "application/octet-stream"
            };

            if (_enableEncryption)
            {
                request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
            }

            await _s3Client.PutObjectAsync(request, ct).ConfigureAwait(false);
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "S3 PutAsync failed: {Bucket}/{Path}", _bucketName, path);
            return ResultBox.Error<bool>(ex);
        }
    }

    public async Task<ResultBox<IReadOnlyList<string>>> ListAsync(string prefix, CancellationToken ct)
    {
        try
        {
            var normalizedPrefix = NormalizePath(prefix);
            var storagePrefix = BuildListPrefix(normalizedPrefix);
            var result = new List<string>();
            string? continuationToken = null;

            do
            {
                var response = await _s3Client.ListObjectsV2Async(new ListObjectsV2Request
                {
                    BucketName = _bucketName,
                    Prefix = storagePrefix,
                    ContinuationToken = continuationToken
                }, ct).ConfigureAwait(false);

                foreach (var obj in response.S3Objects)
                {
                    result.Add(ToRelativePath(obj.Key));
                }

                continuationToken = response.IsTruncated == true ? response.NextContinuationToken : null;
            } while (continuationToken is not null);

            return ResultBox.FromValue<IReadOnlyList<string>>(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "S3 ListAsync failed: {Bucket}/{Prefix}", _bucketName, prefix);
            return ResultBox.Error<IReadOnlyList<string>>(ex);
        }
    }

    public async Task<ResultBox<bool>> DeleteAsync(string path, CancellationToken ct)
    {
        try
        {
            var key = BuildKey(path);
            await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            }, ct).ConfigureAwait(false);
            return ResultBox.FromValue(true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "S3 DeleteAsync failed: {Bucket}/{Path}", _bucketName, path);
            return ResultBox.Error<bool>(ex);
        }
    }

    private string BuildKey(string path)
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

    private string BuildListPrefix(string normalizedPrefix)
    {
        if (string.IsNullOrEmpty(_prefix))
        {
            return normalizedPrefix;
        }

        if (string.IsNullOrEmpty(normalizedPrefix))
        {
            return _prefix.TrimEnd('/') + "/";
        }

        return $"{_prefix.TrimEnd('/')}/{normalizedPrefix}";
    }

    private string ToRelativePath(string key)
    {
        if (string.IsNullOrEmpty(_prefix))
        {
            return NormalizePath(key);
        }

        var prefixWithSlash = _prefix.TrimEnd('/') + "/";
        if (key.StartsWith(prefixWithSlash, StringComparison.Ordinal))
        {
            return NormalizePath(key[prefixWithSlash.Length..]);
        }

        return NormalizePath(key);
    }

    private static string NormalizePath(string path)
        => path.Replace('\\', '/').TrimStart('/');

}
