using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Sekiban.Infrastructure.Azure.Storage.Blobs;
using Sekiban.Infrastructure.Cosmos;
namespace Sekiban.Aspire.Infrastructure.Cosmos;

public static class SekibanBlobAspireExtensions
{
    public static SekibanCosmosDbOptionsServiceCollection AddSekibanBlobAspire(
        this SekibanCosmosDbOptionsServiceCollection cosmosServiceCollection,
        string connectionName)
    {
        cosmosServiceCollection.ApplicationBuilder.AddKeyedAzureBlobClient(connectionName);
        cosmosServiceCollection.ApplicationBuilder.Services.AddSingleton(new SekibanBlobAspireOptions(connectionName));
        cosmosServiceCollection.ApplicationBuilder.Services.AddTransient<IBlobContainerAccessor, AzureAspireBlobContainerAccessor>();
        return cosmosServiceCollection;
    }
}
