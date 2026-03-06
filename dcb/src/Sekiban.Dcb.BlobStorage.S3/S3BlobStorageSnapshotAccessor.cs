using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sekiban.Dcb.Snapshots;
using System.Security.Cryptography;
using System.Text;

namespace Sekiban.Dcb.BlobStorage.S3;

/// <summary>
///     AWS S3 implementation of IBlobStorageSnapshotAccessor.
/// </summary>
public sealed class S3BlobStorageSnapshotAccessor : IBlobStorageSnapshotAccessor
{
    private const string DefaultLocalCacheFolderName = "sekiban-snapshot-cache";
    private readonly IAmazonS3 _s3Client;
    private readonly string _bucketName;
    private readonly string _prefix;
    private readonly bool _enableEncryption;
    private readonly ILogger<S3BlobStorageSnapshotAccessor> _logger;
    private readonly string _localCacheDirectory;

    public string ProviderName => "AwsS3";

    public S3BlobStorageSnapshotAccessor(
        IAmazonS3 s3Client,
        string bucketName,
        string? prefix = null,
        bool enableEncryption = true,
        string? localCacheDirectory = null,
        ILogger<S3BlobStorageSnapshotAccessor>? logger = null)
    {
        _s3Client = s3Client ?? throw new ArgumentNullException(nameof(s3Client));
        _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
        _prefix = prefix ?? string.Empty;
        _enableEncryption = enableEncryption;
        _localCacheDirectory = localCacheDirectory ?? Path.Combine(Path.GetTempPath(), DefaultLocalCacheFolderName);
        _logger = logger ?? NullLogger<S3BlobStorageSnapshotAccessor>.Instance;
    }

    public S3BlobStorageSnapshotAccessor(
        string bucketName,
        string? prefix = null,
        bool enableEncryption = true,
        string? localCacheDirectory = null,
        ILogger<S3BlobStorageSnapshotAccessor>? logger = null)
    {
        _s3Client = new AmazonS3Client();
        _bucketName = bucketName ?? throw new ArgumentNullException(nameof(bucketName));
        _prefix = prefix ?? string.Empty;
        _enableEncryption = enableEncryption;
        _localCacheDirectory = localCacheDirectory ?? Path.Combine(Path.GetTempPath(), DefaultLocalCacheFolderName);
        _logger = logger ?? NullLogger<S3BlobStorageSnapshotAccessor>.Instance;
    }

    public async Task<string> WriteAsync(Stream data, string projectorName, CancellationToken cancellationToken = default)
    {
        var key = data.CanSeek
            ? StreamOffloadHelper.ComputeDeterministicKey(
                projectorName,
                await StreamOffloadHelper.ComputeContentHashAsync(data, cancellationToken).ConfigureAwait(false))
            : BuildKey(projectorName, Guid.NewGuid().ToString("N"));
        if (data.CanSeek)
        {
            data.Position = 0;
        }

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
            var cachePath = BuildCachePath(key);
            if (File.Exists(cachePath))
            {
                return OpenCachedFile(cachePath);
            }

            var request = new GetObjectRequest
            {
                BucketName = _bucketName,
                Key = key
            };

            Directory.CreateDirectory(_localCacheDirectory);
            var tempPath = cachePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using var response = await _s3Client.GetObjectAsync(request, cancellationToken).ConfigureAwait(false);
                await using (var target = new FileStream(
                                 tempPath,
                                 FileMode.CreateNew,
                                 FileAccess.Write,
                                 FileShare.None,
                                 bufferSize: 81920,
                                 FileOptions.Asynchronous | FileOptions.SequentialScan))
                {
                    await response.ResponseStream.CopyToAsync(target, cancellationToken).ConfigureAwait(false);
                }

                PromoteTempCacheFile(tempPath, cachePath);
                return OpenCachedFile(cachePath);
            }
            finally
            {
                SafeDelete(tempPath);
            }
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
