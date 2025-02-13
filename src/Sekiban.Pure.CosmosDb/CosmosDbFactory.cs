using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Caching.Memory;
using ResultBoxes;
using Sekiban.Pure.Documents;
using Sekiban.Pure.Events;
using Sekiban.Pure.Exceptions;
using System.Text.Json;
namespace Sekiban.Pure.CosmosDb;

public class CosmosDbFactory(
    ICosmosMemoryCacheAccessor cosmosMemoryCache,
    SekibanCosmosClientOptions options,
    SekibanAzureCosmosDbOption sekibanAzureCosmosDbOptions,
    SekibanDomainTypes sekibanDomainTypes)
{
    public Func<Task<CosmosClient?>> SearchCosmosClientAsync { get; set; } = async () =>
    {
        await Task.CompletedTask;
        return null;
    };

    public JsonSerializerOptions GetJsonSerializerOptions() => sekibanDomainTypes.JsonSerializerOptions;

    public async Task DeleteAllFromEventContainer()
    {
        await DeleteAllFromAggregateFromContainerIncludes(DocumentType.Event);
    }

    public async Task<T> CosmosActionAsync<T>(
        DocumentType documentType,
        Func<Container, Task<T>> cosmosAction)
    {
        try
        {
            var result = await cosmosAction(await GetContainerAsync(documentType));
            return result;
        }
        catch
        {
            ResetMemoryCache(documentType);
            throw;
        }
    }

    public async Task CosmosActionAsync(DocumentType documentType, Func<Container, Task> cosmosAction)
    {
        try
        {
            await cosmosAction(await GetContainerAsync(documentType));
        }
        catch
        {
            // There may be a network error, so initialize the container.
            // This allows reconnection when recovered next time.
            ResetMemoryCache(documentType);
            throw;
        }
    }

    public string GetContainerId(DocumentType documentType)
    {
        return documentType switch
        {
            DocumentType.Event => sekibanAzureCosmosDbOptions.CosmosEventsContainer ?? string.Empty,
            _ => sekibanAzureCosmosDbOptions.CosmosItemsContainer ?? string.Empty
        };
    }

    private static string GetMemoryCacheContainerKey(
        DocumentType documentType,
        string databaseId,
        string containerId) =>
        $"{(documentType == DocumentType.Event ? "event." : "")}cosmosdb.container.{databaseId}.{containerId}";

    private static string GetMemoryCacheClientKey(DocumentType documentType) =>
        $"{(documentType == DocumentType.Event ? "event." : "")}cosmosdb.client";

    private static string GetMemoryCacheDatabaseKey(DocumentType documentType, string databaseId) =>
        $"{(documentType == DocumentType.Event ? "event." : "")}cosmosdb.container.{databaseId}";

    private string GetUri() => sekibanAzureCosmosDbOptions.CosmosEndPointUrl ?? string.Empty;

    private string GetSecurityKey() => sekibanAzureCosmosDbOptions.CosmosAuthorizationKey ?? string.Empty;

    private ResultBox<string> GetConnectionString()
    {
        return ResultBox<SekibanAzureCosmosDbOption>
            .FromValue(sekibanAzureCosmosDbOptions)
            .Conveyor(
                azureOptions => azureOptions.CosmosConnectionString switch
                {
                    { } v when !string.IsNullOrWhiteSpace(v) => ResultBox<string>.FromValue(v),
                    _ => new SekibanConfigurationException("CosmosConnectionString is not set.")
                });
    }

    public string GetDatabaseId() => sekibanAzureCosmosDbOptions.CosmosDatabase ?? string.Empty;

    public Container? GetContainerFromCache(DocumentType documentType)
    {
        var databaseId = GetDatabaseId();
        var containerId = GetContainerId(documentType);
        return (Container?)cosmosMemoryCache.Cache.Get(
            GetMemoryCacheContainerKey(documentType, databaseId, containerId));
    }

    public void SetContainerToCache(DocumentType documentType, Container container)
    {
        var databaseId = GetDatabaseId();
        var containerId = GetContainerId(documentType);
        cosmosMemoryCache.Cache.Set(
            GetMemoryCacheContainerKey(documentType, databaseId, containerId),
            container,
            new MemoryCacheEntryOptions());
    }

    public async Task<Database> GetDatabaseAsync(DocumentType documentType, CosmosClient client)
    {
        var database = cosmosMemoryCache.Cache.Get<Database?>(GetMemoryCacheDatabaseKey(documentType, GetDatabaseId()));
        if (database is not null) return database;
        database = await client.CreateDatabaseIfNotExistsAsync(GetDatabaseId());
        cosmosMemoryCache.Cache.Set(
            GetMemoryCacheDatabaseKey(documentType, GetDatabaseId()),
            database,
            new MemoryCacheEntryOptions());
        return database;
    }

    public async Task<Container> GetContainerFromDatabaseAsync(DocumentType documentType, Database database)
    {
        var containerId = GetContainerId(documentType);
        var containerProperties = new ContainerProperties(containerId, GetPartitionKeyPaths());
        var container = await database.CreateContainerIfNotExistsAsync(containerProperties, 400);

        SetContainerToCache(documentType, container);
        return container;
    }

    public async Task<CosmosClient> GetCosmosClientAsync(DocumentType documentType)
    {
        await Task.CompletedTask;
        var client = cosmosMemoryCache.Cache.Get<CosmosClient?>(GetMemoryCacheClientKey(documentType));
        if (client is not null) return client;
        options.ClientOptions.Serializer = new SekibanCosmosSerializer(sekibanDomainTypes.JsonSerializerOptions);
        var clientOptions = options.ClientOptions;
        client = await SearchCosmosClientAsync() ??
            GetConnectionString() switch
            {
                { IsSuccess: true } value => new CosmosClient(value.GetValue(), clientOptions),
                _ => GetCosmosClientFromUriAndKey()
            };
        cosmosMemoryCache.Cache.Set(GetMemoryCacheClientKey(documentType), client, new MemoryCacheEntryOptions());
        return client;
    }

    private CosmosClient GetCosmosClientFromUriAndKey()
    {
        var uri = GetUri();
        var securityKey = GetSecurityKey();
        var clientOptions = options.ClientOptions;
        return new CosmosClient(uri, securityKey, clientOptions);
    }

    public async Task<Container> GetContainerAsync(DocumentType documentType)
    {
        var container = GetContainerFromCache(documentType);
        if (container is not null) return container;
        var client = await GetCosmosClientAsync(documentType);

        var database = await GetDatabaseAsync(documentType, client);
        return await GetContainerFromDatabaseAsync(documentType, database);
    }

    public async Task DeleteAllFromAggregateFromContainerIncludes(DocumentType documentType)
    {
        await CosmosActionAsync<IEnumerable<IEvent>?>(
            documentType,
            async container =>
            {
                var query = container.GetItemLinqQueryable<IDocument>().Where(b => true);
                var feedIterator = container.GetItemQueryIterator<CosmosEventInfo>(query.ToQueryDefinition());

                var deleteItemIds = new List<(Guid id, PartitionKey partitionKey)>();
                while (feedIterator.HasMoreResults)
                {
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        if (item is null) continue;
                        var id = item.Id;
                        var partitionKey = item.PartitionKey;
                        var rootPartitionKey = item.RootPartitionKey;
                        var aggregateType = item.AggregateType;

                        deleteItemIds.Add(
                            (id,
                                new PartitionKeyBuilder()
                                    .Add(rootPartitionKey)
                                    .Add(aggregateType)
                                    .Add(partitionKey)
                                    .Build()));
                    }
                }

                var concurrencyTasks = new List<Task>();
                foreach (var (id, partitionKey) in deleteItemIds)
                    concurrencyTasks.Add(container.DeleteItemAsync<IDocument>(id.ToString(), partitionKey));

                await Task.WhenAll(concurrencyTasks);
                return null;
            });
    }

    public void ResetMemoryCache(DocumentType documentType)
    {
        var containerId = GetContainerId(documentType);
        var databaseId = GetDatabaseId();
        // There may be a network error, so initialize the container.
        // This allows reconnection when recovered next time.
        cosmosMemoryCache.Cache.Remove(GetMemoryCacheClientKey(documentType));
        cosmosMemoryCache.Cache.Remove(GetMemoryCacheDatabaseKey(documentType, databaseId));
        cosmosMemoryCache.Cache.Remove(GetMemoryCacheContainerKey(documentType, databaseId, containerId));
    }

    private static IReadOnlyList<string> GetPartitionKeyPaths() =>
        ["/rootPartitionKey", "/aggregateGroup", "/partitionKey"];
    // private static IReadOnlyList<string> GetPartitionKeyPaths() => ["/RootPartitionKey", "/AggregateGroup", "/PartitionKey"];
}