using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.Snapshots;

namespace Sekiban.Dcb.BlobStorage.AzureStorage;

public static class SekibanDcbAzureBlobExtensions
{
    public static IServiceCollection AddSekibanDcbAzureBlobStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureBlobStorageOptions>(options =>
            configuration.GetSection("AzureBlobStorage").Bind(options));

        services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureBlobStorageOptions>>().Value;
            var logger = sp.GetService<ILogger<AzureBlobStorageSnapshotAccessor>>();

            var blobServiceClient = sp.GetService<BlobServiceClient>();
            if (blobServiceClient is not null)
            {
                return new AzureBlobStorageSnapshotAccessor(blobServiceClient, options.ContainerName, options.Prefix, logger);
            }

            return new AzureBlobStorageSnapshotAccessor(options.ConnectionString, options.ContainerName, options.Prefix, logger);
        });

        return services;
    }

    public static IServiceCollection AddSekibanDcbAzureBlobStorage(
        this IServiceCollection services,
        string connectionString,
        string containerName,
        string? prefix = null)
    {
        services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
        {
            var logger = sp.GetService<ILogger<AzureBlobStorageSnapshotAccessor>>();
            return new AzureBlobStorageSnapshotAccessor(connectionString, containerName, prefix, logger);
        });
        return services;
    }

    public static IServiceCollection AddSekibanDcbAzureBlobStorageWithAspire(
        this IServiceCollection services,
        string containerName,
        string? prefix = null)
    {
        services.AddSingleton<IBlobStorageSnapshotAccessor>(sp =>
        {
            var blobServiceClient = sp.GetRequiredService<BlobServiceClient>();
            var logger = sp.GetService<ILogger<AzureBlobStorageSnapshotAccessor>>();
            return new AzureBlobStorageSnapshotAccessor(blobServiceClient, containerName, prefix, logger);
        });
        return services;
    }

    public static IServiceCollection AddSekibanDcbAzureColdObjectStorage(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.Configure<AzureBlobStorageOptions>(options =>
            configuration.GetSection("AzureBlobStorage").Bind(options));

        services.AddSingleton<IColdObjectStorage>(sp =>
        {
            var options = sp.GetRequiredService<IOptions<AzureBlobStorageOptions>>().Value;
            var logger = sp.GetService<ILogger<AzureBlobColdObjectStorage>>();

            var blobServiceClient = sp.GetService<BlobServiceClient>();
            if (blobServiceClient is not null)
            {
                return new AzureBlobColdObjectStorage(blobServiceClient, options.ContainerName, options.Prefix, logger);
            }

            return new AzureBlobColdObjectStorage(options.ConnectionString, options.ContainerName, options.Prefix, logger);
        });

        return services;
    }

    public static IServiceCollection AddSekibanDcbAzureColdObjectStorage(
        this IServiceCollection services,
        string connectionString,
        string containerName,
        string? prefix = null)
    {
        services.AddSingleton<IColdObjectStorage>(sp =>
        {
            var logger = sp.GetService<ILogger<AzureBlobColdObjectStorage>>();
            return new AzureBlobColdObjectStorage(connectionString, containerName, prefix, logger);
        });
        return services;
    }

    public static IServiceCollection AddSekibanDcbAzureColdObjectStorageWithAspire(
        this IServiceCollection services,
        string containerName,
        string? prefix = null)
    {
        services.AddSingleton<IColdObjectStorage>(sp =>
        {
            var blobServiceClient = sp.GetRequiredService<BlobServiceClient>();
            var logger = sp.GetService<ILogger<AzureBlobColdObjectStorage>>();
            return new AzureBlobColdObjectStorage(blobServiceClient, containerName, prefix, logger);
        });
        return services;
    }
}
