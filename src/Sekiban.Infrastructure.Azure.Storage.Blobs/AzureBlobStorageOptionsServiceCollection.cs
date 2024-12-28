using Microsoft.Extensions.Hosting;
namespace Sekiban.Infrastructure.Azure.Storage.Blobs;

public class AzureBlobStorageOptionsServiceCollection(
    SekibanAzureBlobStorageOptions sekibanAzureBlobStorageOptions,
    IHostApplicationBuilder applicationBuilder)
{
    public SekibanAzureBlobStorageOptions SekibanAzureBlobStorageOptions { get; init; } = sekibanAzureBlobStorageOptions;
    public IHostApplicationBuilder ApplicationBuilder { get; init; } = applicationBuilder;
}
