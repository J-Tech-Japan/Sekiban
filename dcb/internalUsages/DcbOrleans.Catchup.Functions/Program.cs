using Azure.Storage.Blobs;
using Dcb.Domain.WithoutResult;
using DcbOrleans.Catchup.Functions;
using Microsoft.Extensions.Options;
using Sekiban.Dcb.ColdEvents;
using Sekiban.Dcb.Postgres;

var builder = Host.CreateApplicationBuilder(args);
builder.AddServiceDefaults();

var domainTypes = DomainType.GetDomainTypes();
builder.Services.AddSingleton(domainTypes);
builder.Services.AddSekibanDcbPostgresWithAspire();

var coldConfig = builder.Configuration.GetSection("Sekiban:ColdEvent");
var configuredColdOptions = coldConfig.Get<ColdEventStoreOptions>() ?? new ColdEventStoreOptions();
var coldOptions = configuredColdOptions.Enabled
    ? configuredColdOptions
    : configuredColdOptions with { Enabled = true };
builder.Services.AddSingleton<IOptions<ColdEventStoreOptions>>(Options.Create(coldOptions));

var storageOptions = coldConfig.GetSection("Storage").Get<ColdStorageOptions>() ?? new ColdStorageOptions();
var storageRoot = ColdObjectStorageFactory.ResolveStorageRoot(storageOptions, Directory.GetCurrentDirectory());
builder.Services.AddSingleton(storageOptions);

if (string.Equals(storageOptions.Provider, "azureblob", StringComparison.OrdinalIgnoreCase)
    || string.Equals(storageOptions.Type, "azureblob", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddKeyedSingleton<BlobServiceClient>(storageOptions.AzureBlobClientName, (sp, _) =>
    {
        var connectionString = builder.Configuration.GetConnectionString(storageOptions.AzureBlobClientName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string '{storageOptions.AzureBlobClientName}' is required for Azure Blob cold storage.");
        }

        return new BlobServiceClient(connectionString);
    });
}

builder.Services.AddSingleton<IColdObjectStorage>(sp =>
    ColdObjectStorageFactory.Create(storageOptions, storageRoot, sp));
builder.Services.AddSingleton<IColdLeaseManager, StorageBackedColdLeaseManager>();
builder.Services.AddSingleton<ColdExporter>();
builder.Services.AddSingleton<IColdEventExporter>(sp => sp.GetRequiredService<ColdExporter>());
builder.Services.AddHostedService<ColdExportTimerService>();

var app = builder.Build();
app.Run();
