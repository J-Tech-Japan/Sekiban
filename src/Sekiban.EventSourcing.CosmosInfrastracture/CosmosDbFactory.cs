using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
namespace CosmosInfrastructure;

public class CosmosDbFactory
{
    private const string SekibanSection = "Sekiban";
    private readonly IConfiguration _configuration;
    private readonly IMemoryCache _memoryCache;
    public CosmosDbFactory(IConfiguration configuration, IMemoryCache memoryCache)
    {
        _configuration = configuration;
        _memoryCache = memoryCache;
    }
    private string GetContainerId(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        return documentType switch
        {
            DocumentType.AggregateEvent => _configuration.GetSection(SekibanSection)
                    .GetValue<string>(
                        $"AggregateEventCosmosDbContainer{(containerGroup == AggregateContainerGroup.Dissolvable ? "Dissolvable" : "")}") ??
                _configuration.GetSection(SekibanSection)
                    .GetValue<string>($"CosmosDbContainer{(containerGroup == AggregateContainerGroup.Dissolvable ? "Dissolvable" : "")}") ??
                _configuration.GetSection(SekibanSection).GetValue<string>("CosmosDbContainer"),
            DocumentType.AggregateCommand => _configuration.GetSection(SekibanSection)
                    .GetValue<string>(
                        $"AggregateCommandCosmosDbContainer{(containerGroup == AggregateContainerGroup.Dissolvable ? "Dissolvable" : "")}") ??
                _configuration.GetSection(SekibanSection)
                    .GetValue<string>($"CosmosDbContainer{(containerGroup == AggregateContainerGroup.Dissolvable ? "dissolvable" : "")}") ??
                _configuration.GetSection(SekibanSection).GetValue<string>("CosmosDbContainer"),
            _ => _configuration.GetSection(SekibanSection)
                    .GetValue<string>($"CosmosDbContainer{(containerGroup == AggregateContainerGroup.Dissolvable ? "Dissolvable" : "")}") ??
                _configuration.GetSection(SekibanSection).GetValue<string>("CosmosDbContainer")
        };
    }
    private static string GetMemoryCacheContainerKey(DocumentType documentType, string databaseId, string containerId) =>
        $"{(documentType == DocumentType.AggregateEvent ? "event." : "")}cosmosdb.container.{databaseId}.{containerId}";
    private static string GetMemoryCacheClientKey(DocumentType documentType) =>
        $"{(documentType == DocumentType.AggregateEvent ? "event." : "")}cosmosdb.client";
    private static string GetMemoryCacheDatabaseKey(DocumentType documentType, string databaseId) =>
        $"{(documentType == DocumentType.AggregateEvent ? "event." : "")}cosmosdb.container.{databaseId}";

    private string GetUri(DocumentType documentType) =>
        documentType == DocumentType.AggregateEvent
            ? _configuration.GetSection(SekibanSection).GetValue<string>("EventCosmosDbEndPointUrl") ??
            _configuration.GetSection(SekibanSection).GetValue<string>("CosmosDbEndPointUrl")
            : _configuration.GetSection(SekibanSection).GetValue<string>("CosmosDbEndPointUrl");

    private string GetSecurityKey(DocumentType documentType) =>
        documentType == DocumentType.AggregateEvent
            ? _configuration.GetSection(SekibanSection).GetValue<string>("EventCosmosDbAuthorizationKey") ??
            _configuration.GetSection(SekibanSection).GetValue<string>("CosmosDbAuthorizationKey")
            : _configuration.GetSection(SekibanSection).GetValue<string>("CosmosDbAuthorizationKey");

    private string GetDatabaseId(DocumentType documentType) =>
        documentType == DocumentType.AggregateEvent
            ? _configuration.GetSection(SekibanSection).GetValue<string>("EventCosmosDbDatabase") ??
            _configuration.GetSection(SekibanSection).GetValue<string>("CosmosDbDatabase")
            : _configuration.GetSection(SekibanSection).GetValue<string>("CosmosDbDatabase");

    private async Task<Container> GetContainerAsync(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        var databaseId = GetDatabaseId(documentType);
        var containerId = GetContainerId(documentType, containerGroup);
        var container = (Container?)_memoryCache.Get(GetMemoryCacheContainerKey(documentType, databaseId, containerId));

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
                    DateFormatString = "yyyy-MM-dd'T'HH:mm:ss.ffffff'Z'"
                })
        };
        var client = _memoryCache.Get<CosmosClient?>(GetMemoryCacheClientKey(documentType));
        if (client == null)
        {
            client = new CosmosClient(uri, securityKey, options);
            _memoryCache.Set(GetMemoryCacheClientKey(documentType), client);
        }
        var database = _memoryCache.Get<Database?>(GetMemoryCacheDatabaseKey(documentType, databaseId));
        if (database == null)
        {
            database = await client.CreateDatabaseIfNotExistsAsync(databaseId);
            _memoryCache.Set(GetMemoryCacheDatabaseKey(documentType, databaseId), database);
        }

        var containerProperties = new ContainerProperties(containerId, "/partitionkey");
        container = await database.CreateContainerIfNotExistsAsync(containerProperties, 400);
        _memoryCache.Set(GetMemoryCacheContainerKey(documentType, databaseId, containerId), container);

        return container;
    }

    public async Task DeleteAllFromAggregateFromContainerIncludes(
        DocumentType documentType,
        AggregateContainerGroup containerGroup = AggregateContainerGroup.Default)
    {
        await CosmosActionAsync<IEnumerable<AggregateEvent>?>(
            documentType,
            containerGroup,
            async container =>
            {
                var query = container.GetItemLinqQueryable<Document>().Where(b => true);
                var feedIterator = container.GetItemQueryIterator<dynamic>(query.ToQueryDefinition());
                var todelete = new List<Document>();
                while (feedIterator.HasMoreResults)
                {
                    var response = await feedIterator.ReadNextAsync();
                    foreach (var item in response)
                    {
                        if (item == null) { continue; }
                        if (item is not JObject jobj) { continue; }
                        todelete.Add(jobj.ToObject<Document>() ?? throw new Exception());
                    }
                }
                foreach (var d in todelete)
                {
                    await container.DeleteItemAsync<Document>(d.Id.ToString(), new PartitionKey(d.PartitionKey));
                }
                return null;
            });
    }
    public async Task DeleteAllFromAggregateEventContainer(AggregateContainerGroup containerGroup)
    {
        await DeleteAllFromAggregateFromContainerIncludes(DocumentType.AggregateEvent, containerGroup);
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
        var databaseId = GetDatabaseId(documentType);
        // ネットワークエラーの可能性があるので、コンテナを初期化する
        // これによって次回回復したら再接続できる
        _memoryCache.Remove(GetMemoryCacheClientKey(documentType));
        _memoryCache.Remove(GetMemoryCacheDatabaseKey(documentType, databaseId));
        _memoryCache.Remove(GetMemoryCacheContainerKey(documentType, databaseId, containerId));
    }
    public async Task CosmosActionAsync(DocumentType documentType, AggregateContainerGroup containerGroup, Func<Container, Task> cosmosAction)
    {
        try
        {
            await cosmosAction(await GetContainerAsync(documentType, containerGroup));
        }
        catch
        {
            // ネットワークエラーの可能性があるので、コンテナを初期化する
            // これによって次回回復したら再接続できる
            ResetMemoryCache(documentType, containerGroup);
            throw;
        }
    }
}
