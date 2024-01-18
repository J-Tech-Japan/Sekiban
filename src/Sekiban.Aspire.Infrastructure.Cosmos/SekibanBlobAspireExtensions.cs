using Microsoft.Extensions.Hosting;
namespace Sekiban.Infrastructure.Cosmos.Aspire;

public static class SekibanBlobAspireExtensions
{
    public static SekibanCosmosDbOptionsServiceCollection AddSekibanBlobAspire(
        this SekibanCosmosDbOptionsServiceCollection cosmosServiceCollection,
        string connectionName)
    {
        cosmosServiceCollection.ApplicationBuilder.AddKeyedAzureBlobService(connectionName);
        return cosmosServiceCollection;
    }
}