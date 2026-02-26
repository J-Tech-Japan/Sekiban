using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.BlobStorage.S3;

/// <summary>
///     AWS S3 implementation of IBlobStorageSnapshotAccessor.
/// </summary>
public sealed class S3BlobStorageSnapshotAccessor : IBlobStorageSnapshotAccessor
{
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _prefix;
    private readonly bool _enableEncryption;
    private readonly ILogger<S3BlobStorageSnapshotAccessor> _logger;

    public string ProviderName => "AwsS3";

    public S3BlobStorageSnapshotAccessor(
        IAmazonS3 s3Client,
        string bucketName,
        string? prefix = null,
        bool enableEncryption = true,
        ILogger<S3BlobStorageSnapshotAccessor>? logger = null)
    {
        _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
        _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
        _prefix = prefix ?? string.Empty;
        _enableEncryption = enableEncryption;
        _logger = logger ?? NullLogger<S3BlobStorageSnapshotAccessor>.Instance;
    }

    public S3BlobStorageSnapshotAccessor(
        string bucketName,
        string? prefix = null,
        bool enableEncryption = true,
        ILogger<S3BlobStorageSnapshotAccessor>? logger = null)
    {
        _s3Client = new AmazonS3Client();
        _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
        _prefix = prefix ?? string.Empty;
        _enableEncryption = enableEncryption;
        _logger = logger ?? NullLogger<S3BlobStorageSnapshotAccessor>.Instance;
    }

    public async Task<string> WriteAsync(byte[] data, string projectorName, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(projectorName, Guid.NewGuid().ToString("N"));

        using var ms = new MemoryStream(data, writable: false);
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

        await _s3Client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("S3 write succeeded: {Bucket}/{Key}, Size: {Size} bytes", _bucketName, key, data.Length);
        return key;
    }

    public async Task<byte[]> ReadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            using var response = await _s3Client.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);
            using var ms = new MemoryStream();
            await response.ResponseStream.CopyToAsync(ms, cancellationToken).ConfigureAwait(false);
            var data = ms.ToArray();
            _logger.LogDebug("S3 read succeeded: {Bucket}/{Key}, Size: {Size} bytes", _bucketName, key, data.Length);
            return data;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3 read failed: {Bucket}/{Key}, StatusCode: {StatusCode}",
                _bucketName, key, ex.StatusCode);
            throw;
        }
    }

    public async Task<string> WriteAsync(Stream data, string projectorName, CancellationToken cancellationToken = default)
    {
        var key = BuildKey(projectorName, Guid.NewGuid().ToString("N"));

        var request = new PutObjectRequest
        {
            BucketName = _bucketName,
            Key = key,
            InputStream = data,
            ContentType = "application/octet-stream"
        };

        if (_enableEncryption)
        {
            request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
        }

        await _s3Client.PutObjectAsync(request, cancellationToken).ConfigureAwait(false);
        _logger.LogDebug("S3 stream write succeeded: {Bucket}/{Key}", _bucketName, key);
        return key;
    }

    public async Task<Stream> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            var response = await _s3Client.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);
            return response.ResponseStream;
        }
        catch (AmazonS3Exception ex)
        {
            _logger.LogError(ex, "S3 OpenRead failed: {Bucket}/{Key}, StatusCode: {StatusCode}",
                _bucketName, key, ex.StatusCode);
            throw;
        }
    }

    private string BuildKey(string projectorName, string name)
    {
        var folder = string.IsNullOrEmpty(_prefix) ? projectorName : $"{_prefix.TrimEnd('/')}/{projectorName}";
        return $"{folder}/{name}.bin";
    }
}
