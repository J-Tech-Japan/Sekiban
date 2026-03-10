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
            (ProviderLocal, FormatSqlite) => new JsonlColdObjectStorage(
                GetScopedLocalStoragePath(storageRoot, GetStorageScope(options, FormatSqlite), FormatSqlite)),
            (ProviderLocal, FormatDuckDb) => new JsonlColdObjectStorage(
                GetScopedLocalStoragePath(storageRoot, GetStorageScope(options, FormatDuckDb), FormatDuckDb)),
            (ProviderLocal, DefaultFormat) => new JsonlColdObjectStorage(
                GetScopedLocalStoragePath(storageRoot, GetStorageScope(options, DefaultFormat), DefaultFormat)),
            (ProviderAzureBlob, DefaultFormat) => new AzureBlobColdObjectStorage(
                services.GetRequiredKeyedService<BlobServiceClient>(options.AzureBlobClientName),
                options.AzureContainerName,
                GetAzureStoragePrefix(options, DefaultFormat)),
            (ProviderAzureBlob, FormatSqlite) => new AzureBlobColdObjectStorage(
                services.GetRequiredKeyedService<BlobServiceClient>(options.AzureBlobClientName),
                options.AzureContainerName,
                GetAzureStoragePrefix(options, FormatSqlite)),
            (ProviderAzureBlob, FormatDuckDb) => new AzureBlobColdObjectStorage(
                services.GetRequiredKeyedService<BlobServiceClient>(options.AzureBlobClientName),
                options.AzureContainerName,
                GetAzureStoragePrefix(options, FormatDuckDb)),
            _ => throw new InvalidOperationException(
                $"Unsupported cold storage provider/format: provider={provider}, format={format}, type={options.Type}")
        };
    }

    internal static string GetStorageScope(ColdStorageOptions options, string format)
        => format switch
        {
            FormatSqlite => options.SqliteFile,
            FormatDuckDb => options.DuckDbFile,
            _ => options.JsonlDirectory
        };

    internal static string? GetAzureStoragePrefix(ColdStorageOptions options, string format)
    {
        var scope = GetStorageScope(options, format);
        if (format == DefaultFormat && HasLegacyJsonlScope(scope))
        {
            return NormalizePathSegment(options.AzurePrefix) switch
            {
                "" => null,
                var prefix => prefix
            };
        }

        return CombineAzurePrefix(options.AzurePrefix, scope);
    }

    private static string GetScopedLocalStoragePath(string storageRoot, string configuredScope, string format)
    {
        var scopedPath = Path.Combine(storageRoot, configuredScope);
        if (!File.Exists(scopedPath))
        {
            return scopedPath;
        }

        throw new InvalidOperationException(
            $"Cold storage format '{format}' now stores segmented artifacts under a directory scope. " +
            $"Found an existing file at '{scopedPath}' for configured scope '{configuredScope}'. " +
            "Move or remove the legacy database file before starting with the segmented storage layout.");
    }

    internal static string? CombineAzurePrefix(string? prefix, string scope)
    {
        var normalizedPrefix = NormalizePathSegment(prefix);
        var normalizedScope = NormalizePathSegment(scope);

        return (normalizedPrefix, normalizedScope) switch
        {
            ("", "") => null,
            ("", _) => normalizedScope,
            (_, "") => normalizedPrefix,
            _ => $"{normalizedPrefix}/{normalizedScope}"
        };
    }

    private static string NormalizePathSegment(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Replace('\\', '/').Trim('/');

    private static bool HasLegacyJsonlScope(string scope)
        => string.Equals(
            NormalizePathSegment(scope),
            NormalizePathSegment(new ColdStorageOptions().JsonlDirectory),
            StringComparison.Ordinal);

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
