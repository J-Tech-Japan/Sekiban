using Microsoft.Extensions.Configuration;
using Sekiban.Core.Setting;
namespace Sekiban.Infrastructure.Azure.Storage.Blobs;

public record SekibanAzureBlobStorageOption
{
    public static readonly string BlobConnectionStringNameDefaultValue = "SekibanBlob";

    public string Context { get; init; } = SekibanContext.Default;

    public string BlobConnectionStringName { get; init; } = BlobConnectionStringNameDefaultValue;
    public string? BlobConnectionString { get; init; }

    public bool LegacyPartitions { get; init; }

    public static SekibanAzureBlobStorageOption FromConfiguration(
        IConfigurationSection section,
        IConfigurationRoot configurationRoot,
        string context = SekibanContext.Default)
    {
        var azureSection = section.GetSection("Azure");

        var blobConnectionStringName = azureSection.GetValue<string>(nameof(BlobConnectionStringName)) ?? BlobConnectionStringNameDefaultValue;
        var blobConnectionString = configurationRoot.GetConnectionString(blobConnectionStringName) ??
            azureSection.GetValue<string>(nameof(BlobConnectionString));

        return new SekibanAzureBlobStorageOption
        {
            Context = context, BlobConnectionString = blobConnectionString, BlobConnectionStringName = blobConnectionStringName
        };
    }
}
