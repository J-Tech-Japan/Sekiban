using Microsoft.Extensions.DependencyInjection;
namespace Sekiban.Infrastructure.Cosmos;

public class SekibanCosmosDbOptionsServiceCollection(
    SekibanCosmosDbOptions sekibanCosmosDbOptions,
    SekibanCosmosClientOptions cosmosClientOptions,
    IServiceCollection serviceCollection)
{
    public SekibanCosmosDbOptions SekibanCosmosDbOptions { get; init; } = sekibanCosmosDbOptions;
    public SekibanCosmosClientOptions CosmosClientOptions { get; init; } = cosmosClientOptions;
    public IServiceCollection ServiceCollection { get; init; } = serviceCollection;
}
