using Microsoft.AspNetCore.Builder;
using Sekiban.Infrastructure.Azure.Storage.Blobs;
namespace Sekiban.Infrastructure.Cosmos;

public class SekibanCosmosDbOptionsServiceCollection(
    SekibanCosmosDbOptions sekibanCosmosDbOptions,
    SekibanCosmosClientOptions cosmosClientOptions,
    WebApplicationBuilder applicationBuilder)
{
    public SekibanCosmosDbOptions SekibanCosmosDbOptions { get; init; } = sekibanCosmosDbOptions;
    public SekibanCosmosClientOptions CosmosClientOptions { get; init; } = cosmosClientOptions;
    public WebApplicationBuilder ApplicationBuilder { get; init; } = applicationBuilder;
}
