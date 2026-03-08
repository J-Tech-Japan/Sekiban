using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.BlobStorage.AzureStorage;

namespace Sekiban.Dcb.ColdEvents;

public static class ColdObjectStorageFactory
{
    public const string DefaultBasePath = ".cold-events";
    private const string ProviderLocal = "local";
    private const string ProviderAzureBlob = "azureblob";
    private const string FormatSqlite = "sqlite";
    private const string FormatDuckDb = "duckdb";
    private const string DefaultProvider = ProviderLocal;
    private const string DefaultFormat = "jsonl";

    public static string ResolveStorageRoot(
        ColdStorageOptions options,
        string contentRoot,
        string? defaultBasePathOverride = null)
    {
        var configuredBasePath = options.BasePath;
        var defaultBasePath = string.IsNullOrWhiteSpace(defaultBasePathOverride)
            ? DefaultBasePath
            : defaultBasePathOverride;

        var effectiveBasePath = string.IsNullOrWhiteSpace(configuredBasePath)
            ? defaultBasePath
            : configuredBasePath;

        if (Path.IsPathRooted(effectiveBasePath))
        {
            return Path.GetFullPath(effectiveBasePath);
        }

        return Path.GetFullPath(Path.Combine(contentRoot, effectiveBasePath));
    }

    public static IColdObjectStorage Create(
        ColdStorageOptions options,
        string storageRoot,
        IServiceProvider services)
    {
        var (provider, format) = ResolveProviderAndFormat(options);
        if (provider == ProviderLocal && format is FormatSqlite or FormatDuckDb or DefaultFormat)
        {
            Directory.CreateDirectory(storageRoot);
        }

        return (provider, format) switch
        {
            (ProviderLocal, FormatSqlite) => new JsonlColdObjectStorage(storageRoot),
            (ProviderLocal, FormatDuckDb) => new JsonlColdObjectStorage(storageRoot),
            (ProviderLocal, DefaultFormat) => new JsonlColdObjectStorage(Path.Combine(storageRoot, options.JsonlDirectory)),
            (ProviderAzureBlob, DefaultFormat) => new AzureBlobColdObjectStorage(
                services.GetRequiredKeyedService<BlobServiceClient>(options.AzureBlobClientName),
                options.AzureContainerName,
                options.AzurePrefix),
            (ProviderAzureBlob, FormatSqlite) => new AzureBlobColdObjectStorage(
                services.GetRequiredKeyedService<BlobServiceClient>(options.AzureBlobClientName),
                options.AzureContainerName,
                options.AzurePrefix),
            (ProviderAzureBlob, FormatDuckDb) => new AzureBlobColdObjectStorage(
                services.GetRequiredKeyedService<BlobServiceClient>(options.AzureBlobClientName),
                options.AzureContainerName,
                options.AzurePrefix),
            _ => throw new InvalidOperationException(
                $"Unsupported cold storage provider/format: provider={provider}, format={format}, type={options.Type}")
        };
    }

    private static (string Provider, string Format) ResolveProviderAndFormat(ColdStorageOptions options)
    {
        var type = (options.Type ?? DefaultFormat).ToLowerInvariant();
        var provider = (options.Provider ?? string.Empty).Trim().ToLowerInvariant();
        var format = (options.Format ?? string.Empty).Trim().ToLowerInvariant();

        if (string.IsNullOrWhiteSpace(provider))
        {
            provider = type == ProviderAzureBlob ? ProviderAzureBlob : DefaultProvider;
        }

        if (string.IsNullOrWhiteSpace(format))
        {
            format = type switch
            {
                FormatSqlite => FormatSqlite,
                FormatDuckDb => FormatDuckDb,
                _ => DefaultFormat
            };
        }

        return (provider, format);
    }
}
