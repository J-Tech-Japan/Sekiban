using System.Globalization;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Sekiban.Dcb.ColdEvents;

public static class SekibanDcbColdExportExtensions
{
    public static IServiceCollection AddSekibanDcbColdExport(
        this IServiceCollection services,
        IConfiguration configuration,
        string? contentRoot = null,
        string coldEventSectionPath = "Sekiban:ColdEvent",
        bool addBackgroundService = true)
    {
        var coldConfig = configuration.GetSection(coldEventSectionPath);
        var configuredColdOptions = coldConfig.Get<ColdEventStoreOptions>() ?? new ColdEventStoreOptions();
        var enabled = ResolveEnabled(coldConfig, configuredColdOptions);

        var exportInterval = ParsePositiveTimeSpan(configuration["ColdExport:Interval"]);
        var cycleBudget = ParsePositiveTimeSpan(configuration["ColdExport:CycleBudget"]);

        var coldOptions = configuredColdOptions with
        {
            Enabled = enabled,
            PullInterval = exportInterval ?? configuredColdOptions.PullInterval,
            ExportCycleBudget = cycleBudget ?? configuredColdOptions.ExportCycleBudget
        };

        services.AddSekibanDcbColdEvents(coldOptions, addBackgroundService);

        var storageOptions = coldConfig.GetSection("Storage").Get<ColdStorageOptions>() ?? new ColdStorageOptions();
        var storageRoot = ColdObjectStorageFactory.ResolveStorageRoot(
            storageOptions,
            string.IsNullOrWhiteSpace(contentRoot) ? Directory.GetCurrentDirectory() : contentRoot);

        services.AddSingleton(storageOptions);
        services.TryAddSingleton<IColdLeaseManager, StorageBackedColdLeaseManager>();
        services.TryAddSingleton<IColdObjectStorage>(sp =>
            ColdObjectStorageFactory.Create(storageOptions, storageRoot, sp));
        if (string.Equals(storageOptions.Format, "duckdb", StringComparison.OrdinalIgnoreCase)
            || string.Equals(storageOptions.Type, "duckdb", StringComparison.OrdinalIgnoreCase))
        {
            services.Replace(ServiceDescriptor.Singleton<IColdSegmentFormatHandler, DuckDbColdSegmentFormatHandler>());
        }

        if (UsesAzureBlobProvider(storageOptions))
        {
            services.AddKeyedSingleton<BlobServiceClient>(storageOptions.AzureBlobClientName, (_, _) =>
            {
                var connectionString = ResolveConnectionString(configuration, storageOptions.AzureBlobClientName);
                if (string.IsNullOrWhiteSpace(connectionString))
                {
                    throw new InvalidOperationException(
                        $"Connection string '{storageOptions.AzureBlobClientName}' is required for Azure Blob cold storage.");
                }

                return new BlobServiceClient(connectionString);
            });
        }

        return services;
    }

    private static bool UsesAzureBlobProvider(ColdStorageOptions options)
        => string.Equals(options.Provider, "azureblob", StringComparison.OrdinalIgnoreCase)
           || string.Equals(options.Type, "azureblob", StringComparison.OrdinalIgnoreCase);

    private static string? ResolveConnectionString(IConfiguration configuration, string connectionName)
        => configuration.GetConnectionString(connectionName)
           ?? configuration[connectionName]
           ?? configuration[$"{connectionName}:ConnectionString"];

    private static bool ResolveEnabled(IConfigurationSection coldConfig, ColdEventStoreOptions configuredColdOptions)
        => string.IsNullOrWhiteSpace(coldConfig["Enabled"])
            ? true
            : configuredColdOptions.Enabled;

    private static TimeSpan? ParsePositiveTimeSpan(string? raw)
        => !string.IsNullOrWhiteSpace(raw)
           && TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var parsed)
           && parsed > TimeSpan.Zero
            ? parsed
            : null;
}
