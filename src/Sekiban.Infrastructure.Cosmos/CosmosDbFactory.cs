using LanguageExt.Common;
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
namespace Sekiban.Infrastructure.Cosmos;

public class CosmosDbFactory(
    IConfiguration configuration,
    IMemoryCacheAccessor memoryCache,
    IServiceProvider serviceProvider,
    SekibanCosmosOptions options)
{
    private const string SekibanSection = "Sekiban";
    private string _sekibanContextIdentifier
    {
        get
        {
            var sekibanContext = serviceProvider.GetService<ISekibanContext>();
            return sekibanContext?.SettingGroupIdentifier ?? SekibanContext.Default;
        }
    }

    private IConfigurationSection _section
    {
        get
        {
            var section = configuration.GetSection(SekibanSection);
            var sekibanContext = serviceProvider.GetService<ISekibanContext>();
            if (!string.IsNullOrEmpty(sekibanContext?.SettingGroupIdentifier))
            {
                section = section.GetSection(sekibanContext.SettingGroupIdentifier);
            }
            return section;
        }
    }

    private string GetContainerId(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        return documentType switch
        {
            DocumentType.Event => _section.GetValue<string>(
                    $"AggregateEventCosmosDbContainer{(containerGroup == AggregateContainerGroup.Dissolvable ? "Dissolvable" : "")}") ??
                _section.GetValue<string>($"CosmosDbContainer{(containerGroup == AggregateContainerGroup.Dissolvable ? "Dissolvable" : "")}") ??
                _section.GetValue<string>("CosmosDbContainer") ?? throw new Exception("CosmosDbContainer not found"),
            DocumentType.Command => _section.GetValue<string>(
                    $"AggregateCommandCosmosDbContainer{(containerGroup == AggregateContainerGroup.Dissolvable ? "Dissolvable" : "")}") ??
                _section.GetValue<string>($"CosmosDbContainer{(containerGroup == AggregateContainerGroup.Dissolvable ? "dissolvable" : "")}") ??
                _section.GetValue<string>("CosmosDbContainer") ?? throw new Exception("CosmosDbContainer not found"),
            _ => _section.GetValue<string>($"CosmosDbContainer{(containerGroup == AggregateContainerGroup.Dissolvable ? "Dissolvable" : "")}") ??
                _section.GetValue<string>("CosmosDbContainer") ?? throw new Exception("CosmosDbContainer not found")
        };
    }

    private bool GetSupportsHierarchicalPartitions()
    {
        var legacy = _section.GetValue<bool?>("LegacyPartitions") ?? false;
        return !legacy;
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
            ? _section.GetValue<string>("EventCosmosDbEndPointUrl") ?? _section.GetValue<string>("CosmosDbEndPointUrl")
            : _section.GetValue<string>("CosmosDbEndPointUrl")) ??
        throw new Exception("CosmosDbEndPointUrl not found");

    private string GetSecurityKey(DocumentType documentType) =>
        (documentType == DocumentType.Event
            ? _section.GetValue<string>("EventCosmosDbAuthorizationKey") ?? _section.GetValue<string>("CosmosDbAuthorizationKey")
            : _section.GetValue<string>("CosmosDbAuthorizationKey")) ??
        throw new Exception("CosmosDbAuthorizationKey not found");

    private Result<string> GetConnectionString(DocumentType documentType) =>
        (documentType == DocumentType.Event
            ? _section.GetValue<string>("EventCosmosDbConnectionString") ?? _section.GetValue<string>("CosmosDbConnectionString")
            : _section.GetValue<string>("CosmosDbConnectionString")) ??
        new Result<string>(new Exception("CosmosDbConnectionString not found"));

    private string GetDatabaseId(DocumentType documentType) =>
        (documentType == DocumentType.Event
            ? _section.GetValue<string>("EventCosmosDbDatabase") ?? _section.GetValue<string>("CosmosDbDatabase")
            : _section.GetValue<string>("CosmosDbDatabase")) ??
        throw new Exception("CosmosDbDatabase not found");

    private async Task<Container> GetContainerAsync(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        var databaseId = GetDatabaseId(documentType);
        var containerId = GetContainerId(documentType, containerGroup);
        var container = (Container?)memoryCache.Cache.Get(
            GetMemoryCacheContainerKey(documentType, databaseId, containerId, _sekibanContextIdentifier));

        if (container is not null)
        {
            return container;
        }
        var client = memoryCache.Cache.Get<CosmosClient?>(GetMemoryCacheClientKey(documentType, _sekibanContextIdentifier));
        if (client is null)
        {
            var clientOptions = options.ClientOptions;
            var connectionString = GetConnectionString(documentType);
            client = connectionString.Match(
                v => new CosmosClient(v, clientOptions),
                _ =>
                {
                    var uri = GetUri(documentType);
                    var securityKey = GetSecurityKey(documentType);
                    return new CosmosClient(uri, securityKey, clientOptions);
                });
            memoryCache.Cache.Set(GetMemoryCacheClientKey(documentType, _sekibanContextIdentifier), client, new MemoryCacheEntryOptions());
        }

        var database = memoryCache.Cache.Get<Database?>(GetMemoryCacheDatabaseKey(documentType, databaseId, _sekibanContextIdentifier));
        if (database is null)
        {
            database = await client.CreateDatabaseIfNotExistsAsync(databaseId);
            memoryCache.Cache.Set(
                GetMemoryCacheDatabaseKey(documentType, databaseId, _sekibanContextIdentifier),
                database,
                new MemoryCacheEntryOptions());
        }

        var containerProperties = new ContainerProperties(containerId, GetPartitionKeyPaths(GetSupportsHierarchicalPartitions()));
        container = await database.CreateContainerIfNotExistsAsync(containerProperties, 400);
        memoryCache.Cache.Set(
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
        // There may be a network error, so initialize the container.
        // This allows reconnection when recovered next time.
        memoryCache.Cache.Remove(GetMemoryCacheClientKey(documentType, _sekibanContextIdentifier));
        memoryCache.Cache.Remove(GetMemoryCacheDatabaseKey(documentType, databaseId, _sekibanContextIdentifier));
        memoryCache.Cache.Remove(GetMemoryCacheContainerKey(documentType, databaseId, containerId, _sekibanContextIdentifier));
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
