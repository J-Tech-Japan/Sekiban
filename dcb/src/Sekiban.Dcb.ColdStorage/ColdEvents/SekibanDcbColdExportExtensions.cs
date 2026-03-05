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
        string coldEventSectionPath = "Sekiban:ColdEvent")
    {
        var coldConfig = configuration.GetSection(coldEventSectionPath);
        var configuredColdOptions = coldConfig.Get<ColdEventStoreOptions>() ?? new ColdEventStoreOptions();

        var exportInterval = ParsePositiveTimeSpan(configuration["ColdExport:Interval"]);
        var cycleBudget = ParsePositiveTimeSpan(configuration["ColdExport:CycleBudget"]);

        var coldOptions = configuredColdOptions with
        {
            Enabled = true,
            PullInterval = exportInterval ?? configuredColdOptions.PullInterval,
            ExportCycleBudget = cycleBudget ?? configuredColdOptions.ExportCycleBudget
        };

        services.AddSekibanDcbColdEvents(coldOptions);

        var storageOptions = coldConfig.GetSection("Storage").Get<ColdStorageOptions>() ?? new ColdStorageOptions();
        var storageRoot = ColdObjectStorageFactory.ResolveStorageRoot(
            storageOptions,
            string.IsNullOrWhiteSpace(contentRoot) ? Directory.GetCurrentDirectory() : contentRoot);

        services.AddSingleton(storageOptions);
        services.TryAddSingleton<IColdLeaseManager, StorageBackedColdLeaseManager>();
        services.TryAddSingleton<IColdObjectStorage>(sp =>
            ColdObjectStorageFactory.Create(storageOptions, storageRoot, sp));

        if (UsesAzureBlobProvider(storageOptions))
        {
            services.AddKeyedSingleton<BlobServiceClient>(storageOptions.AzureBlobClientName, (_, _) =>
            {
                var connectionString = configuration.GetConnectionString(storageOptions.AzureBlobClientName);
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

    private static TimeSpan? ParsePositiveTimeSpan(string? raw)
        => !string.IsNullOrWhiteSpace(raw) && TimeSpan.TryParse(raw, out var parsed) && parsed > TimeSpan.Zero
            ? parsed
            : null;
}
