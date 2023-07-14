using Amazon;
using Amazon.DynamoDBv2;
using Amazon.DynamoDBv2.DocumentModel;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Cache;
using Sekiban.Core.Documents;
using Sekiban.Core.Events;
using Sekiban.Core.Setting;
using Document = Amazon.DynamoDBv2.DocumentModel.Document;
namespace Sekiban.Infrastructure.Dynamo;

public class DynamoDbFactory
{
    private const string SekibanSection = "Sekiban";
    private readonly IConfiguration _configuration;
    private readonly IMemoryCacheAccessor _memoryCache;
    private readonly string _sekibanContextIdentifier;
    private readonly IServiceProvider _serviceProvider;

    private IConfigurationSection _section
    {
        get
        {
            var section = _configuration.GetSection(SekibanSection);
            var sekibanContext = _serviceProvider.GetService<ISekibanContext>();
            if (!string.IsNullOrEmpty(sekibanContext?.SettingGroupIdentifier))
            {
                section = section.GetSection(sekibanContext.SettingGroupIdentifier);
            }
            return section;
        }
    }

    public DynamoDbFactory(
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

    private string GetTableId(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        return documentType switch
        {
            DocumentType.Event => _section.GetValue<string>(
                    $"DynamoDbEventsTable{(containerGroup == AggregateContainerGroup.Dissolvable ? "Dissolvable" : "")}") ??
                _section.GetValue<string>($"DynamoDbItemsTable{(containerGroup == AggregateContainerGroup.Dissolvable ? "Dissolvable" : "")}") ??
                _section.GetValue<string>("DynamoDbItemsTable") ?? throw new Exception("DynamoDb Table not found"),
            DocumentType.Command => _section.GetValue<string>(
                    $"DynamoDbCommandsTable{(containerGroup == AggregateContainerGroup.Dissolvable ? "Dissolvable" : "")}") ??
                _section.GetValue<string>($"DynamoDbItemsTable{(containerGroup == AggregateContainerGroup.Dissolvable ? "Dissolvable" : "")}") ??
                _section.GetValue<string>("DynamoDbItemsTable") ?? throw new Exception("DynamoDb Container not found"),
            _ => _section.GetValue<string>($"DynamoDbItemsTable{(containerGroup == AggregateContainerGroup.Dissolvable ? "Dissolvable" : "")}") ??
                _section.GetValue<string>("DynamoDbItemsTable") ?? throw new Exception("DynamoDb Container not found")
        };
    }

    private static string GetMemoryCacheTableKey(DocumentType documentType, string tableId, string sekibanContextIdentifier) =>
        $"{(documentType == DocumentType.Event ? "event." : "")}dynamo.table.{tableId}.{sekibanContextIdentifier}";
    private static string GetMemoryCacheClientKey(DocumentType documentType, string sekibanContextIdentifier) =>
        $"{(documentType == DocumentType.Event ? "event." : "")}dynamo.client.{sekibanContextIdentifier}";

    private string GetAwsAccessKeyId(DocumentType documentType) =>
        (documentType == DocumentType.Event
            ? _section.GetValue<string>("AwsAccessKeyIdEvent") ?? _section.GetValue<string>("AwsAccessKeyId")
            : _section.GetValue<string>("AwsAccessKeyId")) ??
        throw new Exception("Dynamo Db Aws AccessKey DbEndPointUrl not found");
    private string GetAwsAccessKey(DocumentType documentType) =>
        (documentType == DocumentType.Event
            ? _section.GetValue<string>("AwsAccessKeyEvent") ?? _section.GetValue<string>("AwsAccessKey")
            : _section.GetValue<string>("AwsAccessKey")) ??
        throw new Exception("CosmosDbEndPointUrl not found");


    private RegionEndpoint GetDynamoDbRegion()
    {
        return RegionEndpoint.EnumerableAllRegions.FirstOrDefault(m => m.SystemName == _section.GetValue<string>("DynamoDbRegion")) ??
            throw new Exception("CosmosDbEndPointUrl not found");
    }

    private async Task<Table> GetTableAsync(DocumentType documentType, AggregateContainerGroup containerGroup)
    {
        var tableId = GetTableId(documentType, containerGroup);
        var tableFromCache = (Table?)_memoryCache.Cache.Get(GetMemoryCacheTableKey(documentType, tableId, _sekibanContextIdentifier));

        if (tableFromCache is not null)
        {
            return tableFromCache;
        }

        var awsAccessKeyId = GetAwsAccessKeyId(documentType);
        var awsAccessKey = GetAwsAccessKey(documentType);
        var region = GetDynamoDbRegion();

        var client = (AmazonDynamoDBClient?)_memoryCache.Cache.Get(GetMemoryCacheClientKey(documentType, _sekibanContextIdentifier)) ??
            new AmazonDynamoDBClient(awsAccessKeyId, awsAccessKey, region);
        if ((AmazonDynamoDBClient?)_memoryCache.Cache.Get(GetMemoryCacheClientKey(documentType, _sekibanContextIdentifier)) == null)
        {
            _memoryCache.Cache.Set(GetMemoryCacheClientKey(documentType, _sekibanContextIdentifier), client);
        }

        var table = Table.LoadTable(client, tableId);

        _memoryCache.Cache.Set(GetMemoryCacheTableKey(documentType, tableId, _sekibanContextIdentifier), table, new MemoryCacheEntryOptions());
        await Task.CompletedTask;
        return table;
    }

    public async Task DeleteAllFromAggregateFromContainerIncludes(
        DocumentType documentType,
        AggregateContainerGroup containerGroup = AggregateContainerGroup.Default)
    {
        await DynamoActionAsync<IEnumerable<IEvent>?>(
            documentType,
            containerGroup,
            async table =>
            {

                var search = table.Scan(new ScanFilter());

                do
                {
                    var batchWriter = table.CreateBatchWrite();
                    List<Document> items = await search.GetNextSetAsync();
                    foreach (var item in items)
                    {
                        batchWriter.AddItemToDelete(item);
                    }
                    await batchWriter.ExecuteAsync();
                } while (!search.IsDone);

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
    public async Task<T> DynamoActionAsync<T>(DocumentType documentType, AggregateContainerGroup containerGroup, Func<Table, Task<T>> cosmosAction)
    {
        try
        {
            var result = await cosmosAction(await GetTableAsync(documentType, containerGroup));
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
        var containerId = GetTableId(documentType, containerGroup);
        // There may be a network error, so initialize the container.
        // This allows reconnection when recovered next time.
        _memoryCache.Cache.Remove(GetMemoryCacheClientKey(documentType, _sekibanContextIdentifier));
        _memoryCache.Cache.Remove(GetMemoryCacheTableKey(documentType, containerId, _sekibanContextIdentifier));
    }

    public async Task DynamoActionAsync(DocumentType documentType, AggregateContainerGroup containerGroup, Func<Table, Task> dynamoAction)
    {
        try
        {
            await dynamoAction(await GetTableAsync(documentType, containerGroup));
        }
        catch
        {
            // There may be a network error, so initialize the container.
            // This allows reconnection when recovered next time.
            ResetMemoryCache(documentType, containerGroup);
            throw;
        }
    }
}
