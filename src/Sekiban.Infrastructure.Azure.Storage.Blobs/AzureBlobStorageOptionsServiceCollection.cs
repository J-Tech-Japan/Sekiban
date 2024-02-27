using Microsoft.AspNetCore.Builder;
namespace Sekiban.Infrastructure.Azure.Storage.Blobs;

public class AzureBlobStorageOptionsServiceCollection(SekibanCosmosDbOptions sekibanCosmosDbOptions, WebApplicationBuilder applicationBuilder)
{
    public SekibanCosmosDbOptions SekibanCosmosDbOptions { get; init; } = sekibanCosmosDbOptions;
    public WebApplicationBuilder ApplicationBuilder { get; init; } = applicationBuilder;
}
