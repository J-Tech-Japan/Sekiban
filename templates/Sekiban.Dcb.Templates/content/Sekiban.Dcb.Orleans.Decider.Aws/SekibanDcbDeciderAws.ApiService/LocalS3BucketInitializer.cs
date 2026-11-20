using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.BlobStorage.S3;

namespace SekibanDcbDeciderAws.ApiService;

/// <summary>
///     Local development only: LocalStack starts without the snapshot bucket (in AWS the CDK stack creates it),
///     so snapshot offload would fail with "bucket does not exist". This creates the configured
///     <c>S3BlobStorage:BucketName</c> once S3 answers. Registered only in Development when
///     <c>S3BlobStorage:ServiceUrl</c> is a local emulator endpoint (<see cref="IsLocalEndpoint" />), so it never
///     creates buckets in a real AWS account.
/// </summary>
public sealed class LocalS3BucketInitializer(
    IAmazonS3 s3,
    IOptions<S3BlobStorageOptions> options,
    ILogger<LocalS3BucketInitializer> logger) : BackgroundService
{
    private const int MaxAttempts = 30;

    /// <summary>
    ///     True only for an emulator endpoint on this machine or in the local container network
    ///     (localhost, a loopback address, *.localhost, localstack, host.docker.internal).
    ///     Any other URL, including real AWS endpoints, is never used to create buckets.
    /// </summary>
    public static bool IsLocalEndpoint(string? serviceUrl)
    {
        if (string.IsNullOrWhiteSpace(serviceUrl) || !Uri.TryCreate(serviceUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.Trim('[', ']').ToLowerInvariant();
        return host is "localhost" or "localstack" or "host.docker.internal"
            || host.EndsWith(".localhost", StringComparison.Ordinal)
            || (System.Net.IPAddress.TryParse(host, out var address) && System.Net.IPAddress.IsLoopback(address));
    }
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var bucketName = options.Value.BucketName;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                if (!await AmazonS3Util.DoesS3BucketExistV2Async(s3, bucketName))
                {
                    await s3.PutBucketAsync(new PutBucketRequest { BucketName = bucketName }, stoppingToken);
                    logger.LogInformation("Created local S3 snapshot bucket {BucketName}", bucketName);
                }
                return;
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                // LocalStack may still be starting: retry for about a minute.
                logger.LogDebug(ex, "Local S3 not ready (attempt {Attempt}/{MaxAttempts})", attempt, MaxAttempts);
                await Task.Delay(RetryDelay, stoppingToken);
            }
        }

        logger.LogWarning(
            "Could not create local S3 snapshot bucket {BucketName}; snapshot offload will fail until it exists",
            bucketName);
    }
}
