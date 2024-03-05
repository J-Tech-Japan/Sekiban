using Microsoft.AspNetCore.Builder;
namespace Sekiban.Infrastructure.Azure.Storage.Blobs;

public class AzureBlobStorageOptionsServiceCollection(
    SekibanAzureBlobStorageOptions sekibanAzureBlobStorageOptions,
    WebApplicationBuilder applicationBuilder)
{
    public SekibanAzureBlobStorageOptions SekibanAzureBlobStorageOptions { get; init; } = sekibanAzureBlobStorageOptions;
    public WebApplicationBuilder ApplicationBuilder { get; init; } = applicationBuilder;
}
