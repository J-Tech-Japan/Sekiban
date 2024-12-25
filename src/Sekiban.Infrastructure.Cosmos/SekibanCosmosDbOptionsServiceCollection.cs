using Microsoft.Extensions.Hosting;
namespace Sekiban.Infrastructure.Cosmos;

public class SekibanCosmosDbOptionsServiceCollection(
    SekibanCosmosDbOptions sekibanCosmosDbOptions,
    SekibanCosmosClientOptions cosmosClientOptions,
    IHostApplicationBuilder applicationBuilder)
{
    public SekibanCosmosDbOptions SekibanCosmosDbOptions { get; init; } = sekibanCosmosDbOptions;
    public SekibanCosmosClientOptions CosmosClientOptions { get; init; } = cosmosClientOptions;
    public IHostApplicationBuilder ApplicationBuilder { get; init; } = applicationBuilder;
}
