using LanguageExt.Common;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Cache;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
namespace Sekiban.Infrastructure.Cosmos;

public class CosmosDbFactory(
    IMemoryCacheAccessor memoryCache,
    IServiceProvider serviceProvider,
    SekibanCosmosClientOptions options,
    SekibanCosmosDbOptions cosmosDbOptions)
{
    private SekibanAzureOption GetSekibanCosmosDbOption() => cosmosDbOptions.GetContextOption(serviceProvider);
    private string GetContainerId(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        var dbOption = GetSekibanCosmosDbOption();
        return (documentType, containerGroup) switch
        {
            (DocumentType.Event, AggregateContainerGroup.Default) => dbOption.CosmosEventsContainer,
            (DocumentType.Event, AggregateContainerGroup.Dissolvable) => dbOption.CosmosEventsContainerDissolvable,
            (_, AggregateContainerGroup.Default) => dbOption.CosmosItemsContainer,
            _ => dbOption.CosmosItemsContainerDissolvable
        };
    }
    private string SekibanContextIdentifier()
    {
        var sekibanContext = serviceProvider.GetService<ISekibanContext>();
        return sekibanContext?.SettingGroupIdentifier ?? SekibanContext.Default;
    }

    private bool GetSupportsHierarchicalPartitions()
    {
        var dbOption = GetSekibanCosmosDbOption();
        return !dbOption.LegacyPartitions;
    }

    private static string GetMemoryCacheContainerKey(
        DocumentType documentType,
        string databaseId,
        string containerId,
        string sekibanContextIdentifier) =>
        $"{(documentType == DocumentType.Event ? "event." : "")}cosmosdb.container.{databaseId}.{containerId}.{sekibanContextIdentifier}";

    private static string GetMemoryCacheClientKey(DocumentType documentType, string sekibanContextIdentifier) =>
        $"{(documentType == DocumentType.Event ? "event." : "")}cosmosdb.client.{sekibanContextIdentifier}";

    private static string GetMemoryCacheDatabaseKey(DocumentType documentType, string databaseId, string sekibanContextIdentifier) =>
        $"{(documentType == DocumentType.Event ? "event." : "")}cosmosdb.container.{databaseId}.{sekibanContextIdentifier}";

    private string GetUri()
    {
        var dbOption = GetSekibanCosmosDbOption();
        return dbOption.CosmosEndPointUrl ?? string.Empty;
    }

    private string GetSecurityKey()
    {
        var dbOption = GetSekibanCosmosDbOption();
        return dbOption.CosmosAuthorizationKey ?? string.Empty;
    }


    private Result<string> GetConnectionString()
    {
        var dbOption = GetSekibanCosmosDbOption();
        if (string.IsNullOrWhiteSpace(dbOption.CosmosConnectionString))
        {
            return new Result<string>(new InvalidDataException(""));
        }
        return dbOption.CosmosConnectionString ?? string.Empty;
    }
    private string GetDatabaseId()
    {
        var dbOption = GetSekibanCosmosDbOption();
        return dbOption.CosmosDatabase ?? string.Empty;
    }

    private async Task<Container> GetContainerAsync(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        var databaseId = GetDatabaseId();
        var containerId = GetContainerId(documentType, containerGroup);
        var container = (Container?)memoryCache.Cache.Get(
            GetMemoryCacheContainerKey(documentType, databaseId, containerId, SekibanContextIdentifier()));

        if (container is not null)
        {
            return container;
        }
        var client = memoryCache.Cache.Get<CosmosClient?>(GetMemoryCacheClientKey(documentType, SekibanContextIdentifier()));
        if (client is null)
        {
            var clientOptions = options.ClientOptions;
            var connectionString = GetConnectionString();
            client = connectionString.Match(
                v => new CosmosClient(v, clientOptions),
                _ =>
                {
                    var uri = GetUri();
                    var securityKey = GetSecurityKey();
                    return new CosmosClient(uri, securityKey, clientOptions);
                });
            memoryCache.Cache.Set(GetMemoryCacheClientKey(documentType, SekibanContextIdentifier()), client, new MemoryCacheEntryOptions());
        }

        var database = memoryCache.Cache.Get<Database?>(GetMemoryCacheDatabaseKey(documentType, databaseId, SekibanContextIdentifier()));
        if (database is null)
        {
            database = await client.CreateDatabaseIfNotExistsAsync(databaseId);
            memoryCache.Cache.Set(
                GetMemoryCacheDatabaseKey(documentType, databaseId, SekibanContextIdentifier()),
                database,
                new MemoryCacheEntryOptions());
        }

        var containerProperties = new ContainerProperties(containerId, GetPartitionKeyPaths(GetSupportsHierarchicalPartitions()));
        container = await database.CreateContainerIfNotExistsAsync(containerProperties, 400);
        memoryCache.Cache.Set(
            GetMemoryCacheContainerKey(documentType, databaseId, containerId, SekibanContextIdentifier()),
            container,
            new MemoryCacheEntryOptions());

        return container;
    }

    public async Task DeleteAllFromAggregateFromContainerIncludes(
        DocumentType documentType,
        AggregateContainerGroup containerGroup = AggregateContainerGroup.Default)
    {
        await CosmosActionAsync<IEnumerable<IEvent>?>(
            documentType,
            containerGroup,
            async container =>
            {
                var query = container.GetItemLinqQueryable<IDocument>().Where(b => true);
                var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition());

                var deleteItemIds = new List<(Guid id, PartitionKey partitionKey)>();
                while (feedIterator.HasMoreResults)
                {
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        var id = SekibanJsonHelper.GetValue<Guid>(item, nameof(IDocument.Id));
                        var partitionKey = SekibanJsonHelper.GetValue<string>(item, nameof(IDocument.PartitionKey));
                        var rootPartitionKey = SekibanJsonHelper.GetValue<string>(item, nameof(IDocument.RootPartitionKey));
                        var aggregateType = SekibanJsonHelper.GetValue<string>(item, nameof(IDocument.AggregateType));
                        if (id is null || partitionKey is null)
                        {
                            continue;
                        }

                        deleteItemIds.Add((id, new PartitionKeyBuilder().Add(rootPartitionKey).Add(aggregateType).Add(partitionKey).Build()));
                    }
                }

                var concurrencyTasks = new List<Task>();
                foreach (var (id, partitionKey) in deleteItemIds)
                {
                    concurrencyTasks.Add(container.DeleteItemAsync<IDocument>(id.ToString(), partitionKey));
                }

                await Task.WhenAll(concurrencyTasks);
                return null;
            });
    }

    public async Task DeleteAllFromEventContainer(AggregateContainerGroup containerGroup)
    {
        await DeleteAllFromAggregateFromContainerIncludes(DocumentType.Event, containerGroup);
    }
    public async Task DeleteAllFromItemsContainer(AggregateContainerGroup containerGroup)
    {
        await DeleteAllFromAggregateFromContainerIncludes(DocumentType.Command, containerGroup);
    }

    public async Task<T> CosmosActionAsync<T>(
        DocumentType documentType,
        AggregateContainerGroup containerGroup,
        Func<Container, Task<T>> cosmosAction)
    {
        try
        {
            var result = await cosmosAction(await GetContainerAsync(documentType, containerGroup));
            return result;
        }
        catch
        {
            ResetMemoryCache(documentType, containerGroup);
            throw;
        }
    }

    private void ResetMemoryCache(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        var containerId = GetContainerId(documentType, containerGroup);
        var databaseId = GetDatabaseId();
        // There may be a network error, so initialize the container.
        // This allows reconnection when recovered next time.
        memoryCache.Cache.Remove(GetMemoryCacheClientKey(documentType, SekibanContextIdentifier()));
        memoryCache.Cache.Remove(GetMemoryCacheDatabaseKey(documentType, databaseId, SekibanContextIdentifier()));
        memoryCache.Cache.Remove(GetMemoryCacheContainerKey(documentType, databaseId, containerId, SekibanContextIdentifier()));
    }

    public async Task CosmosActionAsync(DocumentType documentType, AggregateContainerGroup containerGroup, Func<Container, Task> cosmosAction)
    {
        try
        {
            await cosmosAction(await GetContainerAsync(documentType, containerGroup));
        }
        catch
        {
            // There may be a network error, so initialize the container.
            // This allows reconnection when recovered next time.
            ResetMemoryCache(documentType, containerGroup);
            throw;
        }
    }

    private IReadOnlyList<string> GetPartitionKeyPaths(bool supportsHierarchicalPartitions) =>
        supportsHierarchicalPartitions
            ? new List<string> { "/RootPartitionKey", "/AggregateType", "/PartitionKey" }
            : new List<string> { "/PartitionKey" };
}
