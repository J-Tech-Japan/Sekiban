using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.BlobStorage.AzureStorage;

namespace Sekiban.Dcb.ColdEvents;

public static class ColdObjectStorageFactory
{
    public const string DefaultBasePath = ".cold-events";
    private const string DefaultProvider = "local";
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
        if (provider == "local" && format is "sqlite" or "duckdb" or "jsonl")
        {
            Directory.CreateDirectory(storageRoot);
        }

        return (provider, format) switch
        {
            ("local", "sqlite") => new SqliteColdObjectStorage(Path.Combine(storageRoot, options.SqliteFile)),
            ("local", "duckdb") => new DuckDbColdObjectStorage(Path.Combine(storageRoot, options.DuckDbFile)),
            ("local", "jsonl") => new JsonlColdObjectStorage(Path.Combine(storageRoot, options.JsonlDirectory)),
            ("azureblob", "jsonl") => new AzureBlobColdObjectStorage(
                services.GetRequiredKeyedService<BlobServiceClient>(options.AzureBlobClientName),
                options.AzureContainerName,
                options.AzurePrefix),
            ("azureblob", "sqlite") => new AzureBlobSqliteColdObjectStorage(
                services.GetRequiredKeyedService<BlobServiceClient>(options.AzureBlobClientName),
                options.AzureContainerName,
                options.AzurePrefix,
                options.SqliteFile),
            ("azureblob", "duckdb") => new AzureBlobDuckDbColdObjectStorage(
                services.GetRequiredKeyedService<BlobServiceClient>(options.AzureBlobClientName),
                options.AzureContainerName,
                options.AzurePrefix,
                options.DuckDbFile),
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
            provider = type == "azureblob" ? "azureblob" : DefaultProvider;
        }

        if (string.IsNullOrWhiteSpace(format))
        {
            format = type switch
            {
                "sqlite" => "sqlite",
                "duckdb" => "duckdb",
                _ => DefaultFormat
            };
        }

        return (provider, format);
    }
}
