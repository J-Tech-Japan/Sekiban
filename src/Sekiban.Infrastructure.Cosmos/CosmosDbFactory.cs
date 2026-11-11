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

public class CosmosDbFactory : ICosmosDbFactory, IDisposable
{
    private readonly IServiceProvider serviceProvider;
    private readonly SekibanCosmosClientOptions options;
    private readonly SekibanCosmosDbOptions cosmosDbOptions;
    private readonly CosmosClientStore clientStore;
    private readonly bool ownsClientStore;

    public CosmosDbFactory(
        IMemoryCacheAccessor memoryCache,
        IServiceProvider serviceProvider,
        SekibanCosmosClientOptions options,
        SekibanCosmosDbOptions cosmosDbOptions)
    {
        // Keep the existing constructor; Cosmos handles now follow the client owner's lifetime.
        ArgumentNullException.ThrowIfNull(memoryCache);
        this.serviceProvider = serviceProvider;
        this.options = options;
        this.cosmosDbOptions = cosmosDbOptions;
        var sharedStore = serviceProvider.GetService<CosmosClientStore>();
        ownsClientStore = sharedStore is null;
        clientStore = sharedStore ?? new CosmosClientStore();
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        // DI owns the shared store. A directly constructed factory owns its fallback store.
        if (disposing && ownsClientStore) { clientStore.Dispose(); }
    }

    public Func<Task<CosmosClient?>> SearchCosmosClientAsync { get; set; } = async () =>
    {
        await Task.CompletedTask;
        return null;
    };

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
        return await cosmosAction(await GetContainerAsync(documentType, containerGroup));
    }

    public async Task CosmosActionAsync(DocumentType documentType, AggregateContainerGroup containerGroup, Func<Container, Task> cosmosAction)
    {
        await cosmosAction(await GetContainerAsync(documentType, containerGroup));
    }
    private SekibanAzureCosmosDbOption GetSekibanCosmosDbOption() => cosmosDbOptions.GetContextOption(serviceProvider);
    public string GetContainerId(DocumentType documentType, AggregateContainerGroup containerGroup)
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

    private static object GetMemoryCacheContainerKey(
        DocumentType documentType,
        string databaseId,
        string containerId,
        CosmosClientKey clientKey) =>
        (clientKey, "container", documentType == DocumentType.Event, databaseId, containerId);

    private static object GetMemoryCacheDatabaseKey(
        DocumentType documentType, string databaseId, CosmosClientKey clientKey) =>
        (clientKey, "database", documentType == DocumentType.Event, databaseId);

    private CosmosClientKey GetClientKey()
    {
        var context = SekibanContextIdentifier();
        var setting = cosmosDbOptions.GetContextOption(context);
        return new CosmosClientKey(context, setting.CosmosConnectionString, setting.CosmosEndPointUrl,
            setting.CosmosAuthorizationKey, options.ClientOptions);
    }

    public string GetDatabaseId()
    {
        var dbOption = GetSekibanCosmosDbOption();
        return dbOption.CosmosDatabase ?? string.Empty;
    }

    public Container? GetContainerFromCache(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        var databaseId = GetDatabaseId();
        var containerId = GetContainerId(documentType, containerGroup);
        return (Container?)clientStore.Cache.Get(GetMemoryCacheContainerKey(documentType, databaseId, containerId, GetClientKey()));

    }
    public void SetContainerToCache(DocumentType documentType, AggregateContainerGroup containerGroup, Container container)
    {
        var databaseId = GetDatabaseId();
        var containerId = GetContainerId(documentType, containerGroup);
        clientStore.Cache.Set(
            GetMemoryCacheContainerKey(documentType, databaseId, containerId, GetClientKey()),
            container,
            new MemoryCacheEntryOptions());
    }
    public async Task<Database> GetDatabaseAsync(DocumentType documentType, CosmosClient client)
    {
        var database = clientStore.Cache.Get<Database?>(GetMemoryCacheDatabaseKey(documentType, GetDatabaseId(), GetClientKey()));
        if (database is not null && ReferenceEquals(database.Client, client))
        {
            return database;
        }
        database = await client.CreateDatabaseIfNotExistsAsync(GetDatabaseId());
        clientStore.Cache.Set(
            GetMemoryCacheDatabaseKey(documentType, GetDatabaseId(), GetClientKey()),
            database,
            new MemoryCacheEntryOptions());
        return database;
    }
    public async Task<Container> GetContainerFromDatabaseAsync(DocumentType documentType, AggregateContainerGroup containerGroup, Database database)
    {
        var containerId = GetContainerId(documentType, containerGroup);
        var containerProperties = new ContainerProperties(containerId, GetPartitionKeyPaths(GetSupportsHierarchicalPartitions()));
        var container = await database.CreateContainerIfNotExistsAsync(containerProperties, 400);

        SetContainerToCache(documentType, containerGroup, container);
        return container;
    }

    public async Task<CosmosClient> GetCosmosClientAsync(DocumentType documentType)
    {
        var key = GetClientKey();
        clientStore.ThrowIfDisposed();
        var externalClient = await SearchCosmosClientAsync();
        clientStore.ThrowIfDisposed();
        // Aspire and other external providers retain ownership of the returned client.
        if (externalClient is not null) { return externalClient; }
        return clientStore.GetOrCreate(key, () =>
            !string.IsNullOrWhiteSpace(key.ConnectionString)
                ? new CosmosClient(key.ConnectionString, key.Options)
                : new CosmosClient(key.Endpoint ?? string.Empty, key.AuthorizationKey ?? string.Empty, key.Options));
    }

    public async Task<Container> GetContainerAsync(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        var client = await GetCosmosClientAsync(documentType);
        var container = GetContainerFromCache(documentType, containerGroup);
        if (container is not null && ReferenceEquals(container.Database.Client, client))
        {
            return container;
        }

        var database = await GetDatabaseAsync(documentType, client);
        return await GetContainerFromDatabaseAsync(documentType, containerGroup, database);
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

    public void ResetMemoryCache(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        var containerId = GetContainerId(documentType, containerGroup);
        var databaseId = GetDatabaseId();
        // Explicitly invalidate only lightweight handles; the SDK client manages reconnection.
        clientStore.Cache.Remove(GetMemoryCacheDatabaseKey(documentType, databaseId, GetClientKey()));
        clientStore.Cache.Remove(GetMemoryCacheContainerKey(documentType, databaseId, containerId, GetClientKey()));
    }

    private static IReadOnlyList<string> GetPartitionKeyPaths(bool supportsHierarchicalPartitions) =>
        supportsHierarchicalPartitions ? ["/RootPartitionKey", "/AggregateType", "/PartitionKey"] : ["/PartitionKey"];
}
