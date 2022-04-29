using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;

namespace CosmosInfrastructure;

public class CosmosDbFactory
{
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;

    public CosmosDbFactory(IConfiguration configuration, IMemoryCache memoryCache)
    {
        _configuration = configuration;
        _memoryCache = memoryCache;
    }
    private string GetContainerId(DocumentType documentType)
    {
        return documentType switch
        {
            DocumentType.AggregateEvent => _configuration.GetValue<string>(
                    "AggregateEventCosmosDbContainer") ??
                _configuration.GetValue<string>("CosmosDbContainer"),
            DocumentType.AggregateCommand => _configuration.GetValue<string>(
                    "AggregateCommandCosmosDbContainer") ??
                _configuration.GetValue<string>("CosmosDbContainer"),
            DocumentType.IntegratedEvent => _configuration.GetValue<string>(
                    "IntegrateEventCosmosDbContainer") ??
                _configuration.GetValue<string>("CosmosDbContainer"),
            DocumentType.IntegratedCommand => _configuration.GetValue<string>(
                    "IntegrateEventCosmosDbContainer") ??
                _configuration.GetValue<string>("CosmosDbContainer"),
            _ => _configuration.GetValue<string>("CosmosDbContainer")
        };
    }
    private static string GetMemoryCacheContainerKey(
        DocumentType documentType,
        string databaseId,
        string containerId) =>
        $"{(documentType == DocumentType.AggregateEvent ? "event." : "")}cosmosdb.container.{databaseId}.{containerId}";
    private static string GetMemoryCacheClientKey(DocumentType documentType) =>
        $"{(documentType == DocumentType.AggregateEvent ? "event." : "")}cosmosdb.client";
    private static string GetMemoryCacheDatabaseKey(DocumentType documentType, string databaseId) =>
        $"{(documentType == DocumentType.AggregateEvent ? "event." : "")}cosmosdb.container.{databaseId}";

    private string GetUri(DocumentType documentType)
        => documentType == DocumentType.AggregateEvent
            ? _configuration.GetValue<string>("EventCosmosDbEndPointUrl") ??
            _configuration.GetValue<string>("CosmosDbEndPointUrl")
            : _configuration.GetValue<string>("CosmosDbEndPointUrl");

    private string GetSecurityKey(DocumentType documentType)
        => documentType == DocumentType.AggregateEvent
            ? _configuration.GetValue<string>("EventCosmosDbAuthorizationKey") ??
            _configuration.GetValue<string>("CosmosDbAuthorizationKey")
            : _configuration.GetValue<string>("CosmosDbAuthorizationKey");

    private string GetDatabaseId(DocumentType documentType)
        => documentType == DocumentType.AggregateEvent
            ? _configuration.GetValue<string>("EventCosmosDbDatabase") ??
            _configuration.GetValue<string>("CosmosDbDatabase")
            : _configuration.GetValue<string>("CosmosDbDatabase");

    private async Task<Container> GetContainerAsync(DocumentType documentType)
    {
        var databaseId = GetDatabaseId(documentType);
        var containerId = GetContainerId(documentType);
        var container =
            (Container?)_memoryCache.Get(
                GetMemoryCacheContainerKey(documentType, databaseId, containerId));

        if (container != null)
        {
            return container;
        }

        var uri = GetUri(documentType);
        var securityKey = GetSecurityKey(documentType);

        var options = new CosmosClientOptions
        {
            Serializer = new ESJsonSerializer(
                new JsonSerializerSettings
                {
                    // TypeNameHandling = TypeNameHandling.Auto
                }
            )
        };
        var client = _memoryCache.Get<CosmosClient?>(GetMemoryCacheClientKey(documentType));
        if (client == null)
        {
            client = new CosmosClient(uri, securityKey, options);
            _memoryCache.Set(GetMemoryCacheClientKey(documentType), client);
        }
        var database =
            _memoryCache.Get<Database?>(GetMemoryCacheDatabaseKey(documentType, databaseId));
        if (database == null)
        {
            database = await client.CreateDatabaseIfNotExistsAsync(databaseId);
            _memoryCache.Set(GetMemoryCacheDatabaseKey(documentType, databaseId), database);
        }

        var containerProperties = new ContainerProperties(containerId, "/partitionkey");
        container = await database.CreateContainerIfNotExistsAsync(
            containerProperties,
            400);
        _memoryCache.Set(
            GetMemoryCacheContainerKey(documentType, databaseId, containerId),
            container);

        return container;
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
    private void ResetMemoryCache(DocumentType documentType)
    {
        var containerId = GetContainerId(documentType);
        var databaseId = GetDatabaseId(documentType);
        // ネットワークエラーの可能性があるので、コンテナを初期化する
        // これによって次回回復したら再接続できる
        _memoryCache.Remove(GetMemoryCacheClientKey(documentType));
        _memoryCache.Remove(GetMemoryCacheDatabaseKey(documentType, databaseId));
        _memoryCache.Remove(GetMemoryCacheContainerKey(documentType, databaseId, containerId));
    }
    public async Task CosmosActionAsync(
        DocumentType documentType,
        Func<Container, Task> cosmosAction)
    {
        try
        {
            await cosmosAction(await GetContainerAsync(documentType));
        }
        catch
        {
            // ネットワークエラーの可能性があるので、コンテナを初期化する
            // これによって次回回復したら再接続できる
            ResetMemoryCache(documentType);
            throw;
        }
    }
}
