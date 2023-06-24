using Microsoft.Azure.Cosmos.Linq;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Cache;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Setting;
using Sekiban.Core.Shared;
using Sekiban.Infrastructure.Cosmos.Lib.Json;
namespace Sekiban.Infrastructure.Cosmos;

public class CosmosDbFactory
{
    private const string SekibanSection = "Sekiban";
    private readonly IConfiguration _configuration;
    private readonly IMemoryCacheAccessor _memoryCache;
    private readonly string _sekibanContextIdentifier;
    private readonly IServiceProvider _serviceProvider;

    private IConfigurationSection? _section
    {
        get
        {
            var section = _configuration.GetSection(SekibanSection);
            var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
            if (!string.IsNullOrEmpty(sekibanContext?.SettingGroupIdentifier))
            {
                section = section?.GetSection(sekibanContext.SettingGroupIdentifier);
            }
            return section;
        }
    }

    public CosmosDbFactory(
        IConfiguration configuration,
        IMemoryCacheAccessor memoryCache,
        ISekibanContext sekibanContext,
        IServiceProvider serviceProvider)
    {
        _configuration = configuration;
        _memoryCache = memoryCache;
        _serviceProvider = serviceProvider;
        _sekibanContextIdentifier = sekibanContext.SettingGroupIdentifier;
    }

    private string GetContainerId(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        return documentType switch
        {
            DocumentType.Event => _section?.GetValue<string>(
                    $"AggregateEventCosmosDbContainer{(containerGroup == AggregateContainerGroup.Dissolvable ? "Dissolvable" : "")}") ??
                _section?.GetValue<string>($"CosmosDbContainer{(containerGroup == AggregateContainerGroup.Dissolvable ? "Dissolvable" : "")}") ??
                _section?.GetValue<string>("CosmosDbContainer") ?? throw new Exception("CosmosDbContainer not found"),
            DocumentType.Command => _section?.GetValue<string>(
                    $"AggregateCommandCosmosDbContainer{(containerGroup == AggregateContainerGroup.Dissolvable ? "Dissolvable" : "")}") ??
                _section?.GetValue<string>($"CosmosDbContainer{(containerGroup == AggregateContainerGroup.Dissolvable ? "dissolvable" : "")}") ??
                _section?.GetValue<string>("CosmosDbContainer") ?? throw new Exception("CosmosDbContainer not found"),
            _ => _section?.GetValue<string>($"CosmosDbContainer{(containerGroup == AggregateContainerGroup.Dissolvable ? "Dissolvable" : "")}") ??
                _section?.GetValue<string>("CosmosDbContainer") ?? throw new Exception("CosmosDbContainer not found")
        };
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

    private string GetUri(DocumentType documentType) =>
        (documentType == DocumentType.Event
            ? _section?.GetValue<string>("EventCosmosDbEndPointUrl") ?? _section?.GetValue<string>("CosmosDbEndPointUrl")
            : _section?.GetValue<string>("CosmosDbEndPointUrl")) ??
        throw new Exception("CosmosDbEndPointUrl not found");

    private string GetSecurityKey(DocumentType documentType) =>
        (documentType == DocumentType.Event
            ? _section?.GetValue<string>("EventCosmosDbAuthorizationKey") ?? _section?.GetValue<string>("CosmosDbAuthorizationKey")
            : _section?.GetValue<string>("CosmosDbAuthorizationKey")) ??
        throw new Exception("CosmosDbAuthorizationKey not found");

    private string GetDatabaseId(DocumentType documentType) =>
        (documentType == DocumentType.Event
            ? _section?.GetValue<string>("EventCosmosDbDatabase") ?? _section?.GetValue<string>("CosmosDbDatabase")
            : _section?.GetValue<string>("CosmosDbDatabase")) ??
        throw new Exception("CosmosDbDatabase not found");

    private async Task<Container> GetContainerAsync(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        var databaseId = GetDatabaseId(documentType);
        var containerId = GetContainerId(documentType, containerGroup);
        var container = (Container?)_memoryCache.Cache.Get(
            GetMemoryCacheContainerKey(documentType, databaseId, containerId, _sekibanContextIdentifier));

        if (container is not null)
        {
            return container;
        }

        var uri = GetUri(documentType);
        var securityKey = GetSecurityKey(documentType);

        var options = new CosmosClientOptions
        {
            Serializer = new SekibanCosmosSerializer(),
            AllowBulkExecution = true,
            // MaxRequestsPerTcpConnection = 200,
            MaxRetryAttemptsOnRateLimitedRequests = 200,
            MaxTcpConnectionsPerEndpoint = 200,
            ConnectionMode = ConnectionMode.Gateway,
            GatewayModeMaxConnectionLimit = 200
        };
        var client = _memoryCache.Cache.Get<CosmosClient?>(GetMemoryCacheClientKey(documentType, _sekibanContextIdentifier));
        if (client is null)
        {
            client = new CosmosClient(uri, securityKey, options);
            _memoryCache.Cache.Set(GetMemoryCacheClientKey(documentType, _sekibanContextIdentifier), client, new MemoryCacheEntryOptions());
        }

        var database = _memoryCache.Cache.Get<Database?>(GetMemoryCacheDatabaseKey(documentType, databaseId, _sekibanContextIdentifier));
        if (database is null)
        {
            database = await client.CreateDatabaseIfNotExistsAsync(databaseId);
            _memoryCache.Cache.Set(
                GetMemoryCacheDatabaseKey(documentType, databaseId, _sekibanContextIdentifier),
                database,
                new MemoryCacheEntryOptions());
        }

        var containerProperties = new ContainerProperties(containerId, GetPartitionKeyPaths());
        container = await database.CreateContainerIfNotExistsAsync(containerProperties, 400);
        _memoryCache.Cache.Set(
            GetMemoryCacheContainerKey(documentType, databaseId, containerId, _sekibanContextIdentifier),
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
        var databaseId = GetDatabaseId(documentType);
        // ネットワークエラーの可能性があるので、コンテナを初期化する
        // これによって次回回復したら再接続できる
        _memoryCache.Cache.Remove(GetMemoryCacheClientKey(documentType, _sekibanContextIdentifier));
        _memoryCache.Cache.Remove(GetMemoryCacheDatabaseKey(documentType, databaseId, _sekibanContextIdentifier));
        _memoryCache.Cache.Remove(GetMemoryCacheContainerKey(documentType, databaseId, containerId, _sekibanContextIdentifier));
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

    private IReadOnlyList<string> GetPartitionKeyPaths() => new List<string> { "/RootPartitionKey", "/AggregateType", "/PartitionKey" };
}
