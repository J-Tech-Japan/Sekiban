using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Cache;
using Sekiban.Core.Documents;
using Sekiban.Infrastructure.Cosmos;
namespace Sekiban.Aspire.Infrastructure.Cosmos;

public class AspireCosmosDbFactory(
    SekibanCosmosAspireOptions sekibanCosmosAspireOptions,
    IMemoryCacheAccessor memoryCache,
    IServiceProvider serviceProvider,
    SekibanCosmosClientOptions options,
    SekibanCosmosDbOptions cosmosDbOptions) : ICosmosDbFactory
{
    public readonly CosmosDbFactory cosmosDbFactory = new(memoryCache, serviceProvider, options, cosmosDbOptions);

    public async Task DeleteAllFromEventContainer(AggregateContainerGroup containerGroup)
    {
        cosmosDbFactory.SearchCosmosClientAsync = GetCosmosClientFromAspire;
        await cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.Event, containerGroup);
    }
    public async Task DeleteAllFromItemsContainer(AggregateContainerGroup containerGroup)
    {
        cosmosDbFactory.SearchCosmosClientAsync = GetCosmosClientFromAspire;
        await cosmosDbFactory.DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command, containerGroup);
    }

    public async Task<T> CosmosActionAsync<T>(
        DocumentType documentType,
        AggregateContainerGroup containerGroup,
        Func<Container, Task<T>> cosmosAction)
    {
        cosmosDbFactory.SearchCosmosClientAsync = GetCosmosClientFromAspire;
        return await cosmosDbFactory.CosmosActionAsync(documentType, containerGroup, cosmosAction);
    }

    public async Task CosmosActionAsync(DocumentType documentType, AggregateContainerGroup containerGroup, Func<Container, Task> cosmosAction)
    {
        cosmosDbFactory.SearchCosmosClientAsync = GetCosmosClientFromAspire;
        await cosmosDbFactory.CosmosActionAsync(documentType, containerGroup, cosmosAction);
    }
    private async Task<CosmosClient?> GetCosmosClientFromAspire()
    {
        await Task.CompletedTask;
        var client = serviceProvider.GetKeyedService<CosmosClient>(sekibanCosmosAspireOptions.ConnectionName);
        return client;
    }
}
