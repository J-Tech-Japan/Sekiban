using Amazon.Extensions.NETCore.Setup;
using Amazon.S3;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.BlobStorage.S3;

/// <summary>
///     Service collection extensions for S3 snapshot offloading.
/// </summary>
public static class SekibanDcbS3Extensions
{
    /// <summary>
    ///     Adds S3 blob storage accessor for snapshot offloading.
    /// </summary>
    public static IServiceCollection AddSekibanDcbS3BlobStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<S3BlobStorageOptions>(options =>
            configuration.GetSection("S3BlobStorage").Bind(options));

        services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
        {
            var options = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<S3BlobStorageOptions>>().Value;
            var logger = sp.GetService<ILogger<S3BlobStorageSnapshotAccessor>>();

            var s3Client = sp.GetService<IAmazonS3>();
            if (s3Client != null)
            {
                return new S3BlobStorageSnapshotAccessor(
                    s3Client,
                    options.BucketName,
                    options.Prefix,
                    options.EnableEncryption,
                    logger);
            }

            var config = new AmazonS3Config();
            if (!string.IsNullOrEmpty(options.Region))
            {
                config.RegionEndpoint = Amazon.RegionEndpoint.GetBySystemName(options.Region);
            }

            if (!string.IsNullOrEmpty(options.ServiceUrl))
            {
                config.ServiceURL = options.ServiceUrl;
                config.ForcePathStyle = options.ForcePathStyle;
            }

            var client = new AmazonS3Client(config);
            return new S3BlobStorageSnapshotAccessor(
                client,
                options.BucketName,
                options.Prefix,
                options.EnableEncryption,
                logger);
        });

        return services;
    }

    /// <summary>
    ///     Adds S3 blob storage accessor with explicit bucket configuration.
    /// </summary>
    public static IServiceCollection AddSekibanDcbS3BlobStorage(
        this IServiceCollection services,
        string bucketName,
        string? prefix = null,
        bool enableEncryption = true)
    {
        services.AddAWSService<IAmazonS3>();
        services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
        {
            var s3Client = sp.GetRequiredService<IAmazonS3>();
            var logger = sp.GetService<ILogger<S3BlobStorageSnapshotAccessor>>();
            return new S3BlobStorageSnapshotAccessor(
                s3Client,
                bucketName,
                prefix,
                enableEncryption,
                logger);
        });
        return services;
    }

    /// <summary>
    ///     Adds S3 blob storage accessor using Aspire-provided S3 client.
    /// </summary>
    public static IServiceCollection AddSekibanDcbS3BlobStorageWithAspire(
        this IServiceCollection services,
        string bucketName,
        string? prefix = null,
        bool enableEncryption = true)
    {
        services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
        {
            var s3Client = sp.GetRequiredService<IAmazonS3>();
            var logger = sp.GetService<ILogger<S3BlobStorageSnapshotAccessor>>();
            return new S3BlobStorageSnapshotAccessor(
                s3Client,
                bucketName,
                prefix,
                enableEncryption,
                logger);
        });
        return services;
    }
}
