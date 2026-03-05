using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Dcb.BlobStorage.AzureStorage;

namespace Sekiban.Dcb.ColdEvents;

public static class ColdObjectStorageFactory
{
    public const string DefaultBasePath = ".cold-events";

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
        var type = (options.Type ?? "jsonl").ToLowerInvariant();
        if (type is "sqlite" or "duckdb" or "jsonl")
        {
            Directory.CreateDirectory(storageRoot);
        }

        return type switch
        {
            "sqlite" => new SqliteColdObjectStorage(Path.Combine(storageRoot, options.SqliteFile)),
            "duckdb" => new DuckDbColdObjectStorage(Path.Combine(storageRoot, options.DuckDbFile)),
            "jsonl" => new JsonlColdObjectStorage(Path.Combine(storageRoot, options.JsonlDirectory)),
            "azureblob" => new AzureBlobColdObjectStorage(
                services.GetRequiredKeyedService<BlobServiceClient>(options.AzureBlobClientName),
                options.AzureContainerName,
                options.AzurePrefix),
            _ => throw new InvalidOperationException($"Unsupported cold storage type: {options.Type}")
        };
    }
}
